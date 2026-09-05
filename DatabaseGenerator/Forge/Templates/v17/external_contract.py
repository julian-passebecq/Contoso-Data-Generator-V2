"""Checksum-bound external packages and results. No executable deserialization of returned models."""
import json
import math
from pathlib import Path
import re
from common import read, write, sha


def repo_id(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_-]{0,95}/[A-Za-z0-9][A-Za-z0-9_-]{0,95}", value):
        raise ValueError("Expected safe owner/slug")
    return value


def hashes(folder, exclude=()):
    result = {}
    for path in sorted(folder.rglob("*")):
        if path.is_symlink(): raise ValueError("Symlinks are forbidden in an external package")
        if path.is_file() and path.relative_to(folder).as_posix() not in exclude:
            result[path.relative_to(folder).as_posix()] = sha(path)
    return result


def verify(folder, expected, exclude=()):
    if not expected: raise ValueError("Empty external artifact map")
    for name, digest in expected.items():
        path = (folder / name).resolve()
        if not path.is_relative_to(folder.resolve()) or "\\" in name or not re.fullmatch(r"[0-9a-f]{64}", digest):
            raise ValueError("Unsafe external artifact entry")
    if hashes(folder, exclude) != expected: raise ValueError("External artifact set or checksum mismatch")


def binding(root, state):
    run = read(state / "run_evidence.json")
    return {"runId": run["runId"], "datasetFingerprint": run["identity"]["datasetFingerprint"],
            "projectSha256": run["identity"]["projectSha256"], "featureSha256": sha(state / "lake/gold/ml_customer_dissatisfaction.parquet")}


def validate_results(folder, package, expected_remote):
    manifest = read(folder / "remote_manifest.json")
    if manifest.get("status") != "executed" or manifest.get("identity") != package["identity"]:
        raise ValueError("Remote result has stale run/dataset/feature identity or did not execute")
    if manifest.get("packageManifestSha256") != package["manifestSha256"] or manifest.get("remote") != expected_remote:
        raise ValueError("Remote package/dataset/kernel binding mismatch")
    if manifest.get("target") != package["target"] or manifest.get("splitContract") != package["splitContract"]:
        raise ValueError("Remote target/split contract mismatch")
    required = {"metrics.json", "leaderboard.csv", "selected_model.json", "predictions.parquet", "automl_execution.json", "report/summary.json"}
    if not required.issubset(manifest.get("files", {})): raise ValueError("Remote result missing required outputs")
    verify(folder, manifest["files"], ("remote_manifest.json",))
    metrics, selected = read(folder / "metrics.json"), read(folder / "selected_model.json")
    if metrics.get("status") != "executed" or metrics.get("identity") != package["identity"] or selected.get("identity") != package["identity"]:
        raise ValueError("ML result identity mismatch")
    if metrics.get("partitions") != package["partitions"]: raise ValueError("Remote temporal partitions differ from bound Gold")
    if selected.get("selectedBeforeTest") is not True or metrics.get("selectedModel") != selected.get("name"):
        raise ValueError("Missing frozen model selection evidence")
    if set(metrics.get("models", {}).get(selected["name"], {})) != {"validation", "test"}:
        raise ValueError("Missing selected model validation/test metrics")
    for values in metrics["models"].values():
        for split in ("validation", "test"):
            for key in ("average_precision", "roc_auc", "f1", "precision", "recall"):
                value = values[split].get(key)
                if not isinstance(value, (int, float)) or not math.isfinite(value) or not 0 <= value <= 1:
                    raise ValueError("Invalid required classification metric")
    import pandas as pd
    predictions = pd.read_parquet(folder / "predictions.parquet")
    if not {"order_key", "split_name", "probability"}.issubset(predictions.columns) or set(predictions.split_name) != {"validation", "test"}:
        raise ValueError("Missing prediction partitions")
    if predictions[["order_key", "split_name"]].duplicated().any() or not predictions.probability.between(0, 1).all():
        raise ValueError("Invalid or duplicate predictions")
    for split in ("validation", "test"):
        if len(predictions[predictions.split_name == split]) != metrics["partitions"][split]["rows"]: raise ValueError("Prediction row counts mismatch")
        rows = predictions[predictions.split_name == split]
        expected = package["expectedPredictions"][split]
        if set(rows.order_key.map(lambda k: str(int(k)))) != set(expected): raise ValueError("Predictions contain wrong held-out order keys")
        if package["target"] not in rows or any(int(row[package["target"]]) != expected[str(int(row.order_key))] for _, row in rows.iterrows()):
            raise ValueError("Predictions contain altered target labels")
        from ml_lab import evaluate
        actual = evaluate(rows[package["target"]].to_numpy(), rows.probability.to_numpy())
        reported = metrics["models"][selected["name"]][split]
        if any(abs(actual[k] - reported[k]) > 1e-10 for k in ("average_precision", "roc_auc", "f1", "precision", "recall")):
            raise ValueError("Reported metrics do not match held-out predictions")
    return manifest
