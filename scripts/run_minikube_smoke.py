#!/usr/bin/env python3
"""Install/check the generated Airflow lab and record observed execution gates.

Uses an explicit Minikube context. Never publishes Git source, fabricates a Colab
result, deletes a cluster, or treats Helm rendering as runtime validation.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import time

CHART_VERSION = "1.22.0"
NAMESPACE = "contoso-forge"


def redact(value):
    value = re.sub(r"(://)[^/@\s]+:[^/@\s]+@", r"\1<redacted>@", value)
    return re.sub(r"(?i)(password|token|secret)([\s=:]+)[^\s,;]+", r"\1\2<redacted>", value)


def run(command, timeout=120, input_text=None):
    result = subprocess.run([str(item) for item in command], text=True, input=input_text,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            encoding="utf-8", errors="replace", timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"{Path(str(command[0])).name} exited {result.returncode}: "
                           + redact((result.stderr or result.stdout)[-4000:]))
    return result.stdout


def marker_json(output):
    lines = [line.split("FORGE_EVIDENCE=", 1)[1] for line in output.splitlines()
             if "FORGE_EVIDENCE=" in line]
    if not lines:
        raise RuntimeError("Airflow did not return a structured evidence marker")
    return json.loads(lines[-1])


def selected_pods(document):
    return [{"name": p["metadata"]["name"], "phase": p.get("status", {}).get("phase"),
             "ready": any(c["type"] == "Ready" and c["status"] == "True"
                          for c in p.get("status", {}).get("conditions", [])),
             "containers": [{"name": c["name"], "ready": c.get("ready", False),
                             "restartCount": c.get("restartCount", 0),
                             "image": c.get("image"), "imageId": c.get("imageID")}
                            for c in p.get("status", {}).get("containerStatuses", [])]}
            for p in document["items"]]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--context", required=True, help="Existing Minikube profile/context")
    parser.add_argument("--kubectl", default="kubectl")
    parser.add_argument("--helm", default="helm")
    parser.add_argument("--chart", type=Path, help="Downloaded official chart directory; version checked")
    parser.add_argument("--install", action="store_true", help="Apply generated lab resources and install/upgrade Airflow")
    parser.add_argument("--values-override", type=Path, help="Explicit additional lab-only Helm values")
    execution = parser.add_mutually_exclusive_group()
    execution.add_argument("--trigger", action="store_true", help="Trigger the actual generated DAG up to its manual checkpoint")
    execution.add_argument("--observe-run", action="store_true", help="Observe a previously issued run after returning its result")
    parser.add_argument("--scope", choices=("spark", "spark-and-bigquery"), default="spark-and-bigquery")
    parser.add_argument("--run-id")
    parser.add_argument("--wait-seconds", type=int, default=300)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.observe_run and not args.run_id:
        parser.error("--observe-run requires the actual --run-id")
    args.run_id = args.run_id or "forge_v13_smoke_" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    project = args.project.resolve()
    plan = json.loads((project / "local_plan.json").read_text(encoding="utf-8-sig"))
    dag_id = plan["pipelineId"]
    evidence = {"contractVersion": "1.3", "target": "airflow-minikube", "status": "runtime-incomplete",
                "recordedAt": datetime.now(timezone.utc).isoformat(), "context": args.context,
                "project": str(project), "chartVersion": CHART_VERSION, "runId": args.run_id,
                "executionScope": args.scope,
                "gates": {name: False for name in ["clusterReady", "chartInstalled", "podsReady",
                    "gitSyncGeneratedDag", "dagParsed", "prepareWorkOrderSucceeded", "manualCheckpointPending",
                    "returnedResultReconciled"]}, "cloudApplied": False}
    k = [args.kubectl, "--context", args.context]
    kn = [*k, "-n", NAMESPACE]
    try:
        nodes = json.loads(run([*k, "get", "nodes", "-o", "json"]))
        if not nodes["items"] or not all("minikube.k8s.io/name" in n["metadata"].get("labels", {}) for n in nodes["items"]):
            raise RuntimeError("This runner requires an actual Minikube context")
        evidence["nodes"] = [{"name": n["metadata"]["name"],
                              "kubernetesVersion": n["status"]["nodeInfo"]["kubeletVersion"],
                              "ready": any(c["type"] == "Ready" and c["status"] == "True"
                                           for c in n["status"]["conditions"])} for n in nodes["items"]]
        evidence["gates"]["clusterReady"] = all(n["ready"] for n in evidence["nodes"])
        if not evidence["gates"]["clusterReady"]:
            raise RuntimeError("Minikube nodes are not Ready")
        if args.install:
            run([sys.executable, project / "minikube/bootstrap_secrets.py", "--kubectl", args.kubectl, "--context", args.context])
            for name in ["metadata-postgres.yaml", "runtime-state.yaml"]:
                run([*k, "apply", "-f", project / "minikube" / name])
            run([*kn, "rollout", "status", "statefulset/contoso-forge-postgres", "--timeout=300s"], timeout=330)
            chart = [str(args.chart.resolve())] if args.chart else ["airflow", "--repo", "https://airflow.apache.org"]
            if args.chart and not re.search(r"^version:\s*[\"']?1\.22\.0[\"']?\s*$",
                                             (args.chart / "Chart.yaml").read_text(), re.MULTILINE):
                raise RuntimeError("The official chart must be version 1.22.0")
            command = [args.helm, "upgrade", "--install", "airflow", *chart, "--version", CHART_VERSION,
                       "--kube-context", args.context, "--namespace", NAMESPACE,
                       "--values", project / "minikube/values.yaml", "--timeout", "15m", "--wait"]
            if args.values_override:
                command += ["--values", args.values_override.resolve()]
            run(command, timeout=960)
        releases = json.loads(run([args.helm, "list", "--kube-context", args.context,
                                   "--namespace", NAMESPACE, "--output", "json"]))
        release = next((r for r in releases if r["name"] == "airflow"), None)
        evidence["helmRelease"] = release
        evidence["gates"]["chartInstalled"] = bool(release and release["chart"] == "airflow-" + CHART_VERSION
                                                    and release["status"] == "deployed")
        pods = json.loads(run([*kn, "get", "pods", "-o", "json"]))
        evidence["pods"] = selected_pods(pods)
        required = [p for p in evidence["pods"] if any(name in p["name"] for name in
                                                     ["airflow-api-server", "airflow-scheduler", "airflow-dag-processor", "contoso-forge-postgres"])]
        evidence["gates"]["podsReady"] = len(required) >= 4 and all(p["ready"] for p in required)
        if not evidence["gates"]["podsReady"]:
            raise RuntimeError("Airflow and metadata pods are not all Ready")
        scheduler = next(p for p in pods["items"] if p["metadata"].get("labels", {}).get("component") == "scheduler")
        sync = next(c for c in scheduler["spec"]["containers"] if c["name"] == "git-sync")
        evidence["gitSync"] = {e["name"]: e.get("value") for e in sync.get("env", [])
                               if e["name"] in ["GITSYNC_REPO", "GITSYNC_REF", "GITSYNC_ROOT", "GITSYNC_LINK"]}
        repo = evidence["gitSync"].get("GITSYNC_REPO", "")
        evidence["gitSourceKind"] = "github" if repo.startswith("https://github.com/") else "local-or-other-git"
        exe = [*kn, "exec", scheduler["metadata"]["name"], "-c", "scheduler", "--"]
        code = """import os,sys,json,hashlib
from pathlib import Path
from airflow.models import DagBag
root=Path(os.environ['FORGE_PROJECT_ROOT']); path=root/'airflow/dags/contoso_forge_pipeline.py'
sys.path.insert(0,str(path.parent))
bag=DagBag(dag_folder=str(path),include_examples=False,safe_mode=False)
report={'dagSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'dagIds':sorted(bag.dags),
        'taskIds':{k:sorted(v.task_ids) for k,v in bag.dags.items()},'importErrors':bag.import_errors,
        'projectRoot':str(root),'resolvedCheckout':str(root.resolve())}
print('FORGE_EVIDENCE='+json.dumps(report))
"""
        evidence["dagParse"] = marker_json(run([*exe, "python", "-c", code]))
        expected = hashlib.sha256((project / "airflow/dags/contoso_forge_pipeline.py").read_bytes()).hexdigest()
        evidence["gates"]["gitSyncGeneratedDag"] = evidence["dagParse"]["dagSha256"] == expected
        evidence["gates"]["dagParsed"] = (not evidence["dagParse"]["importErrors"]
                and evidence["dagParse"]["taskIds"].get(dag_id) == sorted(a["id"] for a in plan["activities"]))
        if not evidence["gates"]["gitSyncGeneratedDag"] or not evidence["gates"]["dagParsed"]:
            raise RuntimeError("GitSync DAG bytes or parsed task identities do not match the generated project")
        if args.trigger or args.observe_run:
            if args.trigger:
                run([*exe, "airflow", "dags", "unpause", dag_id])
                run([*exe, "airflow", "dags", "trigger", dag_id, "--run-id", args.run_id,
                     "--conf", json.dumps({"executionScope": args.scope})])
            deadline = time.monotonic() + args.wait_seconds
            while time.monotonic() < deadline:
                status_code = """import json
from airflow.models.taskinstance import TaskInstance
from airflow.utils.session import create_session
with create_session() as session:
    tasks=session.query(TaskInstance).filter(TaskInstance.dag_id==%r,TaskInstance.run_id==%r).all()
    print('FORGE_EVIDENCE='+json.dumps({t.task_id:t.state for t in tasks}))
""" % (dag_id, args.run_id)
                states = marker_json(run([*exe, "python", "-c", status_code]))
                evidence["taskStates"] = states
                evidence["gates"]["prepareWorkOrderSucceeded"] = states.get("prepare_colab") == "success"
                evidence["gates"]["manualCheckpointPending"] = states.get("await_result") == "up_for_reschedule"
                evidence["gates"]["returnedResultReconciled"] = states.get("reconcile") == "success"
                if any(value in ["failed", "upstream_failed"] for value in states.values()):
                    raise RuntimeError("A generated DAG task failed; inspect the Airflow task logs")
                if evidence["gates"]["returnedResultReconciled"] or (args.trigger and evidence["gates"]["manualCheckpointPending"]):
                    break
                time.sleep(10)
            identity = hashlib.sha256((dag_id + "\n" + args.run_id).encode()).hexdigest()
            evidence["workOrderDirectory"] = "/opt/airflow/forge-state/runs/" + identity
            evidence["schedulerPod"] = scheduler["metadata"]["name"]
            files_code = """import json,hashlib
from pathlib import Path
root=Path(%r); report={}
for name in ['work_order.json','work_package.zip','result_manifest.json']:
    path=root/name
    if path.is_file():
        entry={'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bytes':path.stat().st_size}
        if name.endswith('.json'):
            data=json.loads(path.read_text())
            entry.update({k:data.get(k) for k in ['workOrderId','runId','executionScope','resultScope','executionRuntime','datasetFingerprint'] if k in data})
        report[name]=entry
print('FORGE_EVIDENCE='+json.dumps(report))
""" % evidence["workOrderDirectory"]
            evidence["runFiles"] = marker_json(run([*exe, "python", "-c", files_code]))
            issued = evidence["runFiles"].get("work_order.json", {})
            if issued.get("runId") != args.run_id or issued.get("executionScope", "spark-and-bigquery") != args.scope:
                raise RuntimeError("Observed work-order identity/scope does not match the requested run")
            if not (evidence["gates"]["manualCheckpointPending"] or evidence["gates"]["returnedResultReconciled"]):
                raise RuntimeError("The DAG did not reach its manual checkpoint or reconciliation before the observation timeout")
        if (evidence["gates"]["returnedResultReconciled"] and evidence["gitSourceKind"] == "github"
                and args.scope == "spark-and-bigquery"):
            evidence["status"] = "validated-runtime"
        elif evidence["gates"]["returnedResultReconciled"]:
            evidence["status"] = "validated-local-spark-return-cycle" if args.scope == "spark" else "validated-local-control-plane-return-cycle"
        elif evidence["gates"]["manualCheckpointPending"]:
            evidence["status"] = "validated-local-control-plane-manual-checkpoint"
        elif all(evidence["gates"][name] for name in ["chartInstalled", "podsReady", "gitSyncGeneratedDag", "dagParsed"]):
            evidence["status"] = "validated-local-install-and-dag-parse"
    except (RuntimeError, OSError, subprocess.TimeoutExpired, ValueError, StopIteration) as error:
        evidence["error"] = redact(str(error))
        try:
            evidence["pods"] = selected_pods(json.loads(run([*kn, "get", "pods", "-o", "json"], timeout=20)))
        except Exception:
            pass
    finally:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"status": evidence["status"], "gates": evidence["gates"], "evidence": str(args.output),
                      "error": evidence.get("error")}))
    return 1 if evidence.get("error") else 0


if __name__ == "__main__":
    raise SystemExit(main())
