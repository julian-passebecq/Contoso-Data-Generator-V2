"""Optional model-level Airflow view; plain dbt build remains the reconciliation authority.

Copy to the Airflow DAG directory only when Cosmos is selected and installed.
All execution variables are run scoped. DAG import performs no dbt or cloud calls.
Generated/unverified: dbt currently runs twice, first in the TaskGroup (cosmos.duckdb),
then in a full plain build (warehouse.duckdb). The latter proves only its own run.
TODO: capture separate invocation-bound results for every Cosmos model/test and
the plain build before claiming runnable Cosmos; a last-node run_results is insufficient.
"""
import os
from pathlib import Path
import sys
from datetime import datetime, timezone, timedelta
from airflow.sdk import DAG
from airflow.providers.standard.operators.python import PythonOperator
from cosmos import DbtTaskGroup, ProjectConfig, ProfileConfig, RenderConfig, ExecutionConfig
from cosmos.constants import LoadMode, TestBehavior

ROOT = Path(os.environ["FORGE_PROJECT_ROOT"]).resolve()
sys.path.insert(0, str(ROOT / "factory"))
from run import execute, state_path
from dbt_runtime import prepare
from common import read

CONFIG = read(ROOT / "factory/ml/run_config.json")
GENERATION = read(ROOT / "project.json")["sourceProject"]["generation"]
LABEL_AS_OF = CONFIG["labelAsOf"] or (datetime.fromisoformat(GENERATION["startDate"].replace("Z", "+00:00"))
                                    + timedelta(days=GENERATION.get("timeSpanDays", 60) + 35)).isoformat()


def prepare_run(run_id):
    for stage in ("verify", "silver", "validate-silver"):
        execute(ROOT, run_id, stage)
    state = state_path(ROOT, run_id)
    prepare(ROOT, state)
    return str(state)


def finish_run(run_id):
    # A full plain build captures the complete manifest/run_results instead of treating
    # Cosmos's last per-node run_results file as evidence of an entire build.
    from common import read
    from dbt_runtime import build
    for stage in ("dbt", "reconcile"):
        execute(ROOT, run_id, stage)
    config = read(ROOT / "factory/ml/run_config.json")
    if config["enabled"]: execute(ROOT, run_id, "ml" if config["target"] == "local-sklearn" else "export-ml")
    execute(ROOT, run_id, "bi")


with DAG(dag_id="contoso_forge_cosmos", start_date=datetime(2026, 1, 1, tzinfo=timezone.utc), schedule=None,
         catchup=False, max_active_runs=1, tags=["contoso-forge", "v1.5", "cosmos"]) as dag:
    silver = PythonOperator(task_id="prepare_silver", python_callable=prepare_run, op_kwargs={"run_id": "{{ run_id }}"})
    dbt = DbtTaskGroup(
        group_id="dbt_models_and_tests",
        project_config=ProjectConfig(dbt_project_path=ROOT / "factory/dbt", manifest_path=ROOT / "factory/dbt_manifest.json"),
        profile_config=ProfileConfig(profile_name="contoso_forge_customer_satisfaction", target_name="local", profiles_yml_filepath=ROOT / "factory/dbt/profiles.yml"),
        execution_config=ExecutionConfig(dbt_executable_path=os.environ.get("FORGE_DBT_EXECUTABLE", "dbt")),
        render_config=RenderConfig(load_method=LoadMode.DBT_MANIFEST, test_behavior=TestBehavior.AFTER_EACH),
        operator_args={"append_env": True, "env": {
            "FORGE_LAKE_ROOT": "{{ ti.xcom_pull(task_ids='prepare_silver') }}/lake",
            "FORGE_DUCKDB_PATH": "{{ ti.xcom_pull(task_ids='prepare_silver') }}/cosmos.duckdb",
            "FORGE_TRUTH_MANIFEST": str(ROOT / "truth_manifest.json"),
            "FORGE_LABEL_AS_OF": LABEL_AS_OF
        }, "pool": "forge_duckdb_single_writer"}
    )
    evidence = PythonOperator(task_id="plain_build_reconcile_and_report", python_callable=finish_run, op_kwargs={"run_id": "{{ run_id }}"})
    silver >> dbt >> evidence
