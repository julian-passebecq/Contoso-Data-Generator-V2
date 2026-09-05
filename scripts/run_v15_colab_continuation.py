"""Run a generated Spark work package and its V1.5 continuation locally; no hosted/cloud claim."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import uuid
import zipfile


def run(project, output):
    project, output = project.resolve(), output.resolve()
    if output.exists(): raise ValueError("Choose a fresh output directory")
    subprocess.run([sys.executable, str(project / "colab/work_order.py"), "package", "--root", str(project),
                    "--run-id", "v15-session-" + uuid.uuid4().hex, "--scope", "spark"], check=True)
    output.mkdir(parents=True)
    with zipfile.ZipFile(project / "colab/work_package.zip") as archive:
        for info in archive.infolist():
            if not (output / info.filename).resolve().is_relative_to(output): raise ValueError("Unsafe ZIP member")
        archive.extractall(output)
    steps = [
        ("spark", "colab/run_spark.py", ["--root", ".", "--lake-root", "lake", "--work-order", "colab/work_order.json"]),
        ("result", "colab/work_order.py", ["spark-result", "--root", ".", "--work-order", "colab/work_order.json", "--runtime", "colab/spark_runtime.json", "--output", "colab/spark_result_manifest.json"]),
        ("import", "colab/work_order.py", ["import-evidence", "--root", ".", "--work-order", "colab/work_order.json", "--result", "colab/spark_result_manifest.json", "--output", "spark_evidence.json"]),
        ("factory", "factory/after_spark.py", ["--root", ".", "--lake", "lake", "--work-order", "colab/work_order.json", "--spark-runtime", "colab/spark_runtime.json", "--state", "factory-session"])
    ]
    for name, script, arguments in steps:
        print(name, flush=True)
        with (output / (name + ".log")).open("w", encoding="utf-8") as log:
            subprocess.run([sys.executable, script, *arguments], cwd=output, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=1200)
    runtime = json.loads((output / "colab/spark_runtime.json").read_text())
    evidence = json.loads((output / "factory-session/run_evidence.json").read_text())
    report = {"status": evidence["status"], "actualSparkApiMode": runtime["actualSparkApiMode"], "isRemote": runtime["isRemote"],
              "sparkVersion": runtime["sparkVersion"], "samePackageAndSession": True, "hostedColabExecution": False, "nativeBigQueryExecution": False}
    (output / "continuation_summary.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    run(args.project, args.output)
