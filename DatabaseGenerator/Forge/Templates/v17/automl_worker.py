"""MLJAR 1.3.2 custom chronological validation, with test excluded until model/threshold freeze."""
import argparse
from importlib import metadata
from pathlib import Path
import numpy as np
import pandas as pd
from common import read, write, sha, now
from ml_lab import FEATURES, NUMERIC, CATEGORICAL, TARGET, load_features, temporal_split, evaluate, select_validation_threshold, prediction_rows


def train(package, output, identity, automl_class=None):
    config, spec = read(package / "run_config.json"), read(package / "spec.json")
    analysis = config["analysis"]
    if analysis["target"] != TARGET or spec["target"] != TARGET or set(spec["features"]) != set(FEATURES) or set(FEATURES) & set(spec["leakageExclusions"]):
        raise ValueError("AutoML target/features violate the governed leakage contract")
    if analysis["mode"] not in ("Explain", "Perform", "Compete") or not 30 <= analysis["totalTimeLimitSeconds"] <= 3600:
        raise ValueError("Invalid bounded AutoML configuration")
    frame = load_features(package / "features.parquet", config)
    # Arrow decimal columns arrive as Python Decimal/object. MLJAR otherwise treats them
    # as text. Apply the governed types before splitting, without learning statistics.
    for column in NUMERIC:
        frame[column] = pd.to_numeric(frame[column], errors="raise").astype("float64")
    for column in CATEGORICAL:
        frame[column] = frame[column].map(lambda value: str(value) if pd.notna(value) else np.nan)
    if int(frame.memory_usage(deep=True).sum()) > config["materializationLimitMb"] * 1024 * 1024: raise ValueError("AutoML materialization budget exceeded")
    frame, partitions = temporal_split(frame, config["labelAsOf"])
    selection = frame[frame.split_name != "test"].reset_index(drop=True)
    training = np.flatnonzero(selection.split_name.to_numpy() == "train")
    validation = np.flatnonzero(selection.split_name.to_numpy() == "validation")
    if not len(training) or not len(validation) or set(training) & set(validation): raise ValueError("Invalid custom validation indices")
    if automl_class is None:
        from supervised.automl import AutoML
        automl_class = AutoML
    output.mkdir(parents=True, exist_ok=True)
    report = output / "report"
    # CPU-only bounded candidate set for every mode. No stacking, tuning, random validation, or test fitting.
    model = automl_class(results_path=str(report), mode=analysis["mode"], ml_task="binary_classification",
        algorithms=["Baseline", "Linear", "Decision Tree"], total_time_limit=analysis["totalTimeLimitSeconds"],
        validation_strategy={"validation_type": "custom"}, eval_metric="logloss", train_ensemble=False,
        stack_models=False, golden_features=False, features_selection=False, kmeans_features=False,
        explain_level=1, n_jobs=1, random_state=config["seed"])
    started = now()
    model.fit(selection[FEATURES], selection[TARGET], cv=[(training, validation)])
    leaderboard = model.get_leaderboard(original_metric_values=True)
    leaderboard.to_csv(output / "leaderboard.csv", index=False)
    if not any("baseline" not in name.lower() for name in leaderboard["name"]):
        raise RuntimeError("No non-baseline AutoML candidate trained successfully")
    selected_name = read(report / "params.json")["best_model"]
    if selected_name not in set(leaderboard["name"]): raise ValueError("Selected MLJAR model absent from leaderboard")
    validation_frame = selection.iloc[validation]
    probability = np.asarray(model.predict_proba(validation_frame[FEATURES]))[:, 1]
    threshold = select_validation_threshold(validation_frame[TARGET], probability)
    selected = {"name": selected_name, "identity": identity, "framework": "mljar-supervised", "selectedBeforeTest": True,
        "selectedBy": "custom chronological validation logloss", "threshold": threshold["threshold"], "thresholdSelectedOn": "validation F1", "frozenAt": now()}
    write(output / "selected_model.json", selected)
    validation_metrics = evaluate(validation_frame[TARGET], probability)
    rows = [prediction_rows(validation_frame, selected_name, probability, threshold["threshold"])]
    # No test rows or test metrics reach fit, the leaderboard, or threshold selection.
    test = frame[frame.split_name == "test"]
    test_probability = np.asarray(model.predict_proba(test[FEATURES]))[:, 1]
    test_metrics = evaluate(test[TARGET], test_probability)
    threshold["test"] = evaluate(test[TARGET], test_probability, threshold["threshold"])
    rows.append(prediction_rows(test, selected_name, test_probability, threshold["threshold"]))
    pd.concat(rows).to_parquet(output / "predictions.parquet", index=False)
    metrics = {"status": "executed", "framework": "mljar-supervised", "identity": identity, "selectedModel": selected_name,
        "selectedBy": "custom chronological validation logloss; test held out", "partitions": partitions,
        "models": {selected_name: {"validation": validation_metrics, "test": test_metrics}}, "thresholdAnalysis": {selected_name: threshold}, "completedAt": now()}
    write(output / "metrics.json", metrics)
    # Importance from a losing candidate must not be presented as the selected model's.
    importance = list((report / selected_name).rglob("*importance.csv"))
    if importance:
        import shutil
        shutil.copyfile(sorted(importance)[0], output / "feature_importance.csv")
    write(output / "automl_execution.json", {"status": "executed", "identity": identity, "framework": "mljar-supervised",
        "version": metadata.version("mljar-supervised") if automl_class.__module__.startswith("supervised") else "test-fixture",
        "startedAt": started, "completedAt": now(), "mode": analysis["mode"], "totalTimeLimitSeconds": analysis["totalTimeLimitSeconds"],
        "validationType": "custom", "trainingRows": len(training), "validationRows": len(validation), "testRowsPassedToFit": 0,
        "featureAllowlist": FEATURES, "embargoDays": 14, "selectedBeforeTest": True})
    write(report / "summary.json", {"identity": identity, "partitions": partitions, "selection": selected,
        "limitations": "Synthetic binary classification only; model files are retained as opaque training outputs and never unpickled during import."})
    return metrics


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    train(args.package, args.output, read(args.package / "package_manifest.json")["identity"])
