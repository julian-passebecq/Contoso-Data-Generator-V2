# artifact-status: validated
"""Airflow 3 DAG for the local Contoso Forge reference pipeline."""

from __future__ import annotations

import os
from datetime import datetime, timedelta, timezone

from airflow.sdk import DAG
from airflow.providers.docker.operators.docker import DockerOperator
from airflow.providers.standard.operators.empty import EmptyOperator
from docker.types import Mount


WORKSPACE_VOLUME = os.environ.get("FORGE_WORKSPACE_VOLUME", "contoso-forge-workspace")
WORKSPACE_MOUNT = Mount(source=WORKSPACE_VOLUME, target="/workspace", type="volume")
COMMON = {
    "docker_url": "unix://var/run/docker.sock",
    "api_version": "auto",
    "mounts": [WORKSPACE_MOUNT],
    "mount_tmp_dir": False,
    "auto_remove": "success",
    "force_pull": False,
    "network_mode": "bridge",
}


with DAG(
    dag_id="contoso_forge_customer_satisfaction",
    description="Contoso Forge -> Delta Bronze -> Parquet Silver -> dbt/DuckDB Gold",
    schedule=None,
    start_date=datetime(2024, 1, 1, tzinfo=timezone.utc),
    catchup=False,
    default_args={"retries": 1, "retry_delay": timedelta(seconds=15)},
    tags=["contoso-forge", "local-reference"],
) as dag:
    forge_generate = DockerOperator(
        task_id="forge_generate",
        image="contoso-forge:local",
        command=(
            "forge generate --project /workspace/examples/customer-satisfaction.project.json "
            "--output /workspace/out --lake /workspace/lake"
        ),
        **COMMON,
    )

    bronze_spark = DockerOperator(
        task_id="bronze_spark",
        image="contoso-forge-spark:local",
        command="/workspace/out/pyspark/bronze_silver.py --stage bronze",
        environment={"FORGE_LAKE_ROOT": "/workspace/lake"},
        **COMMON,
    )

    silver_spark = DockerOperator(
        task_id="silver_spark",
        image="contoso-forge-spark:local",
        command=(
            "/workspace/out/pyspark/bronze_silver.py --stage silver "
            "--truth-manifest /workspace/out/truth_manifest.json"
        ),
        environment={"FORGE_LAKE_ROOT": "/workspace/lake"},
        **COMMON,
    )

    dbt_build_gold = DockerOperator(
        task_id="dbt_build_gold",
        image="contoso-forge-dbt:local",
        command="run --project-dir /workspace/out/dbt --profiles-dir /workspace/out/dbt --target local",
        environment={"FORGE_LAKE_ROOT": "/workspace/lake"},
        **COMMON,
    )

    dbt_test = DockerOperator(
        task_id="dbt_test",
        image="contoso-forge-dbt:local",
        command="test --project-dir /workspace/out/dbt --profiles-dir /workspace/out/dbt --target local",
        environment={"FORGE_LAKE_ROOT": "/workspace/lake"},
        retries=0,
        **COMMON,
    )

    ml_handoff = EmptyOperator(task_id="ml_handoff")
    semantic_handoff = EmptyOperator(task_id="semantic_handoff")

    forge_generate >> bronze_spark >> silver_spark >> dbt_build_gold >> dbt_test
    dbt_test >> [ml_handoff, semantic_handoff]
