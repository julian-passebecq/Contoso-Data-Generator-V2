"""Measured Gold -> small, reproducible notebook packages. Export is never training."""
import argparse
from pathlib import Path
import shutil
import zipfile
from common import read, write, sha


def notebook(title, code):
    return {"nbformat": 4, "nbformat_minor": 5, "metadata": {"kernelspec": {"display_name": "Python 3", "language": "python", "name": "python3"}},
            "cells": [{"cell_type": "markdown", "metadata": {}, "source": ["# " + title + "\n", "Generated experiment; no execution claimed. Use this package's verified Gold features. Keep sklearn in the same Colab session when the materialization budget permits.\n"]},
                      {"cell_type": "code", "execution_count": None, "outputs": [], "metadata": {}, "source": code.splitlines(keepends=True)}]}


def export(root, state):
    destination = state / "exports/ml-package"
    destination.mkdir(parents=True, exist_ok=True)
    config = read(root / "factory/ml/run_config.json")
    config["labelAsOf"] = read(state / "dbt_execution.json")["labelAsOf"]
    config["trainingStatus"] = "not-executed"
    for file in ("ml_lab.py", "spark_ml.py", "common.py", "requirements.txt"):
        shutil.copyfile(root / "factory" / file, destination / file)
    shutil.copyfile(root / "factory/ml/spec.json", destination / "spec.json")
    shutil.copyfile(state / "lake/gold/ml_customer_dissatisfaction.parquet", destination / "features.parquet")
    write(destination / "run_config.json", config)
    manifest = {"status": "exported-not-executed", "datasetFingerprint": read(root / "truth_manifest.json")["datasetFingerprint"],
                "files": {p.name: sha(p) for p in destination.iterdir() if p.is_file() and p.name != "package_manifest.json"}}
    write(destination / "package_manifest.json", manifest)
    prefix = """from pathlib import Path
import subprocess, sys, json, hashlib
# In the current Colab session, point PACKAGE at this run's exports/ml-package.
# For a separate session, upload/extract ml-package.zip first. Kaggle users attach it as a dataset.
PACKAGE = Path('ml-package')
if not PACKAGE.exists() and Path('/kaggle/input').exists():
    PACKAGE = next(Path('/kaggle/input').rglob('package_manifest.json')).parent
manifest = json.loads((PACKAGE / 'package_manifest.json').read_text())
for name, digest in manifest['files'].items():
    candidate = (PACKAGE / name).resolve()
    assert candidate.parent == PACKAGE.resolve(), 'Unsafe package path'
    assert hashlib.sha256(candidate.read_bytes()).hexdigest() == digest, 'Package checksum mismatch'
subprocess.run([sys.executable, '-m', 'pip', 'install', '-r', str(PACKAGE / 'requirements.txt')], check=True)
"""
    sklearn_code = prefix + "subprocess.run([sys.executable, str(PACKAGE / 'ml_lab.py'), '--features', str(PACKAGE / 'features.parquet'), '--spec', str(PACKAGE / 'spec.json'), '--config', str(PACKAGE / 'run_config.json'), '--output', 'ml-results'], check=True)\nprint(Path('ml-results/metrics.json').read_text())\n"
    spark_code = prefix + "subprocess.run([sys.executable, '-m', 'pip', 'install', 'pyspark==4.0.4'], check=True)\nsubprocess.run([sys.executable, str(PACKAGE / 'spark_ml.py'), '--features', str(PACKAGE / 'features.parquet'), '--spec', str(PACKAGE / 'spec.json'), '--config', str(PACKAGE / 'run_config.json'), '--output', 'spark-ml-results'], check=True)\n"
    for target in ("colab-sklearn", "kaggle-sklearn", "databricks-export"):
        write(state / "exports" / (target + ".ipynb"), notebook("Contoso Forge · " + target, sklearn_code))
    write(state / "exports/colab-spark-ml.ipynb", notebook("Contoso Forge · Spark ML comparison (classic Spark)", spark_code))
    with zipfile.ZipFile(state / "exports/ml-package.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(destination.iterdir()): archive.write(path, "ml-package/" + path.name)
    if (root / "bqml").exists():
        shutil.copytree(root / "bqml", state / "exports/bqml", dirs_exist_ok=True, ignore=shutil.ignore_patterns("__pycache__", "forge_*"))
    (state / "exports/BQML.md").write_text("# BQML export\n\nUse the preserved free-gcp BQML compiler with a native reconciled BigQuery Gold run. Local Parquet training is not native BQML proof. CREATE MODEL requires explicit --execute --allow-training-cost in a billing-capable environment.\n", encoding="utf-8")
    return {"status": "exported-not-executed", "selectedTarget": config["target"], "package": "exports/ml-package.zip", "packageSha256": sha(state / "exports/ml-package.zip")}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--state", type=Path, required=True)
    args = parser.parse_args()
    print(export(args.root.resolve(), args.state.resolve()))
