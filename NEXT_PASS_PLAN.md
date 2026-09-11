# Next AI pass: stabilize the local Studio workflow

**Historical completed pass.** For new development, follow [projectmanagement/STATUS.md](projectmanagement/STATUS.md) and [Sprint S001](projectmanagement/sprints/S001-trusted-local-runs.md). Keep this document for stabilization acceptance/evidence history.

Status: implemented locally on 8 September 2026. Acceptance checks and explicit interaction/platform limits are recorded below. This is an uncommitted stabilization diff, not a release or remote CI claim.

## Completed pass — results and limits

Phases 1–4 changed application code, with focused regressions. Phase 5 exercised shown WPF windows and the same asynchronous run/build/preview methods used by their buttons, using a folder-selection seam. The final binary additionally passed a Bronze-only execution with an assertion that progress is actually visible. The native folder picker and OS default-browser launch remain unverified interactions: the automated preview uses loopback HTTP and an independently inspected browser. Checkmarks below mean verified by regression, execution or source review, with these explicit limitations; they do not claim every interaction was manually clicked.

- **282 .NET tests passed**, including the 152-artifact legacy compatibility audit. Final WPF build: zero warnings/errors. All five WPF smoke variants passed (journey, legacy, V1.5, Polars and pandas). The journey regression first failed on the original atomic-edit defect and now covers malformed JSON in all three editors, unsupported ML settings, preserved input/pending state, repeated application, an authored graph, and a runnable ML save/reload.
- **Four new report checks passed** against newly generated templates, and are included in the V1.7 CI workflow. They cover snapshot scope/input hashes, tampering, honest failed-build receipts, catalog formatting including decimal strings, and ML-only empty-KPI/threshold handling. Shared legacy templates and expected compatibility hashes were not changed.
- **Both full desktop execution smokes passed**: `out/nk/execution-smoke.json` (KPI) and `out/nm/execution-smoke.json` (ML-only). Each rejects a nonexistent interpreter before generation, checks missing imports and Node/npm, remembers validated Python, verifies early output and complete stderr logs, prevents concurrent report execution/project switching, builds a strict report, loads it over loopback, rejects a tampered rebuild and stale preview, switches projects and completes a distinct Bronze-only run with reporting disabled. The test restores the deliberately tampered page bytes; final report and stage hashes were reverified afterward.
- **The final progress-visibility check passed** in `out/nb/execution-smoke.json`. It reruns the asynchronous preflight/streaming checks and a real Bronze-only run after the final change that selects Measured results and puts elapsed activity in the persistent status bar.
- **Both reports were inspected in a browser**: the KPI page displays 1,325,360.44 gross sales and 11.083300% return rate; ML displays validation/test rows and the stored 0.500 baseline threshold. Eight upstream stage rows show completed outcomes, with no misleading BI/running row. The linked pipeline evidence identifies the same upstream snapshot. Neither page produced browser warnings/errors.
- `artifacts/next-pass/final-verification.json` independently verifies identities and stage hashes for all five fresh runs, both strict build receipts/index hashes, all immutable report-file hashes and copied input hashes. Other evidence is under `artifacts/next-pass/`, including `dotnet/stabilization.trx`, `build-final.log`, `report-tests.log` and the `*-final` smoke directories. All generated data/packages/logs remain ignored.

The report lifecycle is an **immutable upstream snapshot**, excluding its own BI stage and subsequent publication. Final runtime evidence and the separate web-build receipt remain outside the hash-bound report. No final success is fabricated and no circular hash is introduced.

Remaining limits: native folder-picker/default-browser interactions were not manually exercised; shutdown verification covers stopping the owned preview during rebuild/project switching and subsequent close, rather than a separate manual close while preview is active. Cancellation remains an explicit follow-up; closing during execution is blocked. Export-only presentation preserves the recorded outcome strings but no new external execution was added. A long report path failed Node 21 module resolution for an existing 279-character nested package path; both shorter paths containing spaces built successfully. Prefer a short run parent on this host; no Node upgrade or global PATH/Python changes were made. Cloud/provider execution, run history/reopening and guided forms remain outside this pass.

Reproduce the new focused checks from the repository root:

```powershell
dotnet build ContosoForge.PipelineStudio --configuration Release
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v17-kpi-duckdb.project.json --journey-smoke-output artifacts/new-journey-check
# Use short, fresh output paths for real Evidence builds on Windows.
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v17-kpi-duckdb.project.json --execution-smoke-output out/new-kpi
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v17-specific-ml.project.json --execution-smoke-output out/new-ml
# Read firstState in execution-smoke.json to locate the fresh generated root.
python scripts/test_studio_report.py --root <fresh-generated-root> -v
```

The execution smoke uses the Studio interpreter preference/discovery logic. Select and validate a working interpreter first on another machine. Completed work is left reviewable without commit, push or merge. The original plan follows for acceptance traceability.

## Objective

Make one local V1.7 journey dependable from editing through results: open an example, apply settings safely, validate the runtime, execute with visible progress, build a report, and see an accurate outcome. Fix the demonstrated defects before adding platforms or broad new features.

Use the existing C# planner/compiler, Python execution and dbt business calculations. Keep the current WPF visual design. This is a focused stabilization pass, not a rewrite.

## Start here

1. Read `docs/project-status.md`, this plan, and any applicable `AGENTS.md`.
2. Inspect `git status` and the current commit. The audited implementation was `c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296` on `codex/v1.7-modular-journeys-external-labs`. PR #4 was open at audit time; do not assume its remote state is unchanged.
3. Preserve existing work. The audit added/edited `docs/project-status.md`, `README.md`, `HANDOFF.md` and this plan; these may still be uncommitted. Do not reset, stash away or overwrite them to obtain a clean checkout.
4. Inspect the files listed below before editing. Reproduce the atomic-edit defect with one focused test, then implement the phases in order.

The baseline is already strong: 282 .NET tests, five WPF smoke variants, 36 V1.7 Python checks, 13-table DuckDB/Polars/pandas parity, six stop boundaries, both report builds and real local MLJAR execution passed. Repeating the entire release gate is not the starting task.

## Phase 1 — Make journey application atomic

Relevant files:

- `ContosoForge.PipelineStudio/JourneyStudio.cs`: `ApplyJourneySettings`
- `ContosoForge.PipelineStudio/StudioSession.cs`: `ApplyScenario`, `ApplyProduct`, plan/compiled-state invalidation
- `ContosoForge.PipelineStudio/App.xaml.cs`: WPF smoke assertions
- `DatabaseGenerator.Tests/ForgeStudioIntegrationTests.cs` and related shared-contract tests

Reproduction: use a V1.7 KPI journey with the ordinary 60-order BI profile. Select `specific-ml`, enter malformed analysis JSON, and apply. The error is shown, but the applied project has already switched to the ML scenario and 1,200 orders. The optional local probe and before/after JSON are in `artifacts/audit-20260908/ui-probe/`.

Implementation direction: parse the input and validate the complete scenario/product change on a draft. Commit project and pipeline changes together only after all validation succeeds. Merely moving JSON parsing before `ApplyScenario` is insufficient: semantic validation can also fail later. Preserve default-graph regeneration and authored-graph behavior.

Acceptance criteria:

- [x] Malformed analysis, publication or recipe JSON leaves applied project and pipeline unchanged.
- [x] Syntactically valid but unsupported ML settings also leave applied state unchanged.
- [x] Failed application preserves entered text so the user can correct it.
- [x] Pending edits still block Run/Compile; failure does not restore a stale runnable plan.
- [x] Valid BI-to-ML application updates the intended profile once, survives save/reload and produces the expected plan.
- [x] Add a durable regression to the repository's test/smoke coverage, not just the ignored audit probe.

## Phase 2 — Validate runtime setup before execution

Relevant files: `ContosoForge.PipelineStudio/FactoryStudio.cs`, the Run tab in `MainWindow.xaml`, and the existing runtime requirements/launch code.

Observed problem: the field defaults to `python`. On the audited PC that interpreter lacks DuckDB/dbt/Polars/pandas/sklearn and a real run failed immediately. The working environment was `.tools/v15/Scripts/python.exe` under the repository; verify it still exists rather than hard-coding this machine's absolute path into product code.

Implementation direction: allow explicit executable selection, validate it, and remember a successfully validated selection in appropriate local user settings. A discovered environment must be checked before use. Keep local machine preferences out of portable project contracts. Preflight should reflect actual imports and the selected action; Node/npm is required for report building, not every data-only run.

Acceptance criteria:

- [x] A nonexistent interpreter or missing required dependency yields a concise, actionable error before source generation starts.
- [x] The UI identifies the actual executable being used and explains how to install the relevant repository requirements.
- [x] Paths containing spaces work without shell-string construction.
- [x] A valid selected interpreter works after reopening Studio; a stale saved path can be corrected.
- [x] Missing Node/npm is handled before a report build; earlier data stages remain usable.
- [x] Do not silently install dependencies, change global PATH/Python, or upgrade system packages.

## Phase 3 — Make execution progress and controls reliable

Relevant file: `ContosoForge.PipelineStudio/FactoryStudio.cs`, especially `RunPython`, `RunFactory_Click`, `BuildEvidence_Click` and result refresh/open handlers.

`RunPython` currently buffers stdout/stderr until the child exits. Stream available output safely to the UI, retain complete logs, and clearly show the current action and elapsed activity when a child is quiet. Do not invent progress percentages. Introduce only the small execution/state abstraction needed to test this behavior; avoid a wholesale UI framework migration.

Acceptance criteria:

- [x] Output appears before a test child process exits; stdout and stderr cannot deadlock the application.
- [x] The UI remains responsive, with bounded in-memory log display and a route to full logs.
- [x] Run and report-build actions cannot write concurrently to the same run.
- [x] Starting a new run or changing projects cannot expose the previous run's report as the new run's result.
- [x] Success, failure and export-only outcomes are distinguished and attached to the correct run identity.
- [x] Build Evidence is unavailable when the selected stop boundary produced no report package.
- [x] If cancellation is added, terminate only the owned child process tree, retain an honest outcome and verify cleanup. Otherwise make cancellation an explicit follow-up, not an untested promise.

## Phase 4 — Correct report status and improve basic readability

Relevant files:

- `DatabaseGenerator/Forge/Templates/v17/journey_report.py`
- `DatabaseGenerator/Forge/Templates/v17/run.py` and `journey_runtime.py`
- `DatabaseGenerator/Forge/Templates/v15/build_evidence.py` and shared report helpers
- `scripts/test_v17.py`

Reproduction: a completed KPI or ML-only report shows `bi / running / running`, while the final runtime evidence says succeeded and the report build receipt says built. The report copies evidence while its own stage is executing. Its linked `pipeline_evidence.json` is also an earlier snapshot.

Choose and document an honest lifecycle: either clearly label the report as a snapshot of upstream stages and represent its own stage appropriately, or introduce a finalization mechanism with consistent receipts. Do not set success before execution finishes. Consider the circular-hash problem before embedding final evidence inside an artifact that the same evidence hashes. Preserve existing tamper checks and keep generated packages distinct from successful web builds.

Acceptance criteria:

- [x] A successfully built report does not misleadingly present its own BI stage as a currently running operation.
- [x] Snapshot timing/scope and linked evidence are consistent; failed builds cannot claim success.
- [x] Report inputs and output hashes still validate; tampered or stale inputs still fail closed.
- [x] Both KPI and ML-only reports build and display their data; preserve the empty-KPI-file regression fix.
- [x] Use existing KPI catalog formatting for counts/rates/currency without changing KPI calculations.
- [x] Label the displayed ML operating threshold. If exposing the validation-selected alternative, read its stored metrics directly; never reselect on test data or recompute ML logic in the UI.

Changes to shared legacy templates can affect audited artifact bytes. Understand that impact before editing; preserve legacy behavior and compatibility fixtures rather than casually regenerating expected hashes.

## Phase 5 — Verify the complete desktop workflow

Exercise real WPF controls and the asynchronous execution path. Existing hidden-render smokes alone do not establish that the Run/folder-dialog/report-opening flow works. Use a small test seam for folder selection/process launching where helpful, plus an actual local walkthrough when the available tools permit it. State any interaction that could not be verified.

Required scenarios:

1. Open `examples/v17-kpi-duckdb.project.json`, Plan, select a valid Python, Run into a fresh folder, Build Evidence and open the report over loopback HTTP.
2. Reject invalid ML edits, correct them, then successfully apply/save/reload.
3. Run with an invalid interpreter; correct it and retry without corrupting project state.
4. Run a Bronze-only journey: no downstream execution or enabled report-build action.
5. Complete a second run and switch projects: results and report actions remain tied to the correct run.
6. Close Studio after report preview; its owned server/process resources are cleaned up.

## Validation strategy: focused first, broad once when justified

- Add regressions that fail on the demonstrated bugs and test observable behavior. Avoid tests that merely restate implementation.
- Run affected focused tests during development. After implementation settles, run the .NET suite once and the relevant WPF smokes. Include the legacy smoke when shared editor behavior changes.
- For Python/template changes, generate fresh artifacts from the changed implementation. Running tests against old `out/audit-20260908-v17` templates does **not** test the new source.
- Use the smallest fresh KPI and ML-only fixtures needed for report changes; perform one strict build of each final report and inspect the displayed output.
- Broaden to engine parity, AutoML, Spark or orchestration only if changed code affects those paths, a regression appears, or release requirements demand it. Do not rerun every cloud/export lab just because it exists.
- Keep all generated data, npm packages, logs and screenshots under ignored `out/` or `artifacts/` paths with fresh run IDs.

Useful baseline commands (choose new output directories):

```powershell
dotnet test ContosoDGV2.sln --configuration Release
dotnet build ContosoForge.PipelineStudio --configuration Release
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/v17-kpi-duckdb.project.json --journey-smoke-output artifacts/next-pass/journey
dotnet ContosoForge.PipelineStudio/bin/Release/net8.0-windows/ContosoForge.PipelineStudio.dll --project examples/free-gcp-lab.project.json --smoke-output artifacts/next-pass/legacy
```

On the audited machine, use a profile-free PowerShell shell to avoid unrelated Conda startup errors. Node 21 emitted dependency-engine warnings; CI used Node 22. Docker's Linux daemon was unavailable. Recheck only the prerequisites needed by the chosen test; these observations are not permission to reconfigure the whole machine.

## Keep outside this pass

No new cloud providers, FastAPI rewrite, physical pruning, regression-target generation, model-quality tuning, managed deployment or persistent Airflow/Kubernetes rollout. Do not publish to Kaggle/Hugging Face/MotherDuck, push changes, or merge PR #4 as a side effect of this local stabilization task.

After this pass, the highest-value product backlog is: first-class run reopening/history, guided KPI/ML forms with advanced JSON retained, better result interpretation, then one account-backed provider journey at a time. Treat these as later work unless the user explicitly expands the next pass.

## Completion and handoff

- [x] Phases 1–4 are implemented and their acceptance criteria are verified, or a concrete limitation is recorded.
- [x] Phase 5's complete local workflow has evidence, with manual/untested gaps stated explicitly.
- [x] Run `git diff --check`; review scope and ensure no runtime artifacts or machine credentials are tracked.
- [x] Update this checklist and `docs/project-status.md` with what changed, exact checks/results, relevant evidence paths and remaining issues. Link from `HANDOFF.md` without rewriting historical execution claims.
- [x] Report clearly whether application bugs were fixed versus merely retested. Summarize the diff and leave it reviewable; do not claim a release or clean-checkout ledger without meeting the existing requirements.

The audit's optional local evidence is under `artifacts/audit-20260908/` and `out/audit-20260908-v17/`. Those ignored files may be absent in another checkout. This plan and the durable regression tests added during implementation must remain sufficient without them.
