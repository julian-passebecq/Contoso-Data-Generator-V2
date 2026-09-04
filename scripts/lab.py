#!/usr/bin/env python3
"""Portable command surface for the local and Codespaces Compose lab."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import time


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
WORKSPACE_VOLUME = "contoso-forge-workspace"
DAG_ID = "contoso_forge_customer_satisfaction"
JOB_IMAGES = ("contoso-forge:local", "contoso-forge-spark:local", "contoso-forge-dbt:local")


def configure_runtime_environment() -> None:
    """Pass a Linux Docker socket's owning group through to Airflow task workers."""
    socket_path = Path("/var/run/docker.sock")
    if os.name != "nt" and socket_path.exists():
        os.environ.setdefault("FORGE_DOCKER_SOCKET_GID", str(socket_path.stat().st_gid))


def run(*command: str, capture: bool = False, check: bool = True) -> subprocess.CompletedProcess[str]:
    print("+", " ".join(command), flush=True)
    return subprocess.run(
        command,
        cwd=REPOSITORY_ROOT,
        check=check,
        text=True,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
    )


def compose(
    *arguments: str,
    capture: bool = False,
    check: bool = True,
) -> subprocess.CompletedProcess[str]:
    return run("docker", "compose", "-f", "compose.yaml", *arguments, capture=capture, check=check)


def docker_device_path(path: Path) -> str:
    resolved = str(path.resolve())
    if os.name == "nt":
        match = re.fullmatch(r"([A-Za-z]):[\\/](.*)", resolved)
        if not match:
            raise RuntimeError(f"Cannot convert Windows path for Docker Desktop: {resolved}")
        suffix = match.group(2).replace("\\", "/")
        return f"/run/desktop/mnt/host/{match.group(1).lower()}/{suffix}"

    match = re.fullmatch(r"/mnt/([A-Za-z])/(.*)", resolved)
    if match:
        return f"/run/desktop/mnt/host/{match.group(1).lower()}/{match.group(2)}"
    if platform.system() == "Darwin":
        return f"/host_mnt{resolved}"
    return resolved


def ensure_workspace_volume() -> None:
    run("docker", "info", "--format", "{{.ServerVersion}}")
    device = docker_device_path(REPOSITORY_ROOT)
    inspected = run("docker", "volume", "inspect", WORKSPACE_VOLUME, capture=True, check=False)
    if inspected.returncode == 0:
        details = json.loads(inspected.stdout)[0]
        current = (details.get("Options") or {}).get("device")
        if current != device:
            raise RuntimeError(
                f"Docker volume {WORKSPACE_VOLUME!r} points to {current!r}, not {device!r}. "
                "Stop this lab, remove that volume, then retry. Removing this bind-backed volume does not delete repository files."
            )
    else:
        run(
            "docker", "volume", "create",
            "--driver", "local",
            "--opt", "type=none",
            "--opt", "o=bind",
            "--opt", f"device={device}",
            WORKSPACE_VOLUME,
        )

    probe = run(
        "docker", "run", "--rm",
        "--mount", f"type=volume,source={WORKSPACE_VOLUME},target=/workspace",
        "alpine:3.22.1",
        "test", "-f", "/workspace/ContosoDGV2.sln",
        check=False,
    )
    if probe.returncode != 0:
        raise RuntimeError("The bind-backed Docker workspace volume cannot see this repository.")


def build() -> None:
    ensure_workspace_volume()
    compose("--profile", "jobs", "--profile", "airflow", "build", "forge", "spark-job", "dbt", "airflow")
    compose("--profile", "airflow", "pull", "postgres")


def ensure_job_images() -> None:
    missing = [
        image
        for image in JOB_IMAGES
        if run("docker", "image", "inspect", image, capture=True, check=False).returncode != 0
    ]
    if missing:
        print(f"Building missing DAG job images: {', '.join(missing)}", flush=True)
        compose("--profile", "jobs", "build", "forge", "spark-job", "dbt")


def generate() -> None:
    ensure_workspace_volume()
    compose("run", "--rm", "forge")


def run_spark(stage: str) -> None:
    ensure_workspace_volume()
    if not (REPOSITORY_ROOT / "out" / "truth_manifest.json").exists():
        generate()
    compose(
        "run", "--rm", "spark-job",
        "/workspace/out/pyspark/bronze_silver.py", "--stage", stage,
        "--truth-manifest", "/workspace/out/truth_manifest.json",
    )


def run_dbt() -> None:
    ensure_workspace_volume()
    compose("run", "--rm", "dbt")


def wait_for_airflow(timeout_seconds: int = 300) -> None:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        service = compose("ps", "-q", "airflow", capture=True, check=False).stdout.strip()
        inspected = run(
            "docker", "inspect", "--format", "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}",
            service, capture=True, check=False,
        ) if service else None
        if inspected and inspected.returncode == 0 and inspected.stdout.strip() == "healthy":
            print("Airflow is healthy at http://localhost:8080", flush=True)
            return
        time.sleep(5)
    compose("logs", "--tail", "200", "airflow")
    raise RuntimeError("Airflow did not become healthy within the timeout.")


def up_airflow() -> None:
    ensure_workspace_volume()
    if not (REPOSITORY_ROOT / "out" / "airflow" / "dags" / "contoso_forge_customer_satisfaction.py").exists():
        generate()
    compose("--profile", "airflow", "up", "--detach", "--build", "postgres", "airflow")
    wait_for_airflow()


def run_pipeline() -> None:
    ensure_job_images()
    generate()
    up_airflow()
    compose(
        "exec", "-T", "--user", "airflow", "airflow",
        "airflow", "dags", "test", "--use-executor", DAG_ID, "2024-02-01T00:00:00+00:00",
    )


def smoke() -> None:
    build()
    generate()
    run_spark("smoke")
    run_spark("all")
    run_dbt()
    run_pipeline()


def down() -> None:
    compose("--profile", "airflow", "down", "--remove-orphans")


def main() -> int:
    configure_runtime_environment()
    parser = argparse.ArgumentParser(prog="lab", description=__doc__)
    subcommands = parser.add_subparsers(dest="command", required=True)
    for name in ("prepare", "build", "generate", "run-dbt", "run-pipeline", "smoke", "down", "validate"):
        subcommands.add_parser(name)
    spark_parser = subcommands.add_parser("run-spark")
    spark_parser.add_argument("--stage", choices=("all", "bronze", "silver", "smoke"), default="all")
    subcommands.add_parser("up-airflow")
    args = parser.parse_args()

    try:
        if args.command == "prepare":
            ensure_workspace_volume()
        elif args.command == "build":
            build()
        elif args.command == "generate":
            generate()
        elif args.command == "run-spark":
            run_spark(args.stage)
        elif args.command == "run-dbt":
            run_dbt()
        elif args.command == "up-airflow":
            up_airflow()
        elif args.command == "run-pipeline":
            run_pipeline()
        elif args.command == "smoke":
            smoke()
        elif args.command == "down":
            down()
        elif args.command == "validate":
            ensure_workspace_volume()
            compose("--profile", "jobs", "--profile", "airflow", "config", "--quiet")
        return 0
    except (RuntimeError, subprocess.CalledProcessError) as exception:
        print(f"lab: {exception}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
