# S001 — development report

Date: 2026-09-08. Role: medium developer, as assigned by the user.
State: **READY_FOR_QA** — complete development batch; next owner: light QA. Sprint acceptance remains with the tech lead.
Branch/HEAD: `codex/v1.7-modular-journeys-external-labs`, `c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296` plus preserved dirty stabilization and S001 work.
Intake: [starting hashes](../reviews/S001-start-files.json), `artifacts/s001-dev/start.patch`.
Current source: [final file hashes](../reviews/S001-final-files.json), `artifacts/s001-dev/final.patch`. These inventories include untracked source. No commit, push, merge, publication or next-sprint work occurred.

## Outcome

Passes A/B/C are implemented. Studio separates an immutable owned execution context from the selected run and current applied editor revision. Applying edits clears the selection; historical evidence remains on disk and in the local catalog. V1.7 root/state import reads data only. History shows recorded run/build outcomes, identity/location, verification scope and recovery diagnostics. A reopened `running` receipt is explicitly last-recorded state with live execution unconfirmed.

Preview requires matching run IDs and snapshot identity, root/source/compiled manifest hashes, report contract/input/source hashes, the expected index location and recorded index hash. All paths are confined to the bundle; linked paths and links in the served asset tree are rejected. Other built assets are not hash-verified; receipts remain unsigned. Owned V1.5/V1.6 previews retain their copied pre-BI snapshot semantics, while history import remains V1.7-only. No legacy templates or compatibility fixtures changed.

The version-1 catalog stores locators, labels and timestamps under local application data. An exclusive sibling lock serializes read/merge/write across Studio instances. Temporary bytes are flushed before atomic replacement; replacement retries briefly when an external reader denies deletion. Corrupt catalogs are never overwritten, interrupted temporary files do not replace the index, and duplicate IDs at distinct roots remain separate entries. Failed generated runs are registered before runtime execution and remain diagnosable.

V1.7 records missing optional package metadata as `unavailable`; it does not suppress other metadata errors. Studio checks required imports and selected-engine distribution metadata before generation, including pandas needed by the report CSV helper. The minimal DuckDB/PyArrow environment executed Verify and Bronze successfully without pandas, Polars, dbt or scikit-learn.

## Acceptance coverage

Developer evidence is distinct from independent QA and lead acceptance.

| ID | Developer result | Evidence / limits |
| --- | --- | --- |
| AC01 | PASS | Final 310 .NET tests, including existing compatibility tests; all five original WPF editor variants. Inherited JourneyStudio/StudioSession/report code hashes preserved. |
| AC02 | PASS | 28 receipt/catalog tests include valid receipts, after-success index/receipt/input/source/version/path tampering, snapshot binding and linked assets. Real KPI/ML WPF preview negative tests plus restart smoke. Scope is index + documented source/input hashes, not every rendered asset. |
| AC03 | PASS | Real completed-run edit clears selection; explicit reimport is labeled historical and disables build. Fresh second runs use different identities. Context captures root/run/interpreter/project/graph. |
| AC04 | PASS (local paths) | Baseline metadata regression reproduced; fresh and minimal-environment Verify/Bronze tests 2/2 passed. Missing interpreter/import/required distribution metadata checks and generation-before-preflight exclusion exercised in WPF. Fresh local matrix and its 36 regressions passed. |
| AC05 | PASS | Real early stdout and >100 KB stderr/log retention, concurrent action rejection, startup-close blocking, injected startup failure cleanup, active close/switch cleanup. Execution cancellation intentionally unavailable. |
| AC06 | PASS | Concurrent writers, duplicate/conflicting IDs, restart reads, corrupt/truncated/missing index, orphan temporary write and deterministic external read-lock tests. Atomicity is exercised at the file-service seam, not by power interruption. |
| AC07 | PASS | Read-only import/restart with unchanged protected evidence, missing/partial/moved/version/identity/path cases, recorded failed/running/export-only statuses, scripts never executed during inspection. Native folder picker was not automated. |
| AC08 | PASS | Real KPI/ML-only desktop strict builds and reopen/preview passed; Bronze success and failed child execution retained in history passed. Fresh 13-journey matrix, three-engine 13-table parity, all six stop boundaries, local AutoML and both matrix strict reports passed. |
| AC09 | PASS locally / CI defined | Windows CI now has bounded Bronze/KPI/ML asynchronous desktop jobs, minimal Bronze dependencies, receipt/catalog tests, separate-process restart and retained diagnostics. YAML parsed locally. No remote CI result claimed. |
| AC10 | Development packet COMPLETE; QA/lead pending | Final hashes verified unchanged, command ledgers and registers reconciled. Independent QA and lead review are the next owners; no acceptance or remote CI claim. |

## Changes and logic

- `RunEvidence.cs`: read-only identity/receipt validation, Python-compatible run-directory mapping, safe paths, streaming SHA-256, version-specific owned preview compatibility, and immutable execution context.
- `RunCatalog.cs`: minimal JSON locator schema, duplicate preservation, serialized writes, atomic replacement and bounded transient read-lock handling.
- `HistoryStudio.cs` / `HistorySmoke.cs`: explicit import/list/inspect/locate UI and a separate-process restart/preview test entrypoint.
- `FactoryStudio.cs`, `MainWindow.xaml(.cs)`, `App.xaml.cs`, `StudioRuntime.cs`: selection and operation ownership, dependency preflight, persisted failed-build attempts, isolated smoke preferences, startup/close guards, full-height results, actual asynchronous regressions. Test callbacks are internal and unset during normal operation.
- `Templates/v17/run.py`: optional version discovery catches only `PackageNotFoundError`. Legacy templates remain unchanged.
- `StudioRunTests.cs` (linked production services in the existing test project), `scripts/test_studio_dependencies.py`, Windows workflow and Studio/test documentation.

ADR-001 through ADR-010 remain in force. No competing graph/planner, business calculations, ML thresholds, runtime resume, cancellation, provider execution or artifact-manifest redesign was introduced.

## Findings and repair history

| Finding | Evidence and repair |
| --- | --- |
| F001 / P1 | Fresh baseline reproduction accepted mismatched receipt/index; current service and real WPF tampering regressions reject it before server startup. `preview-baseline-reproduction.log`. |
| F002 / P2 | Same preserved baseline assembly retained a successful result after applied revision changes. New real-run edit regression checks that the visible selection clears. |
| F003 / P1 | `dependencies-baseline-negative.log` reproduces missing unused metadata failing Verify. New template passes fault injection and a genuinely minimal environment. |
| D002 / lifecycle | Separate-process history smoke blocks close during startup and confirms failure/active-close cleanup. |
| D003 / CI coverage | Added Windows runtime matrix and restart gates; remote execution remains unverified. |
| S001-D001 / P2 | Visual review found history squeezed into the 210px editor panel. Results now use the available workspace; a rendered-height assertion and final screenshots pass. |
| S001-D002 / P2 | A full integration run encountered transient Windows file locks. A deterministic held-reader regression reproduced catalog replacement failure before the fix. Bounded retry retains the writer lock and staged bytes; 28 focused tests and the subsequent 310-test full suite pass. Test fixture relocation also retries brief OS contention. Origin of the original external lock was not identified. |

Baseline preview reproduction used the preserved bootstrap Studio DLL SHA-256 `C89FB10E17D5AA27AA9D14F2E83AE59FEC672DCB88D173A9CEA70F651B0A48BA` in an isolated probe with a fresh output directory. It did not restore baseline code over the working tree.

## Validation

Exact command arrays, exit codes, durations and log paths are retained in `artifacts/s001-dev/final/{core,ml,repair,matrix}-commands.json`. `artifacts/s001-dev/integrate.py` is the local orchestration script; permanent gates are the committed test sources and workflow. Initial failures are preserved, including `ui-dotnet-first-failure.log`, `dotnet/s001-lock-failure.trx`, `catalog-lock-negative.log`, and baseline negative reproductions.

Final repaired integration:

- `dotnet build ContosoForge.PipelineStudio --configuration Release`: exit 0, zero warnings/errors (`final/ui-build.log`).
- `dotnet test ContosoDGV2.sln --configuration Release --logger 'trx;LogFileName=s001-final.trx' --results-directory artifacts/s001-dev/final/dotnet`: 310 passed, zero failed/skipped, exit 0. Earlier 307-test integration preceded extra regressions; it is not substituted for this result.
- All five documented WPF editor commands: exit 0, `final/ui-{legacy,factory,polars,pandas,journey}`.
- KPI desktop: `out/s001-kpi-desktop/execution-smoke.json`, first ID `studio-20260908-192847-03dacb`, strict report and negative/repeat checks passed.
- ML-only desktop: `out/s001-final-ml/execution-smoke.json`, first ID `studio-20260908-194308-6507c6`, strict report and negative/repeat checks passed (429.61s).
- Latest Bronze/failure desktop: `out/s001-final2-bronze/execution-smoke.json`, successful ID `studio-20260908-195508-fd16a0`, failed generated ID `studio-20260908-195512-772840`; exit 0 for the harness means both expected outcomes were verified.
- Latest separate-process KPI and ML history smoke: `final/ui-{kpi,ml}-restart/history-smoke.json`, exit 0. WPF renders were visually inspected; results are readable and labeled historical. This does not certify native picker/default-browser interaction.
- `scripts/test_studio_dependencies.py --root out/s001-dependencies -v`: 2 passed in the ordinary runtime and 2 passed using `out/s001-minimal-env/Scripts/python.exe`, installed with only DuckDB 1.4.5 and PyArrow 23.0.1.
- `git diff --check` and Windows workflow YAML parse: exit 0.
- Fresh matrix: `python scripts/run_v17.py --output out/s001-final-matrix --automl-python .tools/v17-automl/Scripts/python.exe --build-evidence`, completed in 1024.43s (exit 0), with 13 journey records, matched 13-table DuckDB/Polars/pandas parity, local MLJAR execution and both strict report builds. Follow-up `test_v17.py`: 36/36; report tests: 4/4; dependency tests: 2/2, all exit 0 with zero skips. See `final/matrix-commands.json` and `out/s001-final-matrix/gate.json`. MotherDuck/Kaggle/Hugging Face outputs retain their export-only labels; the Spark-ML package is not Spark engine execution.

Host: .NET SDK 9.0.101 targeting net8; Python 3.13.1 at `.tools/v15/Scripts/python.exe`; DuckDB 1.4.5, PyArrow 23.0.1, pandas 2.3.3, Polars 1.44.1, dbt-core 1.11.14, scikit-learn 1.7.2. Node 21.7.1/npm 10.5.0 produced successful local strict builds with an engine-range warning; new CI uses Node 22. Local AutoML candidate is `.tools/v17-automl/Scripts/python.exe` (MLJAR 1.3.2), executed successfully in the matrix; its bound metrics are under `out/s001-final-matrix/local-automl-results/`.

Source custody: the early matrix runs use unchanged generator/templates; Studio-only layout/service repairs were made while that independent runtime matrix proceeded. A subsequent WPF build, full .NET suite and affected desktop/restart checks validate the repaired files. `S001-final-files.json` identifies those final bytes; the final recheck passed with zero mismatches (`artifacts/s001-dev/final-source-check.json`). Unaffected inherited stabilization was byte-checked in `artifacts/s001-dev/preserved-baseline.json`.

Native pickers/default browser, current remote CI, account-backed providers, actual Spark engine execution and actual Cosmos/Airflow DagRuns have not been exercised in this development batch. No shared Silver/ML/dbt/orchestration implementation changed; conditional gate applicability must remain explicit in QA/lead review. Clean-checkout release evidence is not claimed from this dirty worktree.

## Next action

**READY FOR LIGHT QA**. All owned execution/preview jobs have ended; process inspection found no remaining process referencing the S001 outputs. No development blocker remains. Native-picker/browser and conditional Spark/Cosmos/provider/remote-CI gaps are explicitly carried to QA and lead review.

Exact next instruction (from `projectmanagement/PROMPTS.md`):

> You are light QA and backlog maintainer for this repository. Read AGENTS.md, projectmanagement/STATUS.md, projectmanagement/WORKFLOW.md, the active sprint, projectmanagement/TESTING.md and projectmanagement/reports/S001-dev.md. Independently validate the final source against every acceptance criterion using fresh outputs and exact revision/diff evidence. Audit straightforward failure handling and provenance; refer ambiguous architectural or business logic to the tech lead. Maintain projectmanagement/BACKLOG.md, projectmanagement/BRANCHES.md and projectmanagement/TEST-REGISTER.md. Do not treat the developer's summary, old generated output, skipped checks or old CI as current proof. Write projectmanagement/reports/S001-qa.md. Route clear production defects to medium development with a reproduction. When the sprint is ready for acceptance, or a lead decision is needed, say CALL TECH LEAD and give a concise review packet. Do not accept the sprint or choose the next sprint yourself.
