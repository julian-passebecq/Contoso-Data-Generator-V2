#!/usr/bin/env python3
"""Explicit, bounded BigQuery ML execution after measured Gold; never runs at generation."""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import sys
from datetime import timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "colab"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "gcp"))
from work_order import read_json, write_json, table_prefix, timestamp, utcnow, sha256, reconcile
from bigquery_runtime import validate_config, preflight, query_measured

FEATURES = ["sales_amount", "item_quantity", "store_channel", "customer_loyalty_tier_as_of_order",
            "promised_transit_hours", "actual_transit_hours", "delivery_delay_hours", "is_on_time",
            "shipment_event_count_at_delivery"]
LABEL = "is_dissatisfied_14d"


def classification_metrics(rows):
    pairs = [(int(r[LABEL]), float(r["probability"])) for r in rows]
    if not pairs or {y for y, _ in pairs} != {0, 1}:
        raise ValueError("Evaluation needs both label classes; increase the generated sample/time span")
    if any(not math.isfinite(p) or not 0 <= p <= 1 for _, p in pairs):
        raise ValueError("Predictions must contain finite probabilities in [0,1]")
    positives = sum(y for y, _ in pairs)
    negatives = len(pairs) - positives
    tp = sum(y == 1 and p >= .5 for y, p in pairs)
    fp = sum(y == 0 and p >= .5 for y, p in pairs)
    precision = tp / (tp + fp) if tp + fp else 0.0
    recall = tp / positives
    # Group tied thresholds, matching the non-interpolated average precision definition.
    grouped = {}
    for y, p in pairs:
        group = grouped.setdefault(p, [0, 0]); group[0] += y; group[1] += 1
    seen = positive_seen = 0
    ap = 0.0
    for p in sorted(grouped, reverse=True):
        hit, count = grouped[p]; seen += count; positive_seen += hit
        ap += (hit / positives) * (positive_seen / seen)
    wins = 0.0
    negatives_below = 0
    for p in sorted(grouped):
        hit, count = grouped[p]
        wins += hit * (negatives_below + .5 * (count-hit))
        negatives_below += count-hit
    return {"rows": len(pairs), "positiveRows": positives, "averagePrecision": ap,
            "rocAuc": wins / (positives * negatives), "precision": precision, "recall": recall,
            "f1": 2 * precision * recall / (precision + recall) if precision + recall else 0.0,
            "balancedAccuracy": .5 * (recall + (negatives-fp) / negatives),
            "logLoss": -sum(y * math.log(max(p, 1e-15)) + (1-y) * math.log(max(1-p, 1e-15)) for y, p in pairs) / len(pairs),
            "brierScore": sum((p-y)**2 for y, p in pairs) / len(pairs)}


def prepare(root, order, label_as_of, model_type):
    root = Path(root)
    config = read_json(root / "gcp/bigquery_config.json")
    gcp = validate_config(config)
    if any(order.get("gcp", {}).get(k) != gcp[k] for k in ("projectId", "dataset", "location")):
        raise ValueError("ML destination differs from the issued work order")
    if model_type not in ("logistic", "boosted-tree"):
        raise ValueError("Unsupported BigQuery ML model type")
    cutoff = timestamp(label_as_of).astimezone(timezone.utc).isoformat()
    dataset = f"{gcp['projectId']}.{gcp['dataset']}"
    prefix = table_prefix(order)
    query = (root / "bqml/features.sql").read_text(encoding="utf-8")
    query = query.replace("{{dataset}}", dataset).replace("{{prefix}}", prefix).replace("{{label_as_of}}", cutoff)
    identity = hashlib.sha256((query + model_type).encode()).hexdigest()[:12]
    model = f"{dataset}.{prefix}ml_{model_type.replace('-', '_')}_{identity}"
    training = f"SELECT {', '.join(FEATURES)}, {LABEL}, split_name='validation' AS is_evaluation FROM ({query}) WHERE split_name IN ('train','validation')"
    options = "MODEL_TYPE='LOGISTIC_REG', MAX_ITERATIONS=20, L2_REG=1.0" if model_type == "logistic" else "MODEL_TYPE='BOOSTED_TREE_CLASSIFIER', MAX_ITERATIONS=20, MAX_TREE_DEPTH=4"
    create = f"CREATE MODEL `{model}` OPTIONS({options}, INPUT_LABEL_COLS=['{LABEL}'], DATA_SPLIT_METHOD='CUSTOM', DATA_SPLIT_COL='is_evaluation', ENABLE_GLOBAL_EXPLAIN=TRUE) AS {training}"
    return config, query, model, create


def run(root, work_order, result, label_as_of, model_type="logistic", execute=False, allow_training_cost=False):
    root = Path(root).resolve()
    order = read_json(work_order)
    config, features, model, create = prepare(root, order, label_as_of, model_type)
    output = root / "bqml" / model.rsplit('.', 1)[-1]
    output.mkdir(parents=True, exist_ok=True)
    (output / "train.sql").write_text(create + ";\n", encoding="utf-8")
    if not execute:
        report = {"status": "generated-reference", "cloudExecutionVerified": False, "model": model,
                  "features": FEATURES, "labelAsOf": label_as_of, "trainingSqlSha256": sha256(output / "train.sql")}
        write_json(output / "plan.json", report)
        return report
    if not allow_training_cost:
        raise ValueError("CREATE MODEL has ML-specific pricing/quota. Inspect train.sql and pass --allow-training-cost explicitly; maximumBytesBilled is not a total ML cost cap")
    observed = read_json(result)
    reconcile(root, order, observed, allow_completed_expired=True)
    if observed.get("resultScope") != "spark-and-bigquery" or observed.get("bigQueryEvidence", {}).get("executionOrigin") != "google-bigquery-api":
        raise ValueError("ML requires real native BigQuery reconciliation")
    gold_path = root / "dbt_bigquery/gold_evidence.json"
    gold = read_json(gold_path)
    if gold.get("status") != "succeeded" or not gold.get("cloudExecutionVerified") or gold.get("workOrderId") != order["workOrderId"] or gold.get("loadResultSha256") != sha256(result):
        raise ValueError("Build and reconcile BigQuery Gold for this work order first")
    for path, digest in gold["authoredFileSha256"].items():
        from work_order import safe_path
        if sha256(safe_path(root / "dbt_bigquery", path)) != digest:
            raise ValueError("Gold SQL changed after its measured build")
    from google.cloud import bigquery
    client = bigquery.Client(project=config["gcp"]["projectId"], location=config["gcp"]["location"])
    preflight(client, config)
    jobs = {}
    def query(sql, key):
        jobs[key] = {}
        rows, _ = query_measured(client, sql, config, bigquery, jobs[key])
        return rows
    started = utcnow().isoformat()
    report = {"contractVersion": "1.3", "status": "failed", "cloudExecutionVerified": False,
              "workOrderId": order["workOrderId"], "datasetFingerprint": order["datasetFingerprint"],
              "model": model, "modelType": model_type, "features": FEATURES, "labelAsOf": label_as_of,
              "startedAt": started, "goldEvidenceSha256": sha256(gold_path), "jobs": jobs}
    try:
        splits = query(f"SELECT split_name, {LABEL}, COUNT(*) AS row_count FROM ({features}) GROUP BY 1,2", "split_preflight")
        report["splitCounts"] = [dict(row) for row in splits]
        for split in ("train", "validation", "test"):
            counts = {int(r[LABEL]): int(r["row_count"]) for r in splits if r["split_name"] == split}
            if set(counts) != {0, 1} or min(counts.values()) < 2:
                raise ValueError(f"{split} needs at least two rows of each label after chronological splitting/14-day embargo; increase sample/time span")
        # Never CREATE OR REPLACE: repeated execution cannot overwrite a trained model.
        query(create, "train")
        test = f"SELECT {', '.join(FEATURES)}, {LABEL} FROM ({features}) WHERE split_name='test'"
        report["bigQueryEvaluation"] = [dict(row) for row in query(f"SELECT * FROM ML.EVALUATE(MODEL `{model}`, ({test}))", "evaluate")]
        prediction = f"SELECT order_key, {LABEL}, store_channel, country_code, customer_loyalty_tier_as_of_order, delivery_delay_hours, (SELECT prob FROM UNNEST(predicted_{LABEL}_probs) WHERE CAST(label AS STRING)='1') AS probability FROM ML.PREDICT(MODEL `{model}`, (SELECT * FROM ({features}) WHERE split_name='test'))"
        predictions = [dict(row) for row in query(prediction, "predict")]
        report["metrics"] = classification_metrics(predictions)
        positive_rate = sum(int(r[LABEL]) for r in predictions) / len(predictions)
        report["testPositiveRate"] = positive_rate
        train_counts = [r for r in splits if r['split_name']=='train']
        train_rate = sum(int(r[LABEL])*int(r['row_count']) for r in train_counts)/sum(int(r['row_count']) for r in train_counts)
        report["majorityBaseline"] = classification_metrics([{LABEL: r[LABEL], "probability": train_rate} for r in predictions])
        report["delayHeuristic"] = classification_metrics([{LABEL: r[LABEL], "probability": .8 if r["delivery_delay_hours"] > 0 else .2} for r in predictions])
        report["slices"] = {}
        for column in ("store_channel", "country_code", "customer_loyalty_tier_as_of_order"):
            for value in sorted({str(r[column]) for r in predictions}):
                subset = [r for r in predictions if str(r[column]) == value]
                report["slices"][column+":"+value] = classification_metrics(subset) if {r[LABEL] for r in subset} == {0,1} else {"rows": len(subset), "status": "single-class-insufficient"}
        import pyarrow as pa
        import pyarrow.parquet as pq
        pq.write_table(pa.Table.from_pylist(predictions), output / "predictions.parquet")
        report["predictionsSha256"] = sha256(output / "predictions.parquet")
        report["featureImportance"] = [dict(row) for row in query(f"SELECT * FROM ML.GLOBAL_EXPLAIN(MODEL `{model}`)", "explain")]
        report.update(status="succeeded", cloudExecutionVerified=True)
    except Exception as error:
        report["error"] = type(error).__name__ + ": " + str(error)
        raise
    finally:
        report["completedAt"] = utcnow().isoformat()
        write_json(output / "metrics.json", report)
        (output / "model_card.md").write_text("# Contoso Forge BigQuery ML\n\nStatus: " + report["status"] + "\n\nModel: " + model + "\n\nSynthetic educational classifier, with selective outcome observation; no production quality claim. Features use delivery-time availability, labels close after 14 days, and chronological partitions have a 14-day label embargo. Review/support values only define labels. Reviews/support have no separate ingestion column in V1, so availability is their event/closed timestamp; CDC/shipment events enforce both timestamps. Inspect metrics.json for actual jobs, splits, holdout metrics, baselines and slices.\n", encoding="utf-8")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=".")
    parser.add_argument("--work-order", default="colab/work_order.json")
    parser.add_argument("--result", default="colab/result_manifest.json")
    parser.add_argument("--label-as-of", required=True, help="ISO UTC label observation cutoff")
    parser.add_argument("--model", choices=("logistic", "boosted-tree"), default="logistic")
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--allow-training-cost", action="store_true")
    args = parser.parse_args()
    run(args.root, args.work_order, args.result, args.label_as_of, args.model, args.execute, args.allow_training_cost)
