#!/usr/bin/env python3
"""Validate exported HCL/Helm without credentials, deployment, or shared IaC working state."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

CHART_VERSION = "1.22.0"


def command(binary, args, cwd):
    env = os.environ.copy()
    # A user's global engine working directory must not defeat dual-engine isolation.
    for name in list(env):
        if name == "TF_DATA_DIR" or name.startswith("TF_CLI_ARGS"):
            env.pop(name)
    env.update(TF_IN_AUTOMATION="1", TF_INPUT="0", CHECKPOINT_DISABLE="1")
    try:
        result = subprocess.run([binary, *args], cwd=cwd, env=env, text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                encoding="utf-8", errors="replace", timeout=300)
        return {"command": [Path(binary).name, *args], "exitCode": result.returncode,
                "stdout": result.stdout, "stderr": result.stderr}
    except (OSError, subprocess.TimeoutExpired) as error:
        return {"command": [Path(binary).name, *args], "exitCode": -1, "stdout": "", "stderr": str(error)}


def binary_path(name):
    resolved = shutil.which(name)
    if resolved:
        return str(Path(resolved).resolve())
    path = Path(name)
    return str(path.resolve()) if path.is_file() else None


def validate_iac(engine, executable, source):
    if not source.is_dir() or not any(source.glob("*.tf")):
        return {"engine": engine, "status": "not-exported"}
    binary = binary_path(executable)
    if binary is None:
        return {"engine": engine, "status": "tool-unavailable", "executable": executable}
    # Copy only reviewed source inputs; never carry local state, plans, provider caches,
    # backend overrides, or engine-generated lock files to the other validator.
    files = sorted([*source.glob("*.tf"), *source.glob("*.tfvars.json")])
    fingerprint = hashlib.sha256()
    with tempfile.TemporaryDirectory(prefix=f"forge-{engine}-") as directory:
        work = Path(directory)
        for path in files:
            if "override" in path.name:
                raise ValueError(f"Unexpected infrastructure override file: {path.name}")
            content = path.read_bytes()
            fingerprint.update(path.name.encode() + b"\0" + content)
            (work / path.name).write_bytes(content)
        result = {"engine": engine, "workingDirectory": str(work), "isolated": True,
                  "sourceSha256": fingerprint.hexdigest(), "status": "static-validated", "checks": []}
        for args in (["version"], ["fmt", "-check", "-recursive"],
                     ["init", "-backend=false", "-input=false", "-no-color"], ["validate", "-json"]):
            check = command(binary, args, work)
            result["checks"].append(check)
            if check["exitCode"] != 0:
                result["status"] = "failed"
                break
        return result


def validate_helm(executable, source, chart_source=None):
    if not (source / "values.yaml").is_file():
        return {"status": "not-exported"}
    binary = binary_path(executable)
    if binary is None:
        return {"status": "tool-unavailable", "executable": executable}
    with tempfile.TemporaryDirectory(prefix="forge-helm-") as directory:
        work = Path(directory)
        result = {"status": "static-validated", "chartVersion": CHART_VERSION, "checks": []}
        version = command(binary, ["version", "--short"], work)
        result["checks"].append(version)
        if version["exitCode"] != 0:
            result["status"] = "failed"
            return result
        if chart_source:
            chart = Path(chart_source).resolve()
        else:
            pull = command(binary, ["pull", "airflow", "--repo", "https://airflow.apache.org",
                                    "--version", CHART_VERSION, "--untar", "--untardir", str(work)], work)
            result["checks"].append(pull)
            if pull["exitCode"] != 0:
                result["status"] = "failed"
                return result
            chart = work / "airflow"
        chart_metadata = (chart / "Chart.yaml").read_text(encoding="utf-8")
        if not re.search(rf"^version:\s*[\"']?{re.escape(CHART_VERSION)}[\"']?\s*$", chart_metadata, re.MULTILINE):
            result.update(status="failed", error=f"Chart must be pinned to {CHART_VERSION}")
            return result
        for args in (["lint", str(chart), "--values", str(source / "values.yaml")],
                     ["template", "airflow", str(chart), "--namespace", "contoso-forge", "--values", str(source / "values.yaml")]):
            check = command(binary, args, work)
            if args[0] == "template" and check["exitCode"] == 0:
                # Do not write chart-generated Secret values into evidence/logs.
                rendered = check.pop("stdout")
                check["manifestSha256"] = hashlib.sha256(rendered.encode()).hexdigest()
                check["resourceCount"] = len(re.findall(r"^kind:", rendered, re.MULTILINE))
                version = json.loads((source / "validation_status.json").read_text(encoding="utf-8-sig"))["airflowVersion"]
                check["airflow3ImagePresent"] = version in ("3.2.2", "3.3.1") and "apache/airflow:" + version in rendered
                check["gitSyncPresent"] = "GITSYNC_REPO" in rendered
                if not check["airflow3ImagePresent"] or not check["gitSyncPresent"]:
                    check["exitCode"] = -1
                    check["stderr"] += "Expected Airflow 3 image and git-sync environment missing from rendered manifests."
            result["checks"].append(check)
            if check["exitCode"] != 0:
                result["status"] = "failed"
                break
        return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--iac", choices=["none", "opentofu", "terraform-community", "dual-validate"], default="opentofu")
    parser.add_argument("--tofu", default="tofu")
    parser.add_argument("--terraform", default="terraform")
    parser.add_argument("--helm", default="helm")
    parser.add_argument("--chart", help="Optional already downloaded official chart directory (version is checked).")
    parser.add_argument("--skip-helm", action="store_true")
    parser.add_argument("--require-tools", action="store_true", help="Fail when a selected validator is unavailable.")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    project = args.project.resolve()
    if not project.is_dir():
        parser.error(f"Project directory does not exist: {project}")
    evidence = {"contractVersion": "1.2", "status": "generated-reference",
                "recordedAt": datetime.now(timezone.utc).isoformat(),
                "project": str(project), "iac": args.iac, "cloudApplied": False,
                "runtimeValidation": "not-run", "infrastructure": []}
    for engine, executable in [("opentofu", args.tofu), ("terraform-community", args.terraform)]:
        if args.iac in (engine, "dual-validate"):
            evidence["infrastructure"].append(validate_iac(engine, executable, project / "infra" / "gcp"))
    evidence["helm"] = {"status": "skipped-explicitly"} if args.skip_helm else validate_helm(args.helm, project / "minikube", args.chart)
    all_results = [*evidence["infrastructure"], evidence["helm"]]
    failures = [item for item in all_results if item["status"] == "failed" or
                (args.require_tools and item["status"] == "tool-unavailable")]
    evidence["staticValidation"] = "failed" if failures else (
        "passed" if any(item["status"] == "static-validated" for item in all_results)
        and not any(item["status"] == "tool-unavailable" for item in all_results) else "incomplete")
    output = args.output or project / "validation" / "infrastructure.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"staticValidation": evidence["staticValidation"], "status": evidence["status"],
                      "evidence": str(output), "engines": [{"engine": item["engine"], "status": item["status"]}
                      for item in evidence["infrastructure"]], "helm": evidence["helm"]["status"]}))
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
