#nullable enable

namespace DatabaseGenerator.Forge.Pipeline;

internal static class PipelineRuntimeTemplates
{
    internal const string Runtime = """"
        """Generated neutral pipeline helpers; no Airflow/cloud calls at import."""
        import hashlib
        import json
        import os
        from pathlib import Path
        import subprocess
        import sys
        import time


        class ManualCheckpointPending(RuntimeError):
            pass


        def project_root(root=None):
            value = root or os.environ.get("FORGE_PROJECT_ROOT")
            if not value:
                raise RuntimeError("Set FORGE_PROJECT_ROOT to the generated project directory")
            return Path(value).resolve()


        def run_paths(root, run_id, pipeline_id="contoso_forge_pipeline"):
            if not isinstance(run_id, str) or not run_id.strip():
                raise ValueError("A nonempty run ID is required")
            state_root = Path(os.environ.get("FORGE_STATE_ROOT", str(root / ".forge" / "state"))).resolve()
            identity = hashlib.sha256((pipeline_id + "\n" + run_id).encode("utf-8")).hexdigest()
            state = state_root / "runs" / identity
            state.mkdir(parents=True, exist_ok=True)
            return state, state / "work_order.json", state / "work_package.zip", state / "result_manifest.json"


        def invoke(root, script, arguments, timeout):
            path = root / script
            if not path.is_file():
                raise RuntimeError(f"Required generated runtime is absent: {path}")
            subprocess.run([sys.executable, str(path), *[str(arg) for arg in arguments]],
                           cwd=str(root), check=True, timeout=timeout)


        def verify_source(root):
            manifest = json.loads((root / "truth_manifest.json").read_text(encoding="utf-8-sig"))
            expected = manifest.get("sourceFileSha256")
            if not isinstance(expected, dict) or not expected:
                raise RuntimeError("truth_manifest.json must contain nonempty sourceFileSha256")
            source = (root / "data" / "source").resolve()
            for filename, digest in sorted(expected.items()):
                if Path(filename).name != filename or "/" in filename or "\\" in filename:
                    raise RuntimeError("Unsafe source filename in truth manifest")
                path = source / filename
                if not path.is_file() or path.resolve().parent != source:
                    raise RuntimeError(f"Missing or unsafe source file: {filename}")
                actual = hashlib.sha256(path.read_bytes()).hexdigest()
                if actual != digest:
                    raise RuntimeError(f"Source checksum mismatch: {filename}")
            return True


        def reconcile(root, order, result, timeout, execution_scope="spark-and-bigquery"):
            if not order.is_file():
                raise RuntimeError("This run has no work order; execute its package activity first")
            issued_scope = json.loads(order.read_text(encoding="utf-8-sig")).get("executionScope", "spark-and-bigquery")
            if issued_scope != execution_scope:
                raise ValueError("Requested execution scope does not match this run's issued work order")
            if not result.is_file():
                return False
            # Existing malformed/stale/wrong-run results fail instead of silently waiting.
            invoke(root, "colab/work_order.py", ["reconcile", "--root", root, "--work-order", order,
                                              "--result", result], timeout)
            return True


        def execute_activity(activity, root=None, run_id=None, pipeline_id="contoso_forge_pipeline",
                             execution_scope="spark-and-bigquery"):
            if execution_scope not in ("spark", "spark-and-bigquery"):
                raise ValueError("Execution scope must be spark or spark-and-bigquery")
            root = project_root(root)
            operation = activity["operation"]
            timeout = activity["timeoutSeconds"]
            if operation == "unsupported":
                raise NotImplementedError(f"Activity {activity['id']}: {activity['reason']}")
            if operation == "verify-source":
                return verify_source(root)
            state, order, package, result = run_paths(root, run_id, pipeline_id)
            if operation == "prepare-colab":
                # The helper validates identity, source/runtime hashes and expiry before reusing state.
                invoke(root, "colab/work_order.py", ["package", "--root", root, "--run-id", run_id,
                                                   "--work-order", order, "--package", package,
                                                   "--scope", execution_scope], timeout)
                print(f"Manual Colab package: {package}\nReturn result to: {result}")
                return True
            if operation in ("await-colab", "reconcile-colab"):
                if not reconcile(root, order, result, min(timeout, 300), execution_scope):
                    if operation == "await-colab":
                        raise ManualCheckpointPending(f"Run Colab with {package}; return its result manifest to {result}")
                    raise RuntimeError(f"Required reconciled result is missing: {result}")
                return True
            raise NotImplementedError(f"Unknown compiled operation: {operation}")


        def sensor_activity(activity, root=None, run_id=None, pipeline_id="contoso_forge_pipeline",
                            execution_scope="spark-and-bigquery"):
            try:
                return execute_activity(activity, root=root, run_id=run_id, pipeline_id=pipeline_id,
                                        execution_scope=execution_scope)
            except ManualCheckpointPending as pending:
                print(str(pending))
                return False


        def run_sequential(plan, root, run_id, execution_scope="spark-and-bigquery"):
            completed = set()
            for activity in plan["activities"]:
                if not set(activity["dependsOn"]).issubset(completed):
                    raise RuntimeError(f"Unmet dependencies for {activity['id']}")
                for attempt in range(1, activity["maximumAttempts"] + 1):
                    started = time.monotonic()
                    try:
                        execute_activity(activity, root=root, run_id=run_id, pipeline_id=plan["pipelineId"],
                                         execution_scope=execution_scope)
                        if time.monotonic() - started > activity["timeoutSeconds"]:
                            raise TimeoutError(f"Activity {activity['id']} exceeded its timeout")
                        completed.add(activity["id"])
                        print(f"succeeded:{activity['id']}")
                        break
                    except (ManualCheckpointPending, NotImplementedError):
                        raise
                    except Exception:
                        if attempt == activity["maximumAttempts"]:
                            raise
                        time.sleep(activity["backoffSeconds"])
            return completed
        """";

    internal const string LocalRunner = """
        #!/usr/bin/env python3
        import argparse
        import json
        from pathlib import Path
        import sys
        from forge_pipeline_runtime import ManualCheckpointPending, run_sequential


        def main():
            parser = argparse.ArgumentParser(description="Execute a compiled neutral plan; exit 75 means a human result is required")
            parser.add_argument("--root", default=str(Path(__file__).resolve().parents[1]))
            parser.add_argument("--run-id", required=True, help="Unique execution ID; reuse only to resume the same work order")
            parser.add_argument("--scope", choices=("spark", "spark-and-bigquery"), default="spark-and-bigquery",
                                help="Explicit Spark-only proof or the full default BigQuery work order")
            args = parser.parse_args()
            root = Path(args.root).resolve()
            plan = json.loads((root / "local_plan.json").read_text(encoding="utf-8-sig"))
            try:
                run_sequential(plan, root, args.run_id, execution_scope=args.scope)
            except ManualCheckpointPending as pending:
                print(str(pending), file=sys.stderr)
                return 75
            return 0


        if __name__ == "__main__":
            raise SystemExit(main())
        """;

    internal const string Airflow = """
        # Generated from pipeline.json. Recompile the neutral contract instead of editing this file.
        import base64
        from datetime import datetime, timedelta, timezone
        import json
        from pathlib import Path
        import sys
        from airflow.sdk import DAG
        from airflow.providers.standard.operators.python import PythonOperator
        from airflow.providers.standard.sensors.python import PythonSensor

        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from forge_pipeline_runtime import execute_activity, sensor_activity

        PLAN = json.loads(base64.b64decode("__PLAN_BASE64__").decode("utf-8"))

        with DAG(
            dag_id=PLAN["pipelineId"],
            description="Neutral Contoso Forge pipeline with explicit Colab manual checkpoint",
            start_date=datetime(2026, 1, 1, tzinfo=timezone.utc),
            schedule=None,
            catchup=False,
            max_active_runs=1,
            tags=["contoso-forge", "pipeline-studio", PLAN["presetId"]],
        ) as dag:
            tasks = {}
            for activity in PLAN["activities"]:
                options = dict(
                    task_id=activity["id"],
                    op_kwargs={"activity": activity, "run_id": "{{ run_id }}", "pipeline_id": PLAN["pipelineId"],
                               "execution_scope": "{{ dag_run.conf.get('executionScope', 'spark-and-bigquery') }}"},
                    retries=activity["maximumAttempts"] - 1,
                    retry_delay=timedelta(seconds=activity["backoffSeconds"]),
                    execution_timeout=timedelta(seconds=activity["timeoutSeconds"]),
                )
                if activity["operation"] == "await-colab":
                    tasks[activity["id"]] = PythonSensor(
                        python_callable=sensor_activity,
                        mode="reschedule",
                        poke_interval=60,
                        timeout=activity["timeoutSeconds"],
                        soft_fail=False,
                        **options,
                    )
                else:
                    tasks[activity["id"]] = PythonOperator(python_callable=execute_activity, **options)
            for activity in PLAN["activities"]:
                for parent in activity["dependsOn"]:
                    tasks[parent] >> tasks[activity["id"]]
        """;
}
