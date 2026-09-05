"""One Cosmos Watcher build, invocation-bound adoption, reconciliation, ML and BI.

Prepare explicitly with prepare_cosmos.py. Import never executes dbt. The local
dags-test gate proves a complete DagRun, not a persistent scheduler deployment.
"""
import os
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone, timedelta
from airflow.sdk import DAG
from airflow.providers.standard.operators.python import PythonOperator
from cosmos import DbtTaskGroup, ProjectConfig, ProfileConfig, RenderConfig, ExecutionConfig
from cosmos.constants import LoadMode, TestBehavior, ExecutionMode, InvocationMode
from cosmos.operators.watcher import DbtConsumerWatcherSensor

ROOT = Path(os.environ["FORGE_PROJECT_ROOT"]).resolve()
sys.path.insert(0, str(ROOT / "factory"))
from common import read
from run import state_path
from orchestration import capture_producer

PYTHON = os.environ["FORGE_FACTORY_PYTHON"]
CONFIG = read(ROOT / "factory/ml/run_config.json")
GENERATION = read(ROOT / "project.json")["sourceProject"]["generation"]
LABEL_AS_OF = CONFIG["labelAsOf"] or (datetime.fromisoformat(GENERATION["startDate"].replace("Z", "+00:00"))
                                    + timedelta(days=GENERATION.get("timeSpanDays", 60) + 35)).isoformat()


def prepare_run(run_id):
    subprocess.run([PYTHON, str(ROOT / "factory/orchestration.py"), "prepare", "--root", str(ROOT), "--run-id", run_id], check=True)
    state = state_path(ROOT, run_id)
    return {"state": str(state), "nonce": read(state / "cosmos/attempt.json")["nonce"]}


def finish_run(run_id):
    subprocess.run([PYTHON, str(ROOT / "factory/orchestration.py"), "finish", "--root", str(ROOT), "--run-id", run_id], check=True)


with DAG(dag_id="contoso_forge_cosmos", start_date=datetime(2026, 1, 1, tzinfo=timezone.utc), schedule=None,
         catchup=False, max_active_runs=1, default_args={"retries": 0}, tags=["contoso-forge", "v1.6", "cosmos"]) as dag:
    silver = PythonOperator(task_id="prepare_silver", python_callable=prepare_run, op_kwargs={"run_id": "{{ run_id }}"})
    dbt = DbtTaskGroup(
        group_id="dbt_models_and_tests",
        project_config=ProjectConfig(dbt_project_path=ROOT / "factory/dbt", manifest_path=ROOT / "factory/dbt_manifest.json"),
        profile_config=ProfileConfig(profile_name="contoso_forge_customer_satisfaction", target_name="local", profiles_yml_filepath=ROOT / "factory/dbt/profiles.yml"),
        execution_config=ExecutionConfig(execution_mode=ExecutionMode.WATCHER, invocation_mode=InvocationMode.SUBPROCESS,
            dbt_executable_path=str(ROOT / ".forge/cosmos/dbt"), setup_operator_args={"callback": capture_producer}),
        render_config=RenderConfig(load_method=LoadMode.DBT_MANIFEST, test_behavior=TestBehavior.AFTER_EACH, emit_datasets=False),
        operator_args={"append_env": True, "env": {
            "FORGE_PROJECT_ROOT": str(ROOT),
            "FORGE_AIRFLOW_RUN_ID": "{{ run_id }}",
            "FORGE_AIRFLOW_TASK_ID": "{{ ti.task_id }}",
            "FORGE_COSMOS_NONCE": "{{ ti.xcom_pull(task_ids='prepare_silver')['nonce'] }}",
            "FORGE_LAKE_ROOT": "{{ ti.xcom_pull(task_ids='prepare_silver')['state'] }}/lake",
            "FORGE_DUCKDB_PATH": "{{ ti.xcom_pull(task_ids='prepare_silver')['state'] }}/warehouse.duckdb",
            "FORGE_TRUTH_MANIFEST": str(ROOT / "truth_manifest.json"),
            "FORGE_LABEL_AS_OF": LABEL_AS_OF,
            "DBT_SEND_ANONYMOUS_USAGE_STATS": "false"
        }, "dbt_cmd_global_flags": ["--no-partial-parse"], "install_deps": False, "emit_datasets": False,
        "pool": "forge_duckdb_single_writer"}
    )
    # dags-test executes serially. Explicitly finish the producer before synchronous
    # consumers so a sensor cannot occupy the only execution slot waiting for it.
    producer = dag.get_task("dbt_models_and_tests.dbt_producer_watcher")
    for task in dag.tasks:
        if isinstance(task, DbtConsumerWatcherSensor):
            task.deferrable = False
            producer >> task
    evidence = PythonOperator(task_id="adopt_reconcile_and_report", python_callable=finish_run, op_kwargs={"run_id": "{{ run_id }}"})
    silver >> dbt >> evidence
