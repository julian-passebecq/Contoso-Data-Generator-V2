# Project status — 8 September 2026

**Current delivery planning:** [projectmanagement/STATUS.md](../projectmanagement/STATUS.md). The [leadership baseline review](../projectmanagement/reviews/2026-09-08-baseline.md) adds focused correctness findings and fresh bootstrap checks; this page retains the earlier implementation/audit history.

## Local Studio stabilization implementation

The [next-pass plan](../NEXT_PASS_PLAN.md) has now been implemented locally. The audit below is historical: the rejected-edit mutation and report snapshot defects described there have been fixed in application code.

- Journey application parses all editor fields, applies scenario/profile changes to the same draft as the product, validates it, and commits project/graph together. The durable WPF regression first reproduced the 60-to-1,200-order mutation, then passed malformed analysis/publication/recipe, unsupported ML, pending-edit, repeated-apply, authored-graph and save/reload checks.
- Studio discovers a candidate local interpreter or loads `%LOCALAPPDATA%/ContosoForge/python.txt`, checks actual imports for the planned action, displays the resolved executable and remembers successful validation. Browse/Validate controls allow correction. Node/npm is checked only for a report build. No dependencies or global environment settings are installed or changed by the application.
- Child stdout/stderr are drained concurrently into complete `*.studio.log` files, with a bounded 64,000-character display. Generation runs off the dispatcher; execution shows elapsed activity without percentages. Run and Build Evidence share a busy guard. Changing projects resets results and stops the owned report server. A failed rebuild cannot open a previous successful build as the current result. Results show run identity, pipeline/stage outcomes and full evidence/log paths.
- V1.7 reports use an immutable, explicitly labelled upstream snapshot in both the displayed stage table and linked pipeline evidence. Their own report stage and later publication are outside that snapshot. Final `run_evidence.json` and `bi/build_evidence.json` remain separate authoritative receipts; no success is invented before completion and no circular artifact hash is introduced. KPI presentation follows catalog number formats, including decimal strings. ML rows expose the stored baseline threshold. Shared legacy templates and compatibility fixtures are unchanged.

Verified: all **282 .NET tests**, a zero-warning WPF build, all **five WPF smoke variants**, and **four focused report regressions against newly generated templates**. Both full desktop execution smokes passed: KPI and ML-only generation, strict report builds, loopback report loading, tamper/rebuild rejection, project switching and distinct Bronze-only second runs. A final-binary Bronze smoke separately verifies visible progress. Both built reports were inspected in a browser with no warnings/errors; KPI formatting, measured ML/threshold rows and the linked upstream snapshot all displayed correctly. Independent verification checked all five fresh run identities/stage hashes and both reports' input/output hashes. The new report regression script is also wired into the V1.7 CI workflow; no remote CI or release result is claimed for this uncommitted diff.

Current limitations: the asynchronous desktop regression uses the same run/build/preview methods as the real controls, with a supplied folder path. Native folder-picker interaction and the OS default-browser launcher are not manually verified. User cancellation remains a follow-up; Studio blocks closing while a run/build is active. A deeply nested report directory failed under the installed Node 21 with a missing nested dependency during strict build; keep local run parents short. This pass does not upgrade Node or alter global PATH. First-class run reopening/history and guided configuration forms remain later work.

Evidence: `artifacts/next-pass/repro-failing/failure.txt`, `artifacts/next-pass/dotnet/stabilization.trx`, `artifacts/next-pass/build-final.log`, and the `journey-final`, `legacy-final`, `factory-final`, `polars-final`, and `pandas-final` smoke directories. Fresh desktop run roots are under ignored `out/nk/` and `out/nm/`. The rejected long-path build is retained under `artifacts/next-pass/execution-kpi-final/`.

Full execution receipts: `out/nk/execution-smoke.json`, `out/nm/execution-smoke.json`, `out/nb/execution-smoke.json`; independent artifact verification: `artifacts/next-pass/final-verification.json`. Temporary preview servers were stopped after browser inspection. See the completed-plan section for exact commands and interaction limitations.

## Historical audit

The original actionable handoff was [NEXT_PASS_PLAN.md](../NEXT_PASS_PLAN.md).

The local product is a working prototype with substantial automated coverage. The C# generator, local data transformations, dbt validation and desktop editor have fresh successful checks. There are still reproducible UI/setup problems; green CI does not establish a smooth end-user workflow.

## Overall codebase assessment

**My assessment: a capable local data/analytics prototype whose engine is ahead of its product experience.** The project does not need a rewrite. It needs a focused stabilization pass and a narrower, clearer default workflow before more providers are added. This is an architectural and representative code review, not a line-by-line certification of every module.

| Area | Where it stands | What matters next |
| --- | --- | --- |
| C# generation, contracts and planning | Strong foundation: deterministic generation, shared validation/compiler and compatibility tests | Keep one authoritative contract and preserve legacy fixtures |
| Python execution and dbt | Real local execution, persisted layers, independent reconciliation and matching engine outputs | Improve setup, progress, failure recovery and user-facing diagnostics |
| WPF Studio | Usable planning/editor shell, but configuration remains technical and rejected edits have a real mutation bug | Atomic edits, readable forms, runtime preflight and a complete Run-to-Results experience |
| Evidence/results | KPI and ML reports really build and load | Correct stale status, display useful formatting and surface threshold/model-quality context |
| ML | Training, temporal evaluation and result provenance are implemented | Improve educational usefulness and report interpretation; successful training alone does not mean a useful predictor |
| External platforms | Broad adapter/export surface; some historical cloud proof, but new account-backed paths remain unverified | Finish one provider end to end when it serves a concrete user need |
| Documentation/release state | Substantial documentation, but multiple historical handoffs and stale current-status text obscure the actual state | Make this status page the starting point; keep release history separate |

The separation between C# contracts/planning, Python transformations/ML and dbt business calculations is sensible. The main architectural risk is the growing number of versioned templates, adapters and execution-status representations. The report snapshot bug is a concrete example of status becoming inconsistent across layers. Make ownership of current run status explicit and keep shared versus version-specific behavior clear as the templates evolve.

The immediate product target should be one dependable local journey: open an example, edit ordinary fields, validate setup, run, see progress, inspect meaningful results, and reopen that run later. First-class run history/reopening, guided KPI/ML configuration and actionable failures would help more than another provider integration. Persistent orchestration/retry recovery, scalable processing, physical dependency pruning and additional ML problem types belong to later, separately scoped work.

## Where the code stands

- Audited implementation: `c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296`, branch `codex/v1.7-modular-journeys-external-labs`. The checkout was clean before testing.
- Remote `main` remains `ea7c47a6db64de0e91b69b2259c7d48f744e15cc`: V1.6 and the orchestration work are merged.
- [PR #4: V1.7 modular journeys](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/pull/4) is open and mergeable. All eight workflows passed on the audited commit. This was verified through GitHub during this audit; the older handoff and PR description still say the checks are pending.
- V1.7 extends the C# planner/compiler and Windows WPF Studio. It is not a FastAPI rewrite. It adds goal/stop-stage selection, scoped KPI publication, specific ML/AutoML paths, wrangling recipes and external export adapters.
- The most recent fix prevents ML-only Evidence builds from consuming an empty KPI CSV, restricts feature importance to the selected AutoML model, and preserves honest MotherDuck/Dive export status.

## Fresh local checks

| Check | Result |
| --- | --- |
| Release .NET tests | 282 passed; zero failures or skips |
| WPF Release build | Passed; zero warnings or errors |
| WPF smokes | Legacy planner/editor, V1.5 factory, Polars, pandas and V1.7 journey all passed |
| Real WPF rendering | Reviewed journey Goal, KPI/ML and Run screens; existing smokes also rendered the legacy/factory views |
| Additional WPF rejected-edit probe | **Failed expected behavior:** rejected JSON changed the scenario and order count |
| Report status compared with final run | **Mismatch:** the completed report retained a running BI stage |
| Orchestration evidence unit checks | 43 passed; synthetic custody tests, not a new Airflow deployment |
| Generated free-GCP pipeline runtime checks | 9 passed; offline generated fixture |
| V1.3 runtime evidence checks | 16 passed; offline fixtures |
| Tracked runtime-state guard | 4 passed |
| V1.6 standalone parity checks | 27 passed; 7 completed-run checks explicitly skipped because no V1.6 gate was supplied |
| Factory Python dependency consistency | `pip check` passed |
| Docker Compose configuration | Passed; Docker Linux daemon itself is unavailable |
| Fresh V1.7 execution audit | Resumed and verified all 13 journey receipts, three-engine parity, six stop boundaries, sklearn training, both strict report builds and the Spark-ML export journey |
| V1.7 regression suite | 36 passed; zero failures or skips |
| Real local MLJAR | Baseline, Decision Tree and Linear trained; Baseline won validation log loss. No Kaggle account execution |
| Retained artifact verification | Input identities, stage hashes, Bronze files, 13-table parity, report hashes and returned local AutoML results all verified |

Both the KPI and ML-only reports were served over loopback HTTP and checked in a browser. The five measured KPI rows and the two ML validation/test rows displayed, the linked KPI contract opened, and neither page returned browser warning/error logs. The ML-only page correctly identifies scikit-learn and logistic regression. This verifies the report pages, not a full manual click-through of Studio's folder dialogs and Run button.

## Confirmed problems

### 1. Rejected ML journey edits mutate the project

**Priority: fix before calling the editor reliable.** Starting from a V1.7 KPI journey using the normal 60-order BI profile, select `specific-ml`, enter malformed analysis JSON, and apply. The method rejects the JSON, but the underlying business scenario has already changed to ML and the order count has changed from 60 to 1,200.

Cause: `ApplyJourneySettings` in `ContosoForge.PipelineStudio/JourneyStudio.cs` calls `Session.ApplyScenario` before parsing and validating all journey settings. Only the later `Session.ApplyProduct` operation uses a draft. The scenario and generation changes therefore survive a rejected edit.

Reproduced with real WPF controls in an isolated, ignored harness. Before/after project JSON and the result are retained under `artifacts/audit-20260908/ui-probe/` and `ui-atomicity-probe.log`. No user project file was changed by the probe. Fix by validating the complete scenario/product change on a draft and committing once; add a regression covering malformed JSON and semantically invalid ML settings.

### 2. Studio's default Python cannot run the pipeline on this machine

**Priority: provide a reliable first-run setup.** The Run tab defaults to `python`. Here that resolves to `C:\Users\julia\AppData\Local\Programs\Python\Python313\python.exe`, which lacks DuckDB, dbt, Polars, pandas and sklearn. A real generated run failed at its first stage with `ModuleNotFoundError: No module named 'duckdb'`.

The working factory interpreter is `D:\dotnet\Contoso-Data-Generator-V2\.tools\v15\Scripts\python.exe`. Its dependencies are installed and consistent. Set that full path in Studio's Python executable field. A follow-up should discover/remember a suitable interpreter and validate dependencies before generation begins. Do not install into an arbitrary global Python as a workaround.

### 3. A completed report still says BI is running

**Priority: correct the displayed status.** The fresh final `run_evidence.json` says the pipeline and BI stage succeeded, and `build_evidence.json` says `built`. The rendered report nevertheless displays `bi / running / running`.

Cause: `DatabaseGenerator/Forge/Templates/v17/journey_report.py` copies the evidence and writes `stages.csv` while its own BI stage is still running. The static snapshot is never finalized. The linked `pipeline_evidence.json` is likewise an earlier snapshot. Preserve truthful provenance by explicitly labeling the snapshot timing or introducing a finalization design; do not simply invent a successful result before execution finishes.

## Other friction observed

- Node is `v21.7.1`; dependencies emitted unsupported-engine warnings. CI uses Node 22. The fresh KPI build succeeded despite the warning; this is a setup mismatch, not proof that the build failed.
- Docker Desktop's Linux engine pipe is unavailable. The old `lab.ps1 smoke` route requires Docker to be running; the local DuckDB/Polars/pandas factory does not.
- The PowerShell login profile emitted unrelated broken Conda-import errors. Audit commands used a profile-free shell.
- Studio's KPI/ML JSON editor has very little visible height. Advanced settings still require raw JSON.
- The KPI report uses generic formatting: the order count displays as `1,200.00`, and rates as decimals rather than percentages, despite format metadata in the KPI catalog.
- Successful ML execution does not imply strong model quality. This specific logistic-regression fixture has test ROC-AUC about `0.520`; at the displayed default threshold `0.5`, it predicts no positives and has F1 `0`. The metrics artifact also retains the independently validation-selected threshold, which the V1.7 report does not display.
- `RunPython` collects stdout/stderr until the subprocess exits, so Studio does not show ongoing stage output. It can look inactive during a long dbt/report operation.
- Current WPF smoke coverage exercises planning, editing, saving, compilation and rendering. It does not exercise the complete asynchronous Run → Build Evidence → Open report button sequence.

## What remains outside the verified local scope

Kaggle execution, native MotherDuck/Dive and Hugging Face publication remain account-backed boundaries. Exported packages are not proof of remote execution. No credentials were supplied or cloud resources created during this audit. Spark and Cosmos/Airflow have passing exact-commit CI workflows; this audit does not claim fresh local Spark, Airflow, Kubernetes or hosted-cloud execution. Historical BigQuery/Colab/Minikube evidence retains its original scope.

## Working starting point

From the repository root:

```powershell
dotnet run --project ContosoForge.PipelineStudio --configuration Release -- --project examples/v17-kpi-duckdb.project.json
```

In Studio, Plan the loaded example, open Results → Run, and set Python executable to:

```text
D:\dotnet\Contoso-Data-Generator-V2\.tools\v15\Scripts\python.exe
```

Use Run local with a parent directory for a fresh run, then Build Evidence and Open Evidence report. The example follows the local KPI path. Keep the existing example settings while establishing this baseline; address the rejected-edit bug before relying on scenario-switch validation.

For an independently executed CLI baseline with the same interpreter, use a new output folder:

```powershell
dotnet run --project DatabaseGenerator --configuration Release --no-build -- forge generate --project examples/v17-kpi-duckdb.project.json --output out/my-next-kpi-check
& .tools/v15/Scripts/python.exe out/my-next-kpi-check/pipeline/run_local.py --root out/my-next-kpi-check --run-id check-1
& .tools/v15/Scripts/python.exe out/my-next-kpi-check/factory/build_evidence.py --state out/my-next-kpi-check/.forge/v15/check-1
```

## Recommended next work

1. Fix atomic journey application and cover rejected edits with a WPF regression.
2. Make interpreter selection/dependency preflight reliable, and stream run progress into Studio.
3. Correct report snapshot status and test the completed report's displayed state.
4. Exercise one complete desktop run, including repeat runs, switching projects, a failed run, and report opening. Then review PR #4 for merge.
5. Validate external accounts one provider at a time after the local workflow is dependable.

This audit is an assessment, not a release or a merge. Raw generated data, logs and probes stay in ignored `out/` and `artifacts/` directories.

## Resumed checks and remaining scope

The user authorized resuming the deferred checks. The Spark-ML export journey completed under fresh run ID `audit-resume`, preserving the interrupted run. Real local MLJAR 1.3.2 trained Baseline, Decision Tree and Linear within the bounded job; Baseline won on validation log loss. All 36 V1.7 regression tests passed, including tamper rejection, external-adapter contracts, timeout handling, temporal model selection and wrangling.

The resumed verifier checked all 13 journey receipts against their generated input identities and stage hashes, rechecked Bronze, reproduced the same 13-table local-engine parity, verified both built report hashes, and independently validated the local AutoML result package. Executable source remains at `c6e602c`; only audit documentation differs from that commit. These are resumed audit results, not a clean-checkout release ledger. The production release-capture requirement was not bypassed.

The originally found UI/setup/report defects remain open; this continuation tested behavior without changing application code. Next work should start with focused regressions and fixes for those defects, then a full interactive desktop Run-to-Results flow. Fresh local Spark/Airflow execution and account-backed cloud execution remain separate optional passes. The completed Spark-ML journey proves notebook export, not Spark training or hosted execution. No background audit runner remains active.

Evidence retained locally:

- `artifacts/audit-20260908/test_v17.py.log`: 36 passing regression tests.
- `artifacts/audit-20260908/resumed-verification.json`: verified audit receipts and scope.
- `artifacts/audit-20260908/resume_checks.py` and `finalize_checks.py`: reproduction scripts for the continuation.
- `out/audit-20260908-v17/gate.json`: resumed audit index, explicitly recording documentation-only dirty state.
- `out/audit-20260908-v17/local-automl-results/`: measured MLJAR results, leaderboard and training log.
- The same audit directories retain the earlier .NET results, WPF renders, UI mutation probe, GitHub status snapshots, completed local runs and both built reports.
