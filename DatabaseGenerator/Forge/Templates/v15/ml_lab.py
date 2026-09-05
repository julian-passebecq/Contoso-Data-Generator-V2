"""Bounded, transparent ML Lab; no search service, random temporal splits or post-outcome features."""
import argparse
import json
from pathlib import Path
import numpy as np
import pandas as pd
from sklearn.compose import ColumnTransformer
from sklearn.dummy import DummyClassifier, DummyRegressor
from sklearn.ensemble import RandomForestClassifier, HistGradientBoostingClassifier, RandomForestRegressor, HistGradientBoostingRegressor, IsolationForest
from sklearn.cluster import KMeans
from sklearn.impute import SimpleImputer
from sklearn.linear_model import LogisticRegression, Ridge, LinearRegression
from sklearn.pipeline import Pipeline
from sklearn.preprocessing import OneHotEncoder, StandardScaler
from sklearn.inspection import permutation_importance
from sklearn.metrics import average_precision_score, roc_auc_score, f1_score, precision_score, recall_score, confusion_matrix, precision_recall_curve
from common import read, write, sha, now

CATEGORICAL = ["store_channel", "country_code", "customer_loyalty_tier_as_of_order"]
NUMERIC = ["sales_amount", "item_quantity", "promised_transit_hours", "actual_transit_hours", "delivery_delay_hours", "is_on_time", "shipment_event_count_at_delivery"]
FEATURES = NUMERIC + CATEGORICAL
TARGET = "is_dissatisfied_14d"
BASELINE_THRESHOLD = 0.5


def algorithms(problem, seed):
    """Small appropriate candidate families; only classification has a V1.5 generated scenario."""
    if problem == "binary_classification":
        return {"dummy": DummyClassifier(strategy="prior"), "logistic_regression": LogisticRegression(max_iter=500, random_state=seed),
                "random_forest": RandomForestClassifier(n_estimators=100, min_samples_leaf=3, random_state=seed, n_jobs=1),
                "histogram_gradient_boosting": HistGradientBoostingClassifier(max_iter=100, max_leaf_nodes=15, random_state=seed)}
    if problem == "regression":
        return {"dummy": DummyRegressor(), "linear": LinearRegression(), "ridge": Ridge(),
                "random_forest": RandomForestRegressor(n_estimators=100, random_state=seed, n_jobs=1), "histogram_gradient_boosting": HistGradientBoostingRegressor(random_state=seed)}
    if problem == "clustering": return {"kmeans": KMeans(n_clusters=3, random_state=seed, n_init=10)}
    if problem == "anomaly_detection": return {"isolation_forest": IsolationForest(random_state=seed, contamination="auto", n_jobs=1)}
    raise ValueError("Unsupported ML problem type")


def temporal_split(frame, label_as_of):
    frame = frame.copy()
    for column in ("prediction_time", "label_timestamp"):
        frame[column] = pd.to_datetime(frame[column], utc=True, errors="raise")
    cutoff = pd.Timestamp(label_as_of)
    if cutoff.tzinfo is None: raise ValueError("Label cutoff must be timezone-aware")
    if frame["order_key"].duplicated().any(): raise ValueError("Prediction grain must be one row per order")
    if frame[["prediction_time", "label_timestamp"]].isna().any().any(): raise ValueError("Missing prediction/label timestamp")
    if (frame["label_timestamp"] != frame["prediction_time"] + pd.Timedelta(days=14)).any():
        raise ValueError("The dissatisfaction label must mature exactly 14 days after prediction")
    frame = frame[frame["label_timestamp"] <= cutoff].sort_values(["prediction_time", "order_key"]).reset_index(drop=True)
    if len(frame) < 30: raise ValueError("Insufficient mature rows for three chronological partitions")
    # Timestamp boundaries keep ties in one partition and order keys out of the estimator.
    validation_start = frame.iloc[int(len(frame)*0.70)]["prediction_time"]
    test_start = frame.iloc[int(len(frame)*0.85)]["prediction_time"]
    frame["split_name"] = np.where(frame.prediction_time < validation_start, "train", np.where(frame.prediction_time < test_start, "validation", "test"))
    frame = frame[((frame.split_name == "train") & (frame.label_timestamp < validation_start))
                  | ((frame.split_name == "validation") & (frame.label_timestamp < test_start)) | (frame.split_name == "test")].copy()
    partitions = {}
    for name in ("train", "validation", "test"):
        subset = frame[frame.split_name == name]
        counts = subset[TARGET].value_counts().to_dict()
        if set(counts) != {0, 1} or min(counts.values()) < 2:
            raise ValueError(f"Insufficient class distribution in {name}: {counts}; both classes need at least two rows")
        partitions[name] = {"rows": len(subset), "negative": int(counts[0]), "positive": int(counts[1]),
                            "predictionStart": subset.prediction_time.min().isoformat(), "predictionEnd": subset.prediction_time.max().isoformat(),
                            "latestLabel": subset.label_timestamp.max().isoformat(), "prevalence": float(subset[TARGET].mean())}
    return frame, partitions


def evaluate(y, probability, threshold=0.5):
    predicted = probability >= threshold
    precision, recall, thresholds = precision_recall_curve(y, probability)
    return {"average_precision": float(average_precision_score(y, probability)), "roc_auc": float(roc_auc_score(y, probability)),
            "f1": float(f1_score(y, predicted, zero_division=0)), "precision": float(precision_score(y, predicted, zero_division=0)),
            "recall": float(recall_score(y, predicted, zero_division=0)), "threshold": threshold,
            "confusion_matrix": confusion_matrix(y, predicted, labels=[0, 1]).tolist(),
            "pr_curve": {"precision": precision.tolist(), "recall": recall.tolist(), "thresholds": thresholds.tolist()}}


def select_validation_threshold(y, probability):
    """Maximize validation F1; ties prefer closest to 0.5, then the higher threshold.

    The caller supplies validation only. No test data or estimator fitting occurs here.
    Include all distinct validation scores and 0/0.5/1; >= is the decision rule.
    """
    y, probability = np.asarray(y), np.asarray(probability, dtype=float)
    if y.ndim != 1 or probability.shape != y.shape or set(np.unique(y)) != {0, 1}:
        raise ValueError("Threshold selection requires aligned binary validation labels with both classes")
    if not np.isfinite(probability).all() or ((probability < 0) | (probability > 1)).any():
        raise ValueError("Validation probabilities must be finite and in [0, 1]")
    order = np.argsort(probability, kind="stable")
    scores, labels = probability[order], y[order]
    cumulative = np.r_[0, np.cumsum(labels)]
    positives, negatives = int(y.sum()), int(len(y) - y.sum())
    rows = []
    for threshold in np.unique(np.r_[scores, 0.0, BASELINE_THRESHOLD, 1.0]):
        below = int(np.searchsorted(scores, threshold, side="left"))
        fn = int(cumulative[below])
        tn, tp = below - fn, positives - fn
        fp = negatives - tn
        rows.append({"threshold": float(threshold), "precision": tp / (tp + fp) if tp + fp else 0.0,
                     "recall": tp / positives, "f1": 2 * tp / (2 * tp + fp + fn),
                     "confusion_matrix": [[tn, fp], [fn, tp]]})
    chosen = max(rows, key=lambda r: (r["f1"], -abs(r["threshold"] - BASELINE_THRESHOLD), r["threshold"]))
    return {"selectedOn": "validation", "objective": "maximum F1", "decisionRule": "probability >= threshold",
            "tieBreak": "closest to 0.5, then higher threshold", "threshold": chosen["threshold"],
            "validationTradeoff": rows, "validation": evaluate(y, probability, chosen["threshold"])}


def prediction_rows(selected, name, probability, threshold):
    batch = selected[["order_key", "prediction_time", "label_timestamp", "split_name", TARGET]].copy()
    batch["algorithm"], batch["probability"] = name, probability
    batch["prediction"] = (probability >= BASELINE_THRESHOLD).astype(int)
    batch["selected_threshold"] = threshold
    batch["prediction_at_selected_threshold"] = (probability >= threshold).astype(int)
    return batch


def train_frame(frame, config, spec, output, identity):
    output.mkdir(parents=True, exist_ok=True)
    if set(spec["features"]) != set(FEATURES) or set(FEATURES) & set(spec["leakageExclusions"]):
        raise ValueError("ML feature allowlist/spec mismatch or leakage")
    if not set(FEATURES + [TARGET, "prediction_time", "label_timestamp", "order_key"]).issubset(frame.columns):
        raise ValueError("Missing canonical Gold feature columns")
    size = int(frame.memory_usage(deep=True).sum())
    if size > config["materializationLimitMb"] * 1024 * 1024:
        raise ValueError("Feature mart exceeds the configured materialization limit")
    frame, partitions = temporal_split(frame, config["labelAsOf"])
    write(output / "spec.json", spec)
    write(output / "run_config.json", config)
    write(output / "features.json", {"selected": FEATURES, "availability": spec["featureAvailability"], "materializedBytes": size})
    write(output / "leakage_report.json", {"status": "passed", "featureAllowlist": FEATURES, "excluded": spec["leakageExclusions"],
          "splitStrategy": "chronological", "embargoDays": 14, "partitions": partitions,
          "pointInTimeEnforcement": "dbt Gold filters shipment event time AND ingestion time and customer CDC availability"})
    train = frame[frame.split_name == "train"]
    validation = frame[frame.split_name == "validation"]
    models, fitted, threshold_analysis, predictions, importance = {}, {}, {}, [], []
    for name, estimator in algorithms(spec["problemType"], config["seed"]).items():
        prep = ColumnTransformer([
            ("numeric", Pipeline([("impute", SimpleImputer(strategy="median")), ("scale", StandardScaler())]), NUMERIC),
            ("category", Pipeline([("impute", SimpleImputer(strategy="most_frequent")), ("encode", OneHotEncoder(handle_unknown="ignore", sparse_output=False))]), CATEGORICAL)
        ])
        model = Pipeline([("preprocessing", prep), ("estimator", estimator)])
        model.fit(train[FEATURES], train[TARGET])
        fitted[name] = model
        probability = model.predict_proba(validation[FEATURES])[:, 1]
        models[name] = {"validation": evaluate(validation[TARGET], probability, BASELINE_THRESHOLD)}
        threshold_analysis[name] = select_validation_threshold(validation[TARGET], probability)
        predictions.append(prediction_rows(validation, name, probability, threshold_analysis[name]["threshold"]))
        measured = permutation_importance(model, validation[FEATURES], validation[TARGET], scoring="average_precision", n_repeats=3, random_state=config["seed"], n_jobs=1)
        importance.extend({"algorithm": name, "feature": feature, "importance": float(value), "stddev": float(deviation), "split": "validation"}
                          for feature, value, deviation in zip(FEATURES, measured.importances_mean, measured.importances_std))
    selected_model = max(models, key=lambda n: (models[n]["validation"]["average_precision"], n))
    # Freeze every threshold AND the selected model before computing any test predictions.
    test = frame[frame.split_name == "test"]
    for name, model in fitted.items():
        probability = model.predict_proba(test[FEATURES])[:, 1]
        threshold = threshold_analysis[name]["threshold"]
        models[name]["test"] = evaluate(test[TARGET], probability, BASELINE_THRESHOLD)
        threshold_analysis[name]["test"] = evaluate(test[TARGET], probability, threshold)
        predictions.append(prediction_rows(test, name, probability, threshold))
    pd.concat(predictions).to_parquet(output / "predictions.parquet", index=False)
    pd.DataFrame(importance).to_parquet(output / "feature_importance.parquet", index=False)
    frame.to_parquet(output / "split_assignments.parquet", index=False)
    metrics = {"status": "executed", "framework": "scikit-learn", "identity": identity, "completedAt": now(), "partitions": partitions,
               "selectedBy": "validation Average Precision only; test is held out", "selectedModel": selected_model, "models": models,
               "baselineThreshold": BASELINE_THRESHOLD, "thresholdAnalysis": threshold_analysis}
    write(output / "metrics.json", metrics)
    (output / "model_card.md").write_text("# Customer dissatisfaction experiment\n\nMeasured scikit-learn run. Selected by validation Average Precision: " + selected_model
        + ".\n\nPrediction: one order at delivery; label matures 14 days later. Chronological train/validation/test, 14-day embargo, preprocessing fitted on training only. Baseline threshold: 0.5.\n\n"
        + f"Alternative threshold: {threshold_analysis[selected_model]['threshold']:.17g}, selected by validation F1 only and frozen before test evaluation. Ties prefer closest to 0.5, then higher. Model selection uses validation AP; test metrics are descriptive only.\n\n"
        + "Synthetic selective outcomes are educational; a missing adverse outcome is not proof of satisfaction. No deployment or production quality claim.\n", encoding="utf-8")
    return {"status": "executed", "selectedModel": selected_model, "metrics": "ml/metrics.json", "metricsSha256": sha(output / "metrics.json"), "partitions": partitions}


def load_features(path, config):
    import pyarrow.parquet as pq
    if pq.read_metadata(path).num_rows * 4096 > config["materializationLimitMb"] * 1024 * 1024:
        raise ValueError("Estimated materialized feature mart exceeds memory budget; use Spark ML or a smaller mart")
    return pd.read_parquet(path)


def train(root, state):
    config = read(root / "factory/ml/run_config.json")
    if not config["enabled"] or config["target"] != "local-sklearn": raise ValueError("Local sklearn training was not selected")
    config["labelAsOf"] = read(state / "dbt_execution.json")["labelAsOf"]
    feature_path = state / "lake/gold/ml_customer_dissatisfaction.parquet"
    # Check metadata before materializing; conservative per-row budget, then actual memory check.
    frame = load_features(feature_path, config)
    return train_frame(frame, config, read(root / "factory/ml/spec.json"), state / "ml", {"featureSha256": sha(feature_path), "datasetFingerprint": read(root / "truth_manifest.json")["datasetFingerprint"]})


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--features", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--spec", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    config = read(args.config)
    train_frame(load_features(args.features, config), config, read(args.spec), args.output, {"featureSha256": sha(args.features)})
