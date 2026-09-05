"""Notebook-side subprocess deadline and result contract. Failure never produces a successful manifest."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
from common import read, write, sha, now
from external_contract import hashes, verify


def run(package, output, remote):
    bound = read(package / "package_manifest.json")
    verify(package, bound["files"], ("package_manifest.json",))
    config = read(package / "run_config.json")
    output.mkdir(parents=True)
    status = {"status": "running", "identity": bound["identity"], "startedAt": now()}
    try:
        if config["analysis"]["kind"] == "mljar":
            command = [sys.executable, "-B", str(package / "automl_worker.py"), "--package", str(package), "--output", str(output)]
        else:
            command = [sys.executable, "-B", str(package / "ml_lab.py"), "--features", str(package / "features.parquet"),
                "--spec", str(package / "spec.json"), "--config", str(package / "run_config.json"), "--output", str(output)]
        with (output / "training.log").open("w", encoding="utf-8") as log:
            subprocess.run(command, check=True, stdout=log, stderr=subprocess.STDOUT, timeout=config["analysis"]["totalTimeLimitSeconds"] + 60)
        metrics = read(output / "metrics.json")
        metrics["identity"] = bound["identity"]
        write(output / "metrics.json", metrics)
        if config["analysis"]["kind"] != "mljar":
            import pandas as pd
            selected = {"name": metrics["selectedModel"], "identity": bound["identity"], "selectedBeforeTest": True, "selectedBy": metrics["selectedBy"]}
            write(output / "selected_model.json", selected)
            pd.DataFrame([{"name": k, **v["validation"]} for k, v in metrics["models"].items()]).to_csv(output / "leaderboard.csv", index=False)
            write(output / "automl_execution.json", {"status": "executed", "framework": "scikit-learn", "automl": False, "identity": bound["identity"]})
            write(output / "report/summary.json", {"identity": bound["identity"], "selection": selected})
        write(output / "remote_manifest.json", {"contractVersion": "1.7", "status": "executed", "identity": bound["identity"],
            "packageManifestSha256": sha(package / "package_manifest.json"), "target": bound["target"], "splitContract": bound["splitContract"],
            "remote": remote, "files": hashes(output), "completedAt": now()})
    except Exception as error:
        write(output / "failure.json", {**status, "status": "failed", "errorType": type(error).__name__, "completedAt": now()})
        raise


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--remote", required=True)
    args = parser.parse_args()
    run(args.package.resolve(), args.output.resolve(), json.loads(args.remote))
