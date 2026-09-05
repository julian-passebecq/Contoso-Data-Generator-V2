"""Package the existing Gold experiment and optionally execute it with the official Kaggle CLI."""
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import time
from common import read, write, sha, now
from external_contract import binding, hashes, verify, validate_results, repo_id

SPLIT = {"strategy": "chronological-70-15-15", "embargoDays": 14, "labelDelayDays": 14, "testUse": "evaluation-after-selection"}


def package(root, state, analysis):
    folder = state / "kaggle"
    dataset, notebook = folder / "dataset", folder / "notebook"
    dataset.mkdir(parents=True)
    notebook.mkdir()
    for name in ("common.py", "ml_lab.py", "external_contract.py", "automl_worker.py", "notebook_runner.py", "automl-requirements.txt", "requirements.txt"):
        shutil.copyfile(root / "factory" / name, dataset / name)
    for name in ("spec.json", "run_config.json"):
        shutil.copyfile(root / "factory/ml" / name, dataset / name)
    shutil.copyfile(state / "lake/gold/ml_customer_dissatisfaction.parquet", dataset / "features.parquet")
    config = read(dataset / "run_config.json")
    config.update(labelAsOf=read(state / "dbt_execution.json")["labelAsOf"], analysis=analysis)
    write(dataset / "run_config.json", config)
    from ml_lab import load_features, temporal_split, TARGET
    frame, partitions = temporal_split(load_features(dataset / "features.parquet", config), config["labelAsOf"])
    prediction_contract = {split: {str(int(row.order_key)): int(getattr(row, TARGET)) for row in frame[frame.split_name == split].itertuples()}
                           for split in ("validation", "test")}
    dataset_id = repo_id(analysis.get("datasetId") or "your-account/forge-data")
    kernel_id = repo_id(analysis.get("kernelId") or "your-account/forge-experiment")
    write(dataset / "dataset-metadata.json", {"title": "Contoso Forge synthetic experiment", "id": dataset_id, "licenses": [{"name": "CC0-1.0"}], "isPrivate": True})
    manifest = {"contractVersion": "1.7", "status": "exported-not-executed", "identity": binding(root, state),
                "target": analysis["target"], "splitContract": SPLIT, "partitions": partitions, "expectedPredictions": prediction_contract, "files": hashes(dataset)}
    write(dataset / "package_manifest.json", manifest)
    manifest["manifestSha256"] = sha(dataset / "package_manifest.json")
    write(folder / "package_binding.json", manifest)
    write(notebook / "kernel-metadata.json", {"id": kernel_id, "title": "Contoso Forge bounded experiment", "code_file": "experiment.ipynb",
        "language": "python", "kernel_type": "notebook", "is_private": True, "enable_gpu": False, "enable_tpu": False,
        "enable_internet": True, "dataset_sources": [dataset_id], "competition_sources": [], "kernel_sources": []})
    render_notebook(folder, {"datasetId": dataset_id, "kernelId": kernel_id, "datasetVersion": None})
    write(folder / "export_manifest.json", {"status": "exported-not-executed", "identity": manifest["identity"],
                                           "files": hashes(folder, ("export_manifest.json",))})
    return manifest


def render_notebook(folder, remote):
    from notebook_export import notebook
    bound = read(folder / "package_binding.json")
    # Bind a precise input mount and checksum. Never select the first arbitrary Kaggle dataset.
    code = f'''from pathlib import Path
import hashlib, json, subprocess, sys
sys.dont_write_bytecode = True
PACKAGE = Path('/kaggle/input/{remote['datasetId'].split('/')[1]}')
assert hashlib.sha256((PACKAGE / 'package_manifest.json').read_bytes()).hexdigest() == {bound['manifestSha256']!r}, 'Wrong bound dataset package'
sys.path.insert(0, str(PACKAGE))
from external_contract import verify
manifest = json.loads((PACKAGE / 'package_manifest.json').read_text())
verify(PACKAGE, manifest['files'], ('package_manifest.json',))
subprocess.run([sys.executable, '-m', 'pip', 'install', '-r', str(PACKAGE / 'requirements.txt')], check=True, timeout=600)
config = json.loads((PACKAGE / 'run_config.json').read_text())
if config['analysis']['kind'] == 'mljar':
    subprocess.run([sys.executable, '-m', 'pip', 'install', '-r', str(PACKAGE / 'automl-requirements.txt')], check=True, timeout=600)
subprocess.run([sys.executable, '-B', str(PACKAGE / 'notebook_runner.py'), '--package', str(PACKAGE), '--output', '/kaggle/working/results', '--remote', {json.dumps(remote)!r}], check=True, timeout=config['analysis']['totalTimeLimitSeconds'] + 90)
'''
    write(folder / "notebook/experiment.ipynb", notebook("Contoso Forge · bounded external experiment", code))


def authenticated():
    directory = Path(os.environ.get("KAGGLE_CONFIG_DIR", str(Path.home() / ".kaggle")))
    return bool(os.environ.get("KAGGLE_API_TOKEN") or (os.environ.get("KAGGLE_USERNAME") and os.environ.get("KAGGLE_KEY")) or (directory / "kaggle.json").is_file() or (directory / "access_token").is_file())


def cli(args, timeout=120):
    command = [str(Path(sys.executable).parent / ("kaggle.exe" if os.name == "nt" else "kaggle")), *args]
    result = subprocess.run(command, text=True, capture_output=True, timeout=timeout)
    if result.returncode: raise RuntimeError("Kaggle CLI action failed: " + " ".join(args[:2]))
    # stdout remains in memory; write only parsed non-secret status/identity fields to evidence.
    return result.stdout.strip()


def execute_kaggle(root, state, analysis, call=cli, clock=time.monotonic, sleep=time.sleep):
    folder = state / "kaggle"
    bound = read(folder / "package_binding.json")
    verify(folder / "dataset", bound["files"], ("package_manifest.json",))
    if sha(folder / "dataset/package_manifest.json") != bound["manifestSha256"] or bound["identity"] != binding(root, state): raise ValueError("Kaggle package identity changed")
    export = read(folder / "export_manifest.json")
    verify(folder, export["files"], ("export_manifest.json",))
    remote = {"datasetId": analysis.get("datasetId"), "kernelId": analysis.get("kernelId"), "datasetVersion": None}
    result = {"contractVersion": "1.7", "status": "exported-not-executed", "identity": bound["identity"], "packageManifestSha256": bound["manifestSha256"],
              "localPackageHashes": bound["files"], "remote": remote, "startedAt": now()}
    receipt = state / "kaggle_execution.json"
    if not analysis["execute"] or not authenticated():
        result.update(reason="Execution not selected or Kaggle authentication unavailable", completedAt=now())
        write(receipt, result)
        if analysis["requireExecution"]: raise ValueError("Required Kaggle authentication unavailable")
        return result
    repo_id(remote["datasetId"]); repo_id(remote["kernelId"])
    deadline = clock() + analysis["timeoutSeconds"]

    def invoke(args):
        remaining = deadline - clock()
        if remaining <= 0: raise TimeoutError("Kaggle execution timed out")
        return call(args, timeout=min(120, remaining))

    def poll(args, success):
        delay = 2
        while True:
            raw = invoke(args)
            if args[0] == "kernels":
                match = re.fullmatch(re.escape(remote["kernelId"]) + r' has status "(?:KernelWorkerStatus\.)?([A-Za-z]+)"', raw)
                if not match: raise ValueError("Unrecognized Kaggle kernel status receipt")
                response = {"status": match.group(1)}
            else:
                response = json.loads(raw)
            status = str(response["status"]).lower()
            if status in success: return response
            if status in ("error", "failed", "cancelled", "canceled"): raise RuntimeError("Kaggle remote execution failed")
            if status not in ("queued", "running", "pending", "initializing", "creating"): raise ValueError("Unknown Kaggle status")
            remaining = deadline - clock()
            if remaining <= 0: raise TimeoutError("Kaggle execution timed out")
            sleep(min(delay, remaining)); delay = min(30, delay * 2)
    try:
        if analysis["versionDataset"]:
            metadata_folder = folder / "remote-metadata"
            metadata_folder.mkdir()
            invoke(["datasets", "metadata", remote["datasetId"], "-p", str(metadata_folder)])
            metadata = read(metadata_folder / "dataset-metadata.json")
            if metadata.get("isPrivate") is not True: raise ValueError("Versioning requires an existing private Kaggle dataset")
        command = ["datasets", "version" if analysis["versionDataset"] else "create", "-p", str(folder / "dataset"), "--keep-tabular", "--dir-mode", "skip"]
        if analysis["versionDataset"]: command += ["-m", "Forge run " + bound["identity"]["runId"]]
        invoke(command)
        dataset_status = poll(["datasets", "status", remote["datasetId"], "--format", "json"], {"ready"})
        remote["datasetVersion"] = dataset_status["current_version_number"]
        if type(remote["datasetVersion"]) is not int or remote["datasetVersion"] < 1: raise ValueError("No remote dataset version")
        render_notebook(folder, remote)
        result["submittedNotebookHashes"] = hashes(folder / "notebook")
        invoke(["kernels", "push", "-p", str(folder / "notebook"), "--timeout", str(analysis["timeoutSeconds"])])
        result.update(status="submitted", submittedAt=now())
        write(receipt, result)
        status = poll(["kernels", "status", remote["kernelId"]], {"complete"})
        result["remoteStatus"] = status["status"]
        output = folder / "download"
        output.mkdir()
        invoke(["kernels", "output", remote["kernelId"], "-p", str(output)])
        manifests = list(output.rglob("remote_manifest.json"))
        if len(manifests) != 1: raise ValueError("Expected one returned result manifest")
        returned = manifests[0].parent
        validated = validate_results(returned, bound, remote)
        ingest(root, state, returned, bound, remote)
        result.update(status="executed", outputHashes=validated["files"], remoteManifestSha256=sha(manifests[0]),
                      remoteManifestValidated=True, completedAt=now())
    except Exception as error:
        result.update(status="failed", errorType=type(error).__name__, completedAt=now())
        raise RuntimeError("Kaggle execution or output validation failed; inspect kaggle_execution.json") from None
    finally: write(receipt, result)
    return result


def ingest(root, state, output, bound, remote):
    validate_results(output, bound, remote)
    if bound["identity"] != binding(root, state): raise ValueError("Cannot adopt stale ML results")
    destination = state / "ml"
    if destination.exists(): raise ValueError("ML results already exist")
    shutil.copytree(output, destination)


def analysis(root, state, config):
    if config["runtime"] == "kaggle":
        package(root, state, config)
        return execute_kaggle(root, state, config)
    if config["runtime"] != "local":
        from notebook_export import export
        return export(root, state)
    if config["kind"] == "spark-ml":
        from spark_ml import train
        runtime = read(root / "factory/ml/run_config.json")
        runtime["labelAsOf"] = read(state / "dbt_execution.json")["labelAsOf"]
        result = train(state / "lake/gold/ml_customer_dissatisfaction.parquet", runtime, read(root / "factory/ml/spec.json"), state / "ml")
        result["identity"] = binding(root, state)
        write(state / "ml/metrics.json", result)
        return {"status": "executed", "framework": "spark-ml", "metricsSha256": sha(state / "ml/metrics.json")}
    from ml_lab import train
    result = train(root, state)
    metrics = read(state / "ml/metrics.json")
    metrics["identity"] = binding(root, state)
    write(state / "ml/metrics.json", metrics)
    return {**result, "metricsSha256": sha(state / "ml/metrics.json")}
