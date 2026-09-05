"""Run the generated DAG in real Airflow; collect the completed metadata DB rows.

Invoke with the isolated Airflow interpreter. FORGE_FACTORY_PYTHON and
FORGE_DBT_EXECUTABLE point to the separately pinned factory/dbt environment.
"""
import argparse
import importlib.metadata
import os
from pathlib import Path
import subprocess
import sys
import shutil


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--airflow-home", required=True, type=Path)
    parser.add_argument("--logical-date", default="2026-09-05T00:00:00+00:00")
    args = parser.parse_args()
    root, home = args.root.resolve(), args.airflow_home.resolve()
    if home.exists(): raise ValueError("Use a fresh isolated Airflow home")
    home.mkdir(parents=True)
    dagfile = root / "airflow/dags/contoso_forge_cosmos.py"
    deployed = home / "dags/contoso_forge_cosmos.py"
    deployed.parent.mkdir()
    shutil.copyfile(dagfile, deployed)
    os.environ.update(FORGE_PROJECT_ROOT=str(root), AIRFLOW_HOME=str(home), AIRFLOW__CORE__LOAD_EXAMPLES="false",
                      AIRFLOW__CORE__DAGS_FOLDER=str(deployed.parent), AIRFLOW__CORE__EXECUTOR="LocalExecutor",
                      AIRFLOW__DATABASE__SQL_ALCHEMY_CONN="sqlite:///" + str(home / "airflow.db"),
                      AIRFLOW__CORE__DAGBAG_IMPORT_TIMEOUT="120", AIRFLOW__CORE__EXECUTION_API_SERVER_URL="http://localhost:8080/execution/")
    sys.path.insert(0, str(root / "factory"))
    from common import read, write, sha, now
    from orchestration import DAG_ID, PRODUCER, require, finalize_execution
    from run import state_path, identity
    binary = str(Path(sys.executable).parent / "airflow")

    def execute(command, name):
        with (home / name).open("w", encoding="utf-8") as log:
            return subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=2400).returncode

    require(execute([binary, "db", "migrate"], "db-migrate.log") == 0, "Airflow database migration failed")
    require(execute([binary, "pools", "set", "forge_duckdb_single_writer", "1", "Forge canonical warehouse"], "pool.log") == 0, "pool creation failed")
    from airflow.dag_processing.dagbag import DagBag
    bag = DagBag(dag_folder=str(deployed), safe_mode=False)
    write(home / "imports.json", {"importErrors": bag.import_errors, "dagIds": sorted(bag.dags)})
    require(not bag.import_errors and set(bag.dags) == {DAG_ID}, "DAG import/discovery failed")
    dag = bag.dags[DAG_ID]
    from cosmos.operators.watcher import DbtProducerWatcherOperator, DbtConsumerWatcherSensor, DbtTestWatcherOperator
    require([t.task_id for t in dag.tasks if isinstance(t, DbtProducerWatcherOperator)] == [PRODUCER], "expected exactly one Watcher producer")
    require(sum(isinstance(t, DbtConsumerWatcherSensor) for t in dag.tasks) >= 27
            and any(isinstance(t, DbtTestWatcherOperator) for t in dag.tasks), "missing model/test watcher sensors")
    imported = {"dagId": DAG_ID, "dagFileSha256": sha(dagfile), "taskIds": sorted(dag.task_ids), "importErrorCount": 0,
                "taskOperators": {t.task_id: type(t).__name__ for t in dag.tasks}}
    write(home / "imports.json", imported)
    command = [binary, "dags", "test", DAG_ID, args.logical_date, "-f", str(deployed)]
    started = now()
    print("Executing complete local DagRun:", command, flush=True)
    exit_code = execute(command, "dags-test.log")
    from airflow.models.dagrun import DagRun
    from airflow.utils.session import create_session
    with create_session() as session:
        runs = session.query(DagRun).filter(DagRun.dag_id == DAG_ID).all()
        require(len(runs) == 1, "expected one real DagRun in isolated Airflow database")
        dr = runs[0]
        tasks = [{"taskId": ti.task_id, "state": ti.state, "tryNumber": ti.try_number, "mapIndex": ti.map_index,
                  "startedAt": ti.start_date.isoformat() if ti.start_date else None,
                  "completedAt": ti.end_date.isoformat() if ti.end_date else None,
                  "operator": ti.operator} for ti in dr.get_task_instances(session=session)]
        runtime = {"dagId": dr.dag_id, "dagRunId": dr.run_id, "state": dr.state, "tasks": sorted(tasks, key=lambda t: t["taskId"]),
                   "startedAt": dr.start_date.isoformat() if dr.start_date else None,
                   "completedAt": dr.end_date.isoformat() if dr.end_date else None}
    state = state_path(root, runtime["dagRunId"])
    write(state / "airflow_runtime.json", runtime)
    write(state / "airflow_imports.json", imported)
    shutil.copyfile(home / "dags-test.log", state / "airflow-dags-test.log")
    airflow = {"schemaVersion": 1, "status": "succeeded" if exit_code == 0 and runtime["state"] == "success" else "failed",
               "dagId": DAG_ID, "dagRunId": runtime["dagRunId"], "dagFileSha256": sha(dagfile), "identity": identity(root),
               "executionMode": "airflow-dags-test", "persistentDeploymentProven": False,
               "scope": "Complete local DagRun, not a persistent scheduler or Kubernetes deployment",
               "airflowVersion": importlib.metadata.version("apache-airflow"), "command": command, "exitCode": exit_code,
               "startedAt": started, "completedAt": now(), "importErrorCount": 0, "tasks": runtime["tasks"],
               "failedTaskCount": sum(t["state"] in ("failed", "upstream_failed") for t in tasks),
               "skippedTaskCount": sum(t["state"] == "skipped" for t in tasks),
               "runtimeSha256": sha(state / "airflow_runtime.json"), "importsSha256": sha(state / "airflow_imports.json"),
               "logSha256": sha(state / "airflow-dags-test.log")}
    write(state / "airflow_execution.json", airflow)
    finalize_execution(root, state, runtime["dagRunId"])
    write(home / "result.json", {"root": str(root), "state": str(state), "runId": runtime["dagRunId"], "status": "reconciled"})
    print("Reconciled real Airflow/Cosmos execution:", state, flush=True)


if __name__ == "__main__": main()
