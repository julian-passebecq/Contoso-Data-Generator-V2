"""Execute dbt itself, retain its artifacts, then read canonical Gold for reconciliation."""
import os
import shutil
import subprocess
import sys
from pathlib import Path
import duckdb
from common import read, write, sha, compare_kpis, identifier


def prepare(root, state):
    project = state / "dbt"
    if not project.exists():
        shutil.copytree(root / "factory/dbt", project, ignore=shutil.ignore_patterns("target", "logs", "dbt_packages", "__pycache__"))
    return project


def build(root, state, database_path=None):
    if read(root / "project.json").get("product", {}).get("dbtIntegration") == "cosmos" or (state / "cosmos/attempt.json").exists():
        raise ValueError("Cosmos requires invocation-bound artifact adoption; a second plain dbt build is forbidden")
    project = prepare(root, state)
    from datetime import datetime, timedelta, timezone
    config = read(root / "factory/ml/run_config.json")
    generation = read(root / "project.json")["sourceProject"]["generation"]
    cutoff = config["labelAsOf"] or (datetime.fromisoformat(generation["startDate"].replace("Z", "+00:00")) + timedelta(days=generation.get("timeSpanDays", 60) + 35)).isoformat()
    env = os.environ.copy()
    env.update(FORGE_LAKE_ROOT=(state / "lake").as_posix(), FORGE_TRUTH_MANIFEST=(root / "truth_manifest.json").as_posix(),
               FORGE_DUCKDB_PATH=database_path or (state / "warehouse.duckdb").as_posix(), FORGE_LABEL_AS_OF=cutoff, DBT_SEND_ANONYMOUS_USAGE_STATS="false", PYTHONUTF8="1")
    executable = Path(sys.executable).parent / ("dbt.exe" if os.name == "nt" else "dbt")
    command = [str(executable), "build", "--project-dir", str(project), "--profiles-dir", str(project), "--no-partial-parse"]
    with (state / "dbt-build.log").open("w", encoding="utf-8") as log:
        result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=1800)
    artifacts = {name: sha(project / "target" / name) for name in ("manifest.json", "run_results.json") if (project / "target" / name).is_file()}
    summary = {"exitCode": result.returncode, "command": command, "artifacts": artifacts, "labelAsOf": cutoff}
    write(state / "dbt_execution.json", summary)
    if result.returncode:
        raise RuntimeError(f"dbt build failed ({result.returncode}); see {state / 'dbt-build.log'}")
    summary.update(check_results(state))
    write(state / "dbt_execution.json", summary)
    return summary


def check_results(state):
    manifest = read(state / "dbt/target/manifest.json")
    results = read(state / "dbt/target/run_results.json")
    executed = {r["unique_id"]: r for r in results["results"]}
    expected = {k for k, n in manifest["nodes"].items() if n["resource_type"] in ("model", "test", "snapshot", "seed") and n.get("config", {}).get("enabled", True)}
    missing = expected - executed.keys()
    failed = {k: executed[k]["status"] for k in expected & executed.keys() if executed[k]["status"] not in ("success", "pass")}
    if not expected or missing or failed:
        raise ValueError(f"Incomplete/failed dbt build: missing={sorted(missing)} failed={failed}")
    return {"status": "tested", "models": sum(manifest["nodes"][k]["resource_type"] == "model" for k in expected),
            "tests": sum(manifest["nodes"][k]["resource_type"] == "test" for k in expected), "failed": 0, "skipped": 0}


def reconcile(root, state, database_path=None):
    if (state / "cosmos/attempt.json").exists():
        from orchestration import validate_cosmos
        result = validate_cosmos(root, state, read(state / "run_evidence.json")["runId"])
        if database_path is not None or any(sha(state / "dbt/target" / n) != digest for n, digest in result["artifacts"].items()):
            raise ValueError("Reconciliation must consume the exact adopted Cosmos artifacts and warehouse")
    dbt = check_results(state)
    catalog = read(root / "models/kpi_catalog.json")
    model = ".".join(identifier(part) for part in catalog["reconciliation"]["actualModel"].split("."))
    with duckdb.connect(database_path or str(state / "warehouse.duckdb"), read_only=database_path is None) as db:
        result = db.execute("SELECT * FROM " + model)
        columns = [c[0] for c in result.description]
        rows = result.fetchall()
        if len(rows) != 1:
            raise ValueError("Canonical KPI mart must have exactly one row")
        actual = dict(zip(columns, rows[0]))
        comparisons = compare_kpis(actual, read(root / "truth_manifest.json"), catalog)
        gold_counts = {}
        manifest = read(state / "dbt/target/manifest.json")
        for node in manifest["nodes"].values():
            if node["resource_type"] == "model" and node["schema"] == "gold":
                name = node["alias"]
                gold_counts[name] = db.execute(f"SELECT count(*) FROM gold.{identifier(name)}").fetchone()[0]
                path = state / "lake/gold" / (name + ".parquet")
                path.parent.mkdir(parents=True, exist_ok=True)
                db.execute(f"COPY (SELECT * FROM gold.{identifier(name)} ORDER BY ALL) TO ? (FORMAT PARQUET)", [str(path)])
    result = {"status": "reconciled", "kpis": comparisons, "goldRowCounts": gold_counts, "dbt": dbt}
    write(state / "reconciliation.json", result)
    return result
