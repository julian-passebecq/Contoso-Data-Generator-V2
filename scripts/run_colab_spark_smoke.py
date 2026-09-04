#!/usr/bin/env python3
"""Run an isolated, real Forge Spark gate without BigQuery or hosted-Colab claims.

Requires a generated free-GCP project and a Python environment with compatible
PySpark (including Connect dependencies for connect-local) and Java installed.
This command does not install packages or change the original generated project.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import shutil
import subprocess
import sys
import uuid
from pathlib import Path


def run(project, output, mode, python=None, timeout=600):
    project, output = Path(project).resolve(), Path(output).resolve()
    if mode not in ("classic", "connect-local") or timeout <= 0:
        raise ValueError("Select classic/connect-local and a positive timeout")
    if output == project or output.is_relative_to(project) or project.is_relative_to(output):
        raise ValueError("The smoke output must be separate from the generated input project")
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        raise ValueError("The smoke output must be a new or empty directory; existing evidence is never overwritten")
    spec = importlib.util.spec_from_file_location("forge_smoke_handoff", project / "colab/work_order.py")
    handoff = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(handoff)
    truth, hashes, _ = handoff.verify_sources(project)
    pipeline_name = "pipeline.json" if (project / "pipeline.json").is_file() else "pipeline/pipeline.json"
    pipeline = handoff.read_json(project / pipeline_name)
    activities = [a for a in pipeline.get("activities", []) if a.get("implementation") == "colab-work-order"]
    if len(activities) != 1:
        raise ValueError("Smoke input must have one generated neutral Colab work-order activity")
    config = handoff.read_json(project / "colab/spark_config.json")
    config["sparkApiMode"] = mode
    config.pop("sparkRemote", None)
    if mode == "connect-local" and config.get("sparkVersion") != "4.0.4":
        raise ValueError("Generate the Spark 4.0.4 Connect profile before running this mode")
    resolved = handoff.read_json(project / "resolved_project.json")
    settings = resolved["settings"]
    if any(settings.get(k) != v for k, v in {"engine": "spark", "storage": "local", "fileFormat": "parquet", "tableFormat": "none", "warehouse": "bigquery"}.items()):
        raise ValueError("Smoke input requires the generated local Parquet Spark/BigQuery adapter")
    # Copy only execution inputs; previous orders/results/lakes and credentials are excluded.
    names = ["truth_manifest.json", "resolved_project.json", pipeline_name, "pyspark/bronze_silver.py",
             "gcp/bigquery_config.json", "gcp/bigquery_runtime.py", "gcp/reconcile_kpis.sql", "gcp/requirements.txt",
             *["colab/" + name for name in ("work_order.py", "run_spark.py", "spark_config.json", "spark_session.py", "storage_adapter.py", "bootstrap_runtime.py")],
             *["data/source/" + name for name in sorted(hashes)]]
    for name in names:
        if not handoff.safe_path(project, name).is_file():
            raise ValueError(f"Missing generated smoke input: {name}")
    output.mkdir(parents=True, exist_ok=True)
    for name in names:
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(handoff.safe_path(project, name), target)
    settings.update({k: v for k, v in config.items() if k != "contractVersion"})
    settings.update(runtime="google-colab")
    settings.pop("sparkRemote", None)
    activities[0].update({k: v for k, v in config.items() if k != "contractVersion"})
    activities[0].update(runtime="google-colab")
    activities[0].pop("sparkRemote", None)
    handoff.write_json(output / "resolved_project.json", resolved)
    handoff.write_json(output / pipeline_name, pipeline)
    handoff.write_json(output / "colab/spark_config.json", config)
    run_id = "local-smoke-" + mode + "-" + uuid.uuid4().hex[:12]
    python = python or sys.executable
    steps = [
        ("package", "colab/work_order.py", ["package", "--root", ".", "--run-id", run_id, "--scope", "spark"]),
        ("spark", "colab/run_spark.py", ["--root", ".", "--lake-root", "lake", "--work-order", "colab/work_order.json"]),
        ("result", "colab/work_order.py", ["spark-result", "--root", ".", "--work-order", "colab/work_order.json", "--runtime", "colab/spark_runtime.json", "--output", "colab/spark_result_manifest.json"]),
        ("import", "colab/work_order.py", ["import-evidence", "--root", ".", "--work-order", "colab/work_order.json", "--result", "colab/spark_result_manifest.json", "--output", "evidence.json"])
    ]
    for label, script, arguments in steps:
        log = output / (label + ".log")
        print(f"{label}: {log}", flush=True)
        with log.open("w", encoding="utf-8") as handle:
            subprocess.run([python, script, *arguments], cwd=output, stdout=handle, stderr=subprocess.STDOUT, check=True, timeout=timeout)
    evidence = handoff.read_json(output / "evidence.json")
    runtime = handoff.read_json(output / "colab/spark_runtime.json")
    report = {"status": "passed", "requestedMode": mode, "actualMode": runtime["actualSparkApiMode"],
              "isRemote": runtime["isRemote"], "sparkVersion": runtime["sparkVersion"],
              "datasetFingerprint": truth["datasetFingerprint"], "runtimeStatus": evidence["runtimeStatus"],
              "evidence": str(output / "evidence.json"), "cloudExecutionVerified": False}
    handoff.write_json(output / "smoke_summary.json", report)
    print(json.dumps(report, indent=2))
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, help="Existing generated free-GCP project")
    parser.add_argument("--output", required=True, help="Separate new/empty directory for inputs, lake, logs and evidence")
    parser.add_argument("--mode", choices=("classic", "connect-local"), required=True)
    parser.add_argument("--python", default=sys.executable)
    parser.add_argument("--timeout", type=int, default=600, help="Maximum seconds for each execution step")
    args = parser.parse_args()
    run(args.project, args.output, args.mode, args.python, args.timeout)
