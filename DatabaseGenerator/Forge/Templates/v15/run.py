"""Run the compiled V1.5 factory. Every stage records measured, immutable input identity."""
import argparse
import importlib.metadata
import os
from pathlib import Path
import sys
from common import read, write, sha, now

STAGES = ("verify", "silver", "validate-silver", "dbt", "reconcile", "ml", "export-ml", "bi")
PREREQUISITE = {"silver": "verify", "validate-silver": "silver", "dbt": "validate-silver", "reconcile": "dbt", "ml": "reconcile", "export-ml": "reconcile", "bi": "reconcile"}


def identity(root):
    truth = read(root / "truth_manifest.json")
    sources = truth["sourceFileSha256"]
    if not sources:
        raise ValueError("Missing source hashes")
    for name, digest in sources.items():
        path = (root / "data/source" / name).resolve()
        if path.parent != (root / "data/source").resolve() or sha(path) != digest:
            raise ValueError("Source checksum mismatch: " + name)
    manifest = read(root / "run_manifest.json")
    for name, digest in manifest["files"].items():
        path = (root / name).resolve()
        if not path.is_relative_to(root) or sha(path) != digest:
            raise ValueError("Compiled artifact checksum mismatch: " + name)
    return {"datasetFingerprint": truth["datasetFingerprint"], "truthSha256": sha(root / "truth_manifest.json"),
            "projectSha256": sha(root / "project.json"), "compiledManifestSha256": sha(root / "run_manifest.json")}


def state_path(root, run_id):
    import re
    import hashlib
    if not isinstance(run_id, str) or not run_id.strip() or len(run_id) > 250:
        raise ValueError("run-id must contain 1-250 characters")
    directory = run_id if re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,99}", run_id) else hashlib.sha256(run_id.encode()).hexdigest()
    return root / ".forge/v15" / directory


def verify_artifacts(state, record):
    for relative, digest in record.get("artifacts", {}).items():
        path = (state / relative).resolve()
        if not path.is_relative_to(state) or not path.is_file() or sha(path) != digest:
            raise ValueError("Run artifact changed: " + relative)


def execute(root, run_id, stage):
    import duckdb_silver
    import dbt_runtime
    root = Path(root).resolve()
    settings = read(root / "resolved_project.json")["settings"]
    engine = settings["engine"]
    if not (engine in ("duckdb", "polars", "pandas") and settings["runtime"] == "local-process" and settings["warehouse"] == "duckdb"
            and settings["storage"] == "local" and settings["tableFormat"] == "none" and settings["fileFormat"] == "parquet"):
        raise ValueError("Local factory requires a compiled local DuckDB/Polars/pandas engine with Parquet and DuckDB warehouse")
    state = state_path(root, run_id)
    state.mkdir(parents=True, exist_ok=True)
    # Prevent concurrent writers from corrupting a run; Airflow also has max_active_runs=1.
    lock = state / ".running"
    descriptor = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    os.close(descriptor)
    try:
        current = identity(root)
        path = state / "run_evidence.json"
        evidence = read(path) if path.exists() else {"contractVersion": "1.5", "runId": run_id, "identity": current, "startedAt": now(), "stages": {}, "status": "running"}
        if evidence["identity"] != current:
            raise ValueError("Run inputs changed. Use a new output/run ID; never adopt stale evidence.")
        for prior in evidence["stages"].values():
            if prior["status"] == "succeeded":
                verify_artifacts(state, prior)
        if stage in evidence["stages"] and evidence["stages"][stage]["status"] == "succeeded":
            return state
        prerequisite = PREREQUISITE.get(stage)
        if prerequisite and evidence["stages"].get(prerequisite, {}).get("status") != "succeeded":
            raise ValueError(f"Stage {stage} requires successful {prerequisite}")
        before = {p.relative_to(state).as_posix(): sha(p) for p in state.rglob("*") if p.is_file() and p.name not in (".running", "run_evidence.json")}
        record = {"status": "running", "startedAt": now()}
        evidence["stages"][stage] = record
        evidence["status"] = "running"
        write(path, evidence)
        try:
            if stage == "verify": result = {"status": "verified", **current}
            elif stage == "silver":
                from importlib import import_module
                result = import_module(engine + "_silver").transform(root, state)
                from silver_contract import contract
                write(state / "silver_contract.json", contract(root))
                result.update(adapter=engine, version=importlib.metadata.version(engine))
            elif stage == "validate-silver": result = duckdb_silver.validate(root, state)
            elif stage == "dbt":
                if read(root / "project.json")["product"]["dbtIntegration"] == "cosmos":
                    from orchestration import adopt_cosmos_dbt_results
                    result = adopt_cosmos_dbt_results(root, state, run_id)
                else:
                    result = dbt_runtime.build(root, state)
            elif stage == "reconcile": result = dbt_runtime.reconcile(root, state)
            elif stage == "ml":
                from ml_lab import train
                result = train(root, state)
            elif stage == "export-ml":
                from notebook_export import export
                result = export(root, state)
            elif stage == "bi":
                from bi_report import build
                config = read(root / "factory/ml/run_config.json")
                expected_ml = "ml" if config["target"] == "local-sklearn" else "export-ml"
                if config["enabled"] and evidence["stages"].get(expected_ml, {}).get("status") != "succeeded":
                    raise ValueError("Enabled ML requires its measured experiment or explicit export before BI")
                result = build(root, state, evidence)
            else: raise ValueError("Unknown stage " + stage)
            record.update(status="succeeded", result=result, completedAt=now())
            record["artifacts"] = {p.relative_to(state).as_posix(): sha(p) for p in state.rglob("*")
                                   if p.is_file() and p.name not in (".running", "run_evidence.json")
                                   and before.get(p.relative_to(state).as_posix()) != sha(p)}
            evidence["status"] = "succeeded" if stage == "bi" else "running"
            if stage == "bi": evidence["completedAt"] = now()
            evidence["runtimeVersions"] = {n: importlib.metadata.version(n) for n in ("duckdb", "dbt-core", "dbt-duckdb", "scikit-learn", "pandas", "pyarrow")}
            evidence["engine"] = {"name": engine, "version": importlib.metadata.version(engine), "runtime": settings["runtime"]}
            if engine == "polars": evidence["runtimeVersions"]["polars"] = importlib.metadata.version("polars")
            evidence["python"] = sys.version
        except Exception as error:
            record.update(status="failed", error=str(error), completedAt=now())
            evidence["status"] = "failed"
            raise
        finally:
            write(path, evidence)
        print(f"{stage}:succeeded evidence={path}")
        return state
    finally:
        lock.unlink()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--stage", choices=("all", *STAGES), default="all")
    args = parser.parse_args()
    if args.stage != "all":
        execute(args.root, args.run_id, args.stage)
        return
    plan = read(args.root / "local_plan.json")
    completed = set()
    for activity in plan["activities"]:
        if not activity["operation"].startswith("factory-") or not set(activity["dependsOn"]).issubset(completed):
            raise ValueError("Run the neutral pipeline runner for this custom graph; no unsupported operation is bypassed")
        execute(args.root, args.run_id, activity["operation"][8:])
        completed.add(activity["id"])


if __name__ == "__main__":
    main()
