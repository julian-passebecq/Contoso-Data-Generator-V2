"""Session boundary shared by classic and separately verified Spark Connect modes.

Uses public DataFrame/SQL APIs. No classic execution context is accessed here.
"""
from __future__ import annotations

import os
import platform
import subprocess
from pathlib import Path
from urllib.parse import urlsplit

from bootstrap_runtime import ALLOWED_NATIVE, installed_version


def validate_config(config):
    mode = config.get("sparkApiMode", "classic")
    policy = config.get("sparkVersionPolicy", "colab-native")
    requested = config.get("sparkVersion", "4.0.4")
    installed = installed_version("pyspark")
    if mode not in ALLOWED_NATIVE or policy not in ("colab-native", "pinned"):
        raise ValueError("Unsupported Spark API mode or version policy")
    if installed is None:
        raise ValueError("PySpark is missing; run colab/bootstrap_runtime.py first")
    if requested not in ALLOWED_NATIVE[mode]:
        raise ValueError(f"Requested PySpark {requested} is outside the allowed compatibility set for {mode}")
    if installed not in ALLOWED_NATIVE[mode]:
        raise ValueError(f"Installed PySpark {installed} is outside the allowed compatibility set for {mode}")
    if policy == "pinned" and installed != requested:
        raise ValueError(f"Pinned PySpark {requested} requested, but {installed} is installed; run the explicit bootstrap")
    remote = config.get("sparkRemote")
    if mode == "connect-remote":
        endpoint = urlsplit(remote or "")
        if endpoint.scheme != "sc" or not endpoint.hostname or endpoint.username or endpoint.password or endpoint.query or endpoint.fragment or endpoint.path not in ("", "/") or ";" in str(remote):
            raise ValueError("connect-remote requires a credential-free sc://host:port endpoint; configure authentication outside the work package")
    elif remote:
        raise ValueError("sparkRemote is only valid for connect-remote")
    return mode, installed


def runtime_environment():
    try:
        import google.colab
        execution = "google-colab-interactive"
    except ImportError:
        execution = "local-python"
    try:
        java = subprocess.run(["java", "-version"], capture_output=True, text=True, check=True)
        java_version = (java.stderr or java.stdout).strip()
    except (OSError, subprocess.CalledProcessError) as error:
        java_version = "unavailable: " + type(error).__name__
    memory = {}
    proc = Path("/proc/meminfo")
    if proc.exists():
        for line in proc.read_text().splitlines():
            key, value = line.split(":", 1)
            if key in ("MemTotal", "MemAvailable"):
                memory[key] = value.strip()
    elif os.name == "nt":
        import ctypes
        from ctypes import wintypes

        class MemoryStatus(ctypes.Structure):
            _fields_ = [("length", wintypes.DWORD), ("load", wintypes.DWORD),
                        *[(key, ctypes.c_ulonglong) for key in ("totalPhysical", "availablePhysical", "totalPageFile", "availablePageFile", "totalVirtual", "availableVirtual", "availableExtendedVirtual")]]

        status = MemoryStatus()
        status.length = ctypes.sizeof(status)
        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
            memory = {"totalPhysicalBytes": status.totalPhysical, "availablePhysicalBytes": status.availablePhysical}
    return {"pythonVersion": platform.python_version(), "javaVersion": java_version,
            "cpuCount": os.cpu_count(), "memorySummary": memory, "executionRuntime": execution}


def create_session(config):
    mode, installed = validate_config(config)
    # Ambient Connect flags must never turn the explicitly requested classic mode into Connect.
    if mode == "classic":
        if os.environ.get("SPARK_REMOTE") or os.environ.get("SPARK_CONNECT_MODE_ENABLED"):
            raise ValueError("Classic mode conflicts with an active Connect environment; use a fresh subprocess")
        os.environ["SPARK_API_MODE"] = "classic"
    else:
        if os.environ.get("SPARK_REMOTE") and os.environ["SPARK_REMOTE"] != config.get("sparkRemote"):
            raise ValueError("SPARK_REMOTE conflicts with the explicitly selected Spark endpoint")
        os.environ["SPARK_API_MODE"] = "connect"
    from pyspark.sql import SparkSession
    builder = SparkSession.builder.appName("contoso-forge-colab-" + mode)
    endpoint = config.get("sparkRemote") if mode == "connect-remote" else "local[2]"
    builder = builder.master(endpoint) if mode == "classic" else builder.remote(endpoint)
    if mode != "connect-remote":
        builder = builder.config("spark.driver.memory", "2g")
    spark = (builder.config("spark.sql.session.timeZone", "UTC")
             .config("spark.sql.shuffle.partitions", "4").getOrCreate())
    if installed.startswith("4."):
        from pyspark.sql import is_remote
        remote = bool(is_remote())
    else:
        remote = False  # Supported 3.5.9 path is explicitly classic only.
    session_class = type(spark).__module__ + "." + type(spark).__qualname__
    if remote != (mode != "classic") or remote != (".connect." in session_class):
        spark.stop()
        raise RuntimeError(f"Requested {mode}, observed session {session_class}, is_remote={remote}; no fallback is permitted")
    return spark, {**runtime_environment(), "pysparkVersion": installed, "sparkVersion": spark.version,
                   "requestedSparkApiMode": mode, "actualSparkApiMode": mode,
                   "sparkSessionClass": session_class, "isRemote": remote, "masterOrRemote": endpoint,
                   "sparkVersionPolicy": config.get("sparkVersionPolicy", "colab-native"),
                   "requestedSparkVersion": config.get("sparkVersion", "4.0.4"), "fallbackReason": None}


def dataframe_smoke(spark, lake_root):
    from pyspark.sql import functions as F, Window
    total = spark.range(1, 101).select(F.sum("id").alias("total")).first()["total"]
    if total != 5050:
        raise RuntimeError("DataFrame sum probe failed")
    probe = spark.range(8).select((F.col("id") % 4).alias("key"), F.col("id"))
    dedup = probe.dropDuplicates(["key"])
    windowed = probe.withColumn("position", F.row_number().over(Window.partitionBy("key").orderBy("id")))
    if dedup.count() != 4 or windowed.filter("position = 1").count() != 4:
        raise RuntimeError("Window/dedup probe failed")
    destination = str(lake_root / "_dataframe_smoke")
    probe.write.mode("overwrite").parquet(destination)
    restored = spark.read.parquet(destination)
    if restored.count() != 8 or restored.select(F.sum("id").alias("total")).first()["total"] != 28:
        raise RuntimeError("Parquet round-trip probe failed")
    return {"dataframe": True, "window": True, "dedup": True, "parquetRoundTrip": True}
