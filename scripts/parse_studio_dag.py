#!/usr/bin/env python3
"""Real Airflow DagBag validation; requires the pinned Airflow runtime (Linux CI)."""
import argparse
import json
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, type=Path)
    args = parser.parse_args()
    dag_path = args.project.resolve() / "airflow/dags/contoso_forge_pipeline.py"
    sys.path.insert(0, str(dag_path.parent))
    from airflow.models import DagBag
    bag = DagBag(dag_folder=str(dag_path), include_examples=False, safe_mode=False)
    if bag.import_errors:
        raise RuntimeError(json.dumps(bag.import_errors, indent=2))
    if len(bag.dags) != 1:
        raise RuntimeError(f"Expected exactly one Studio DAG; got {list(bag.dags)}")
    dag = next(iter(bag.dags.values()))
    if set(dag.task_ids) != {"verify_source", "prepare_colab", "await_result", "reconcile"}:
        raise RuntimeError(f"Unexpected default pipeline tasks: {dag.task_ids}")
    report = {"status": "dag-parse-validated", "dagId": dag.dag_id,
              "taskIds": sorted(dag.task_ids), "executed": False}
    output = args.project / "validation/airflow.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
