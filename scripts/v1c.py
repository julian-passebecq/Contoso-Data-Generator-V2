#!/usr/bin/env python3
"""Create and verify the small Contoso Forge V1C kind/OpenTofu lab."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import shutil
import subprocess
import sys
from typing import Sequence


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
KIND_CONFIG = REPOSITORY_ROOT / "infra" / "kind" / "cluster.yaml"
TOFU_ROOT = REPOSITORY_ROOT / "infra" / "tofu"
TOOLS_ROOT = REPOSITORY_ROOT / ".tools"

CLUSTER_NAME = "contoso-forge"
KUBE_CONTEXT = f"kind-{CLUSTER_NAME}"
NAMESPACE = "contoso-forge"
IMAGES = (
    "contoso-forge:local",
    "contoso-forge-dbt:local",
    "contoso-forge-spark:local",
)
JOBS = (
    "contoso-forge-generator",
    "contoso-forge-dbt-validate",
    "contoso-forge-spark-submit",
)


def executable(name: str) -> str:
    """Find a tool on PATH or in the repo-local, ignored .tools directory."""
    candidates = [TOOLS_ROOT / name]
    if sys.platform == "win32":
        candidates.insert(0, TOOLS_ROOT / f"{name}.exe")
    for candidate in candidates:
        if candidate.is_file():
            return str(candidate)
    found = shutil.which(name)
    if found:
        return found
    raise RuntimeError(
        f"Required tool {name!r} was not found on PATH or in {TOOLS_ROOT}. "
        "See docs/kubernetes.md for the pinned V1C tool versions."
    )


def run(
    command: Sequence[str],
    *,
    cwd: Path = REPOSITORY_ROOT,
    capture: bool = False,
    check: bool = True,
) -> subprocess.CompletedProcess[str]:
    rendered = " ".join(str(part) for part in command)
    print(f"+ {rendered}", flush=True)
    return subprocess.run(
        [str(part) for part in command],
        cwd=cwd,
        check=check,
        text=True,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
    )


def tool(name: str, *arguments: str, **kwargs: object) -> subprocess.CompletedProcess[str]:
    return run([executable(name), *arguments], **kwargs)


def kind_clusters() -> set[str]:
    result = tool("kind", "get", "clusters", capture=True, check=False)
    if result.returncode != 0:
        return set()
    return {line.strip() for line in result.stdout.splitlines() if line.strip()}


def require_images() -> None:
    missing = []
    for image in IMAGES:
        result = tool("docker", "image", "inspect", image, capture=True, check=False)
        if result.returncode != 0:
            missing.append(image)
    if missing:
        print(f"Building missing V1C images: {', '.join(missing)}", flush=True)
        tool(
            "docker",
            "compose",
            "-f",
            "compose.yaml",
            "--profile",
            "jobs",
            "build",
            "forge",
            "spark-job",
            "dbt",
        )


def create_cluster() -> None:
    tool("docker", "info", "--format", "{{.ServerVersion}}")
    if CLUSTER_NAME not in kind_clusters():
        tool(
            "kind",
            "create",
            "cluster",
            "--config",
            str(KIND_CONFIG),
            "--wait",
            "300s",
        )
    else:
        print(f"kind cluster {CLUSTER_NAME!r} already exists", flush=True)

    tool(
        "kubectl",
        "--context",
        KUBE_CONTEXT,
        "wait",
        "--for=condition=Ready",
        "node",
        "--all",
        "--timeout=180s",
    )


def load_images() -> None:
    require_images()
    for image in IMAGES:
        tool("kind", "load", "docker-image", "--name", CLUSTER_NAME, image)


def tofu(*arguments: str, capture: bool = False, check: bool = True) -> subprocess.CompletedProcess[str]:
    return tool("tofu", *arguments, cwd=TOFU_ROOT, capture=capture, check=check)


def init_tofu() -> None:
    tofu("fmt", "-check", "-recursive")
    tofu("init", "-upgrade=false")
    tofu("validate")


def current_run_id() -> str:
    result = tofu("output", "-raw", "run_id", capture=True, check=False)
    return result.stdout.strip() if result.returncode == 0 and result.stdout.strip() else "manual"


def new_run_id() -> str:
    return datetime.now(timezone.utc).strftime("v1c-%Y%m%d%H%M%S")


def apply(run_id: str | None = None) -> str:
    selected_run_id = run_id or new_run_id()
    init_tofu()
    tofu("apply", "-auto-approve", f"-var=run_id={selected_run_id}")
    return selected_run_id


def kubectl(*arguments: str, capture: bool = False, check: bool = True) -> subprocess.CompletedProcess[str]:
    return tool(
        "kubectl",
        "--context",
        KUBE_CONTEXT,
        *arguments,
        capture=capture,
        check=check,
    )


def job_logs(name: str) -> str:
    result = kubectl("logs", "-n", NAMESPACE, f"job/{name}", capture=True)
    print(f"\n--- {name} logs ---\n{result.stdout.rstrip()}\n", flush=True)
    return result.stdout


def spark_pods(run_id: str) -> list[dict[str, object]]:
    selector = f"contoso-forge-managed=spark,contoso-forge-run={run_id}"
    result = kubectl(
        "get",
        "pods",
        "-n",
        NAMESPACE,
        "-l",
        selector,
        "-o",
        "json",
        capture=True,
    )
    document = json.loads(result.stdout)
    return document.get("items", [])


def pod_role(pod: dict[str, object]) -> str:
    metadata = pod.get("metadata", {})
    labels = metadata.get("labels", {}) if isinstance(metadata, dict) else {}
    if not isinstance(labels, dict):
        return "unknown"
    return str(labels.get("spark-role") or labels.get("contoso-forge-role") or "unknown")


def verify(run_id: str | None = None) -> None:
    selected_run_id = run_id or current_run_id()

    kubectl("get", "namespace", NAMESPACE)
    kubectl("get", "pvc", "-n", NAMESPACE, "contoso-forge-workspace")
    for job in JOBS:
        kubectl(
            "wait",
            "-n",
            NAMESPACE,
            "--for=condition=complete",
            f"job/{job}",
            "--timeout=30s",
        )
    kubectl("get", "jobs,pods", "-n", NAMESPACE, "-o", "wide")

    forge_output = job_logs(JOBS[0])
    dbt_output = job_logs(JOBS[1])
    spark_submit_output = job_logs(JOBS[2])
    if "DBT_PROJECT_VALIDATED" not in dbt_output:
        raise RuntimeError("dbt Job did not emit its shared-storage/project validation marker")

    pods = spark_pods(selected_run_id)
    drivers = [pod for pod in pods if pod_role(pod) == "driver"]
    executors = [pod for pod in pods if pod_role(pod) == "executor"]
    if not drivers or not executors:
        roles = [pod_role(pod) for pod in pods]
        raise RuntimeError(
            "native Spark evidence is incomplete: "
            f"drivers={len(drivers)}, executors={len(executors)}, observed_roles={roles}"
        )

    driver = drivers[-1]
    driver_name = str(driver.get("metadata", {}).get("name"))
    driver_phase = str(driver.get("status", {}).get("phase"))
    if driver_phase != "Succeeded":
        raise RuntimeError(f"Spark driver {driver_name} ended in phase {driver_phase}, not Succeeded")
    driver_output = kubectl("logs", "-n", NAMESPACE, driver_name, capture=True).stdout
    print(f"\n--- {driver_name} logs ---\n{driver_output.rstrip()}\n", flush=True)
    if "Pi is roughly" not in driver_output:
        raise RuntimeError("Spark driver did not emit the expected Pi result")

    evidence = []
    for pod in pods:
        metadata = pod.get("metadata", {})
        status = pod.get("status", {})
        evidence.append(
            {
                "name": metadata.get("name") if isinstance(metadata, dict) else None,
                "role": pod_role(pod),
                "phase": status.get("phase") if isinstance(status, dict) else None,
            }
        )

    if not forge_output.strip() or not spark_submit_output.strip():
        raise RuntimeError("Forge or Spark submit Job logs were unexpectedly empty")
    print(
        "V1C_VALIDATED "
        f"namespace={NAMESPACE} run_id={selected_run_id} "
        f"spark_pods={json.dumps(evidence, separators=(',', ':'))}",
        flush=True,
    )


def up(run_id: str | None = None) -> None:
    create_cluster()
    load_images()
    selected_run_id = apply(run_id)
    verify(selected_run_id)


def down() -> None:
    if CLUSTER_NAME not in kind_clusters():
        print(f"kind cluster {CLUSTER_NAME!r} does not exist", flush=True)
        return

    selected_run_id = current_run_id()
    kubectl(
        "delete",
        "pods",
        "-n",
        NAMESPACE,
        "-l",
        "contoso-forge-managed=spark",
        "--ignore-not-found=true",
        check=False,
    )
    tofu("destroy", "-auto-approve", f"-var=run_id={selected_run_id}")
    tool("kind", "delete", "cluster", "--name", CLUSTER_NAME)


def versions() -> None:
    tool("docker", "version", "--format", "Docker {{.Server.Version}}")
    tool("kind", "version")
    tool("kubectl", "version", "--client")
    tool("tofu", "version")


def main() -> int:
    parser = argparse.ArgumentParser(prog="v1c", description=__doc__)
    subcommands = parser.add_subparsers(dest="command", required=True)
    subcommands.add_parser("versions", help="show the resolved local tool versions")
    subcommands.add_parser("prepare", help="create kind and load the three Compose job images")
    apply_parser = subcommands.add_parser("apply", help="apply the OpenTofu namespace and Jobs")
    apply_parser.add_argument("--run-id")
    verify_parser = subcommands.add_parser("verify", help="verify Jobs, shared artifacts, and Spark pods")
    verify_parser.add_argument("--run-id")
    up_parser = subcommands.add_parser("up", help="prepare, apply, and verify V1C end to end")
    up_parser.add_argument("--run-id")
    subcommands.add_parser("down", help="destroy V1C resources and delete the kind cluster")

    args = parser.parse_args()
    try:
        if args.command == "versions":
            versions()
        elif args.command == "prepare":
            create_cluster()
            load_images()
        elif args.command == "apply":
            apply(args.run_id)
        elif args.command == "verify":
            verify(args.run_id)
        elif args.command == "up":
            up(args.run_id)
        elif args.command == "down":
            down()
        return 0
    except (RuntimeError, subprocess.CalledProcessError, json.JSONDecodeError) as exception:
        print(f"v1c: {exception}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

