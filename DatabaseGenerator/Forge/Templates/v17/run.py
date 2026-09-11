"""V1.7 operations in the existing compiled factory graph. stopAfter is enforced at dispatch."""
import argparse
from importlib import metadata
import os
from pathlib import Path
import sys
from common import read, write, sha, now
from legacy_run import identity as legacy_identity, state_path, verify_artifacts


def verify_motherduck_summary(state):
    # The public summary evolves across stages; immutable receipts remain hash-bound.
    build, summary = state / "motherduck_build.json", state / "motherduck_execution.json"
    if not build.exists():
        if summary.exists(): raise ValueError("Unbound MotherDuck summary")
        return
    reconciled = state / "motherduck_reconciliation.json"
    expected = read(reconciled if reconciled.exists() else build)
    dive = state / "motherduck_dive_execution.json"
    if reconciled.exists() and dive.exists(): expected["dive"] = read(dive)
    if not summary.exists() or read(summary) != expected: raise ValueError("MotherDuck summary differs from immutable receipts")


def identity(root):
    return {**legacy_identity(root), "sourceModelSha256": sha(root / "models/source_model.json")}


def planned(root):
    plan = read(root / "local_plan.json")["activities"]
    if any(not a["operation"].startswith("factory-") for a in plan):
        raise ValueError("This project has unsupported runtime mappings; use its documented export boundary")
    return [a["operation"][8:] for a in plan]


def available_versions():
    # Optional metadata is descriptive, never a requirement for an unused stage.
    versions = {}
    for name in ("duckdb", "pandas", "polars", "pyarrow", "dbt-core", "scikit-learn"):
        try: versions[name] = metadata.version(name)
        except metadata.PackageNotFoundError: versions[name] = "unavailable"
    return versions


def execute(root, run_id, stage):
    root = Path(root).resolve()
    stages = planned(root)
    if stage not in stages: raise ValueError("Stage is outside the compiled goal/stopAfter boundary: " + stage)
    product = read(root / "project.json")["product"]
    if product["version"] != "1.7": raise ValueError("Expected V1.7 project")
    settings = read(root / "resolved_project.json")["settings"]
    engine = settings["engine"]
    state = state_path(root, run_id)
    state.mkdir(parents=True, exist_ok=True)
    lock = state / ".running"
    os.close(os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY))
    try:
        current = identity(root)
        path = state / "run_evidence.json"
        evidence = read(path) if path.exists() else {"contractVersion": "1.7", "runId": run_id, "identity": current,
            "goal": product["goal"], "stopAfter": product["stopAfter"], "startedAt": now(), "status": "running", "stages": {}}
        if evidence["identity"] != current or evidence["runId"] != run_id: raise ValueError("Stale run identity")
        for prior in evidence["stages"].values():
            if prior["status"] == "succeeded": verify_artifacts(state, prior)
        verify_motherduck_summary(state)
        if evidence["stages"].get("silver", {}).get("status") == "succeeded":
            recorded = {p for r in evidence["stages"].values() for p in r.get("artifacts", {}) if p.startswith("lake/silver/") and p.endswith(".parquet")}
            actual = {p.relative_to(state).as_posix() for p in (state / "lake/silver").rglob("*.parquet")}
            if recorded != actual: raise ValueError("Silver artifact set changed")
        if (state / "bronze_execution.json").exists():
            from layers import verify_bronze
            verify_bronze(root, state)
        if evidence["stages"].get(stage, {}).get("status") == "succeeded": return state
        index = stages.index(stage)
        if any(evidence["stages"].get(s, {}).get("status") != "succeeded" for s in stages[:index]):
            raise ValueError("All preceding compiled stages must succeed first")
        before = {p.relative_to(state).as_posix(): sha(p) for p in state.rglob("*") if p.is_file() and p.name not in (".running", "run_evidence.json", "motherduck_execution.json")}
        record = {"status": "running", "startedAt": now(), "inputIdentity": current,
                  "inputArtifacts": {p: digest for prior in evidence["stages"].values() for p, digest in prior.get("artifacts", {}).items()}}
        evidence["stages"][stage] = record
        write(path, evidence)
        try:
            from journey_runtime import dispatch
            result = dispatch(root, state, product, settings, stage, evidence)
            verify_motherduck_summary(state)
            if identity(root) != current: raise ValueError("Inputs changed during execution")
            record.update(status="succeeded", result=result, completedAt=now())
            record["artifacts"] = {p.relative_to(state).as_posix(): sha(p) for p in state.rglob("*")
                if p.is_file() and p.name not in (".running", "run_evidence.json", "motherduck_execution.json") and before.get(p.relative_to(state).as_posix()) != sha(p)}
            # Optional external export prevents dependent runtime stages from claiming execution.
            if result.get("blocksDownstream"):
                evidence.update(status=result["status"], completedAt=now())
            elif stage == stages[-1]: evidence.update(status="succeeded", completedAt=now())
            evidence["runtimeVersions"] = available_versions()
            version = metadata.version("pyspark" if engine == "spark" else engine) if stage != "verify" or engine != "spark" else "not-started"
            evidence["engine"] = {"name": engine, "version": version, "runtime": settings["runtime"]}
            evidence["python"] = sys.version
        except Exception as error:
            # External library errors can contain credentials/URLs. Persist a type, not raw secrets.
            record.update(status="failed", errorType=type(error).__name__, completedAt=now())
            evidence.update(status="failed", completedAt=now())
            raise
        finally: write(path, evidence)
        print(f"{stage}:{result['status']} evidence={path}")
        return state
    finally: lock.unlink()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--stage", default="all")
    args = parser.parse_args()
    for stage in planned(args.root) if args.stage == "all" else [args.stage]:
        execute(args.root, args.run_id, stage)


if __name__ == "__main__": main()
