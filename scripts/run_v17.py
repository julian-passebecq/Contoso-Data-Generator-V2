"""V1.7 release gate: execute compiled journeys and preserve run-scoped outputs."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--build-evidence", action="store_true")
    parser.add_argument("--automl-python", type=Path)
    parser.add_argument("--spark", action="store_true")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists(): raise ValueError("Release output must be fresh")
    output.mkdir(parents=True)
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    env = {**os.environ, "OMP_NUM_THREADS": "2", "OPENBLAS_NUM_THREADS": "2", "POLARS_MAX_THREADS": "4", "PYTHONUTF8": "1", "PYTHONDONTWRITEBYTECODE": "1"}
    records = {}

    def run(command, log):
        print("Executing " + log, flush=True)
        with (output / (log + ".log")).open("w", encoding="utf-8") as stream:
            subprocess.run(list(map(str, command)), stdout=stream, stderr=subprocess.STDOUT, env=env, check=True)

    def generate(label, project):
        path = output / (label + ".project.json")
        path.write_text(json.dumps(project, indent=2) + "\n", encoding="utf-8")
        root = output / label
        run(["dotnet", "run", "--project", "DatabaseGenerator", "--configuration", "Release", "--no-build", "--", "forge", "generate", "--project", path, "--output", root], label + "-generate")
        run([sys.executable, "scripts/validate_studio_artifacts.py", "--project", root], label + "-schema")
        run([sys.executable, root / "pipeline/run_local.py", "--root", root, "--run-id", "v17"], label + "-pipeline")
        state = root / ".forge/v15/v17"
        records[label] = {"root": str(root), "state": str(state), "status": json.loads((state / "run_evidence.json").read_text())["status"]}
        return root, state

    base = json.loads(Path("examples/v17-kpi-duckdb.project.json").read_text())
    for engine in ("duckdb", "polars", "pandas", *(["spark"] if args.spark else [])):
        project = json.loads(json.dumps(base))
        project["architecture"]["overrides"]["engine"] = engine
        project["product"]["selectedKpis"] = ["order_count", "gross_sales_amount", "return_rate", "on_time_delivery_rate", "average_review_rating"]
        root, state = generate(engine, project)
        if args.build_evidence and engine == "duckdb": run([sys.executable, root / "factory/build_evidence.py", "--state", state], "evidence-build")
    parity = [sys.executable, output / "duckdb/factory/parity.py"]
    for engine in ("duckdb", "polars", "pandas", *(["spark"] if args.spark else [])):
        parity += ["--run", engine, records[engine]["root"], records[engine]["state"]]
    run([*parity, "--revision", revision, "--output", output / "engine_parity.json"], "parity")
    for stage in ("generate", "bronze", "silver", "gold", "semantic", "analysis"):
        project = json.loads(json.dumps(base))
        project["product"].update(stopAfter=stage, goal="data-only" if stage in ("generate", "bronze", "silver", "gold") else "kpi-semantic", selectedKpis=[] if stage in ("generate", "bronze", "silver", "gold") else ["return_rate"])
        generate("stop-" + stage, project)
    for label in ("specific-ml", "automl-kaggle", "motherduck", "spark-ml"):
        root, state = generate(label, json.loads(Path("examples/v17-" + label + ".project.json").read_text()))
        if label == "specific-ml" and args.build_evidence:
            run([sys.executable, root / "factory/build_evidence.py", "--state", state], "ml-evidence-build")
    if args.automl_python:
        root, state = Path(records["automl-kaggle"]["root"]), Path(records["automl-kaggle"]["state"])
        # This is real local MLJAR proof; it is never labelled a Kaggle account-backed run.
        remote = json.dumps({"datasetId": "local-fixture/forge", "kernelId": "local-fixture/automl", "datasetVersion": 1})
        run([args.automl_python.resolve(), "-B", root / "factory/notebook_runner.py", "--package", state / "kaggle/dataset", "--output", output / "local-automl-results", "--remote", remote], "local-automl")
    if subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip() != revision:
        raise ValueError("Checkout changed during the measured gate")
    summary = {"version": "1.7", "headCommit": revision, "workingTreeDirty": bool(subprocess.check_output(["git", "status", "--porcelain"], text=True).strip()),
        "runs": records, "parity": "engine_parity.json", "localAutoMl": "executed" if args.automl_python else "not-executed", "spark": "executed" if args.spark else "not-executed",
        "externalKaggle": "exported-not-executed", "huggingFace": "exported-not-published", "motherDuck": "exported-not-executed"}
    (output / "gate.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary), flush=True)


if __name__ == "__main__": main()
