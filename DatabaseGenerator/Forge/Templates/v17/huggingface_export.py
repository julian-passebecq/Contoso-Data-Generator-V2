"""Measured ML model-card/results package and opt-in model/static-Space publication."""
import html
import os
import re
import shutil
from common import read, write, sha, now
from external_contract import binding, hashes, verify, repo_id


def export(root, state):
    from run import identity, verify_artifacts
    run = read(state / "run_evidence.json")
    if identity(root) != run["identity"]: raise ValueError("Stale source cannot produce a current model card")
    record = run["stages"].get("analysis", {})
    if record.get("status") != "succeeded": raise ValueError("Hugging Face export requires completed analysis")
    verify_artifacts(state, record)
    metrics = read(state / "ml/metrics.json")
    expected = binding(root, state)
    if metrics.get("status") != "executed" or metrics.get("identity") != expected:
        raise ValueError("Model card requires measured ML from this exact run")
    spec = read(root / "factory/ml/spec.json")
    folder = state / "huggingface"
    model, space = folder / "model", folder / "space"
    model.mkdir(parents=True)
    (space / "data").mkdir(parents=True)
    selected = metrics["selectedModel"]
    card = {"forgeVersion": "1.7", "identity": expected, "syntheticData": True, "businessProblem": "Customer dissatisfaction following fulfillment",
        "framework": metrics["framework"], "predictionGrain": spec["predictionGrain"], "predictionTimestamp": spec["predictionTimestamp"],
        "target": spec["target"], "labelDelayDays": spec["minimumLabelDelayDays"], "featureAvailability": spec["featureAvailability"],
        "leakageExclusions": spec["leakageExclusions"], "partitions": metrics["partitions"], "embargoDays": spec["embargoDays"],
        "candidateAlgorithms": list(metrics["models"]), "selectedModel": selected, "selectedBy": metrics["selectedBy"],
        "thresholdProvenance": metrics.get("thresholdAnalysis", {}).get(selected, {}), "finalMetrics": metrics["models"][selected],
        "modelWeightsIncluded": False, "limitations": ["Synthetic educational results do not establish production quality.",
            "Missing adverse outcomes are not proof of satisfaction.", "No executable pickle/joblib or live serving dependency is published. Native training reports remain in the run; safe portable weights were not produced by this adapter."]}
    write(model / "model_card.json", card)
    write(model / "metrics.json", metrics)
    selected_path = state / "ml/selected_model.json"
    write(model / "selected_model.json", read(selected_path) if selected_path.exists() else {"name": selected, "identity": expected, "selectedBy": metrics["selectedBy"], "selectedBeforeTest": True})
    import pandas as pd
    leaderboard = state / "ml/leaderboard.csv"
    if leaderboard.exists():
        shutil.copyfile(leaderboard, model / "leaderboard.csv")
    else:
        pd.DataFrame([{"name": k, **{m: v["validation"][m] for m in ("average_precision", "roc_auc", "f1")}} for k, v in metrics["models"].items()]).to_csv(model / "leaderboard.csv", index=False)
    predictions = pd.read_parquet(state / "ml/predictions.parquet")
    predictions.head(100).to_parquet(model / "sample_predictions.parquet", index=False)
    importance = state / "ml/feature_importance.csv"
    if importance.exists(): shutil.copyfile(importance, model / "feature_importance.csv")
    elif (state / "ml/feature_importance.parquet").exists():
        pd.read_parquet(state / "ml/feature_importance.parquet").to_csv(model / "feature_importance.csv", index=False)
    else:
        (model / "feature_importance.csv").write_text("feature,importance\n", encoding="utf-8")
        card["featureImportanceStatus"] = "not-produced-by-training-runtime"
        write(model / "model_card.json", card)
    readme = "---\nlanguage: en\nlicense: mit\ntags:\n- tabular-classification\n- synthetic-data\n- model-results\n---\n\n# Contoso Forge · Customer dissatisfaction\n\n"
    readme += "This package describes an actual measured experiment on synthetic data. It contains results and model metadata. Safe portable weights were not produced; executable serialized objects are excluded.\n\n"
    for label, value in card.items():
        import json
        readme += "## " + label + "\n\n```json\n" + json.dumps(value, indent=2, default=str) + "\n```\n\n"
    (model / "README.md").write_text(readme, encoding="utf-8")
    for name, value in (("metrics.json", metrics), ("run_summary.json", card), ("leaderboard.json", pd.read_csv(model / "leaderboard.csv").fillna("").to_dict("records")),
                        ("feature_importance.json", pd.read_csv(model / "feature_importance.csv").fillna("").to_dict("records"))):
        write(space / "data" / name, value)
    (space / "README.md").write_text("---\ntitle: Contoso Forge ML Results\nemoji: 📊\ncolorFrom: blue\ncolorTo: green\nsdk: static\napp_file: index.html\npinned: false\n---\n\nStatic measured results; no model compute or live inference.\n", encoding="utf-8")
    rows = "".join("<tr><th>" + html.escape(k.replace("_", " ")) + "</th><td>" + html.escape(str(v)) + "</td></tr>" for k, v in card["finalMetrics"]["test"].items() if isinstance(v, (float, int)))
    document = f'''<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Contoso Forge ML Results</title>
<style>body{{font:18px system-ui;margin:0;background:#eff4f6;color:#123347}}main{{max-width:900px;margin:60px auto;padding:32px}}h1{{font-size:42px}}table{{width:100%;background:white;border-collapse:collapse}}th,td{{padding:14px;text-align:left;border-bottom:1px solid #ddd}}code{{overflow-wrap:anywhere}}a{{color:#076a79}}</style></head><body><main>
<p>CONTOSO FORGE · MEASURED SYNTHETIC EXPERIMENT</p><h1>Customer dissatisfaction</h1><p>Origin: <strong>{html.escape(metrics['framework'])}</strong> · selected model: <strong>{html.escape(selected)}</strong></p>
<p>{html.escape(metrics['selectedBy'])}. Test results are descriptive and did not select the model or threshold.</p><h2>Held-out test results</h2><table>{rows}</table>
<h2>Run identity</h2><p><code>{html.escape(expected['runId'])}</code></p><p>Dataset <code>{expected['datasetFingerprint']}</code></p>
<p>Synthetic educational data. These results do not establish production quality or individual customer behavior. Static results only; no live inference.</p>
<p><a href="data/run_summary.json">Model card and split provenance</a> · <a href="data/metrics.json">Full metrics</a> · <a href="data/leaderboard.json">Leaderboard</a> · <a href="data/feature_importance.json">Feature importance</a></p></main></body></html>'''
    (space / "index.html").write_text(document, encoding="utf-8")
    manifest = {"contractVersion": "1.7", "status": "exported-not-published", "identity": expected, "inputMetricsSha256": sha(state / "ml/metrics.json"), "files": hashes(folder)}
    write(folder / "package_manifest.json", manifest)
    return manifest


def publish(root, state, target, api=None):
    folder = state / "huggingface"
    package = read(folder / "package_manifest.json")
    verify(folder, package["files"], ("package_manifest.json",))
    if package["identity"] != binding(root, state) or package["inputMetricsSha256"] != sha(state / "ml/metrics.json"):
        raise ValueError("Cannot publish stale or changed ML results")
    result = {"status": "exported-not-published", "identity": package["identity"], "packageHashes": package["files"],
        "private": not target["public"], "startedAt": now(), "repositories": []}
    token = os.environ.get("HF_TOKEN")
    if api is None and target["execute"]:
        try:
            from huggingface_hub import HfApi, get_token
            token = token or get_token()
            if token: api = HfApi(token=token)
        except ImportError:
            if token: raise RuntimeError("Install factory/external-requirements.txt for authenticated publication") from None
    try:
        if not target["execute"] or api is None:
            result["reason"] = "Publication not selected or Hugging Face write authentication unavailable"
            if target["requireExecution"]: raise ValueError("Required Hugging Face authentication unavailable")
            return result
        for kind, key, directory in (("model", "modelRepoId", "model"), ("space", "spaceRepoId", "space")):
            if not target.get(key): continue
            repo = repo_id(target[key])
            kwargs = {"repo_id": repo, "repo_type": kind, "private": not target["public"], "exist_ok": True}
            if kind == "space": kwargs["space_sdk"] = "static"
            api.create_repo(**kwargs)
            # Existing public repos must not accidentally receive a private-intent package.
            info = api.repo_info(repo_id=repo, repo_type=kind)
            if bool(info.private) != (not target["public"]): raise ValueError("Existing repository visibility differs from explicit intent")
            verify(folder, package["files"], ("package_manifest.json",))
            commit = api.upload_folder(repo_id=repo, repo_type=kind, folder_path=str(folder / directory), commit_message="Contoso Forge run " + package["identity"]["runId"])
            revision = getattr(commit, "oid", None)
            if not isinstance(revision, str) or not re.fullmatch(r"[0-9a-f]{40,64}", revision): raise ValueError("Publication returned no verified commit revision")
            result["repositories"].append({"repoId": repo, "repoType": kind, "revision": revision})
        if not result["repositories"]: raise ValueError("No explicit Hugging Face repository selected")
        verify(folder, package["files"], ("package_manifest.json",))
        result.update(status="published", completedAt=now())
    except Exception as error:
        result.update(status="failed", errorType=type(error).__name__, completedAt=now())
        raise RuntimeError("Hugging Face publication failed; inspect sanitized receipt") from None
    finally:
        result.setdefault("completedAt", now())
        write(state / "huggingface_execution.json", result)
    return result


def export_publish(root, state, target):
    if not (state / "ml/metrics.json").exists():
        result = {"status": "exported-not-published", "reason": "No validated ML results returned; a measured model card cannot yet be produced", "packageGenerated": False}
        write(state / "huggingface_execution.json", result)
        if target["requireExecution"]: raise ValueError("Required publication has no validated ML result")
        return result
    export(root, state)
    return publish(root, state, target)
