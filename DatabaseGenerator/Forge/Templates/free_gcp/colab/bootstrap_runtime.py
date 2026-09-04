#!/usr/bin/env python3
"""Explicit Colab version policy; installation runs only when this CLI is invoked."""
from __future__ import annotations

import argparse
import importlib.metadata
import json
import subprocess
import sys
from pathlib import Path

ALLOWED_NATIVE = {"classic": ("3.5.9", "4.0.4"), "connect-local": ("4.0.4",), "connect-remote": ("4.0.4",)}


def installed_version(name):
    try:
        return importlib.metadata.version(name)
    except importlib.metadata.PackageNotFoundError:
        return None


def plan_install(config, installed=None):
    mode = config.get("sparkApiMode", "classic")
    policy = config.get("sparkVersionPolicy", "colab-native")
    requested = config.get("sparkVersion", "4.0.4")
    if mode not in ALLOWED_NATIVE or policy not in ("colab-native", "pinned"):
        raise ValueError("Unknown Spark mode or version policy")
    if requested not in ALLOWED_NATIVE[mode]:
        raise ValueError(f"Version {requested} is outside the adapter compatibility set for {mode}: {ALLOWED_NATIVE[mode]}")
    if policy == "colab-native" and installed is not None and installed not in ALLOWED_NATIVE[mode]:
        raise ValueError(f"Installed PySpark {installed} is outside the allowed set for {mode}. Select pinned explicitly to replace it.")
    selected = installed if policy == "colab-native" and installed else requested
    packages = []
    if installed != selected:
        packages.append(f"pyspark=={selected}")
    if mode.startswith("connect-"):
        # Extras install missing dependencies while preserving the selected Spark version.
        packages.append(f"pyspark[connect]=={selected}")
    return {"policy": policy, "installedVersion": installed, "requestedVersion": requested,
            "selectedVersion": selected, "changesSparkVersion": installed is not None and installed != selected,
            "packages": packages}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", default="colab/spark_config.json")
    parser.add_argument("--allow-version-change", action="store_true", help="Explicit consent for a pinned replacement of installed PySpark")
    args = parser.parse_args()
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))
    plan = plan_install(config, installed_version("pyspark"))
    print(json.dumps(plan, indent=2), flush=True)
    if plan["changesSparkVersion"] and not args.allow_version_change:
        raise ValueError("Pinned version changes require --allow-version-change. The current runtime was left unchanged.")
    if plan["packages"]:
        subprocess.run([sys.executable, "-m", "pip", "install", *plan["packages"]], check=True)
    # No forced downgrade of a preinstalled Arrow, pandas or BigQuery client.
    missing = [name for name in ("pyarrow", "google-cloud-bigquery") if installed_version(name) is None]
    if missing:
        subprocess.run([sys.executable, "-m", "pip", "install", *missing], check=True)
    subprocess.run(["java", "-version"], check=True)
    print(json.dumps({name: installed_version(name) for name in ("pyspark", "pyarrow", "pandas", "grpcio", "google-cloud-bigquery")}, indent=2))


if __name__ == "__main__":
    main()
