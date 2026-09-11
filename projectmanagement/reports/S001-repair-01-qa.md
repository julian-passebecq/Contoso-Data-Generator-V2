# S001 repair 01 — independent light QA

Validation date: 2026-09-08; final handoff: 2026-09-09 (Europe/Zurich). Role: light QA, assigned by user.
State: **READY_FOR_LEAD — CALL TECH LEAD**. Both repairs are ready for acceptance review; QA has not accepted S001.
Branch/HEAD: `codex/v1.7-modular-journeys-external-labs`, `c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296`, plus preserved dirty Studio/S001/repair work.

Custody: [QA intake](../reviews/S001-repair-01-qa-start-files.json), [QA final](../reviews/S001-repair-01-qa-final-files.json), `artifacts/s001-repair-01-qa/{start,final}.patch` and `final-source-check.json`. Product source stayed unchanged throughout independent QA. Original S001 development, QA, lead and repair development packets remain intact. QA changed management records only; temporary probes and fresh outputs are ignored. No commit, push, publication, merge, acceptance or S002 work occurred.

## Outcome and logic review

No remaining defect was reproduced in either returned case. Recommend the lead close S001-L001 and S001-L002 after reviewing the fixes and evidence below.

L002 now validates the candidate canonical root/state/run ID before assigning selection or stopping a valid preview. The UI guard explicitly catches InvalidDataException and shows a visible diagnostic, preserving the existing expected-exception filter. Invalid locators are not rewritten. Per-selection semantic validation leaves corrupt-catalog recovery available. Read RunCatalog/RunEvidence and the changed handler path; locking and receipt verification bytes are unchanged.

L001's resolver combines compiled operations with independent Silver engine and analysis kind/runtime. Included local Spark ML adds PySpark independently of Silver. Colab export or an earlier stop with non-Spark Silver does not add it. Required engine metadata and shared dispatch/analysis/export imports remain checked. Compared this selection with the actual local/export dispatch in external_labs.py and exercised the actual WPF preflight, rather than relying only on resolver assertions.

## Repair matrix

All rows are fresh repaired-desktop QA unless explicitly marked reused. Evidence paths in this section are relative to `artifacts/s001-repair-01-qa` or the named fresh output root.

| Test / acceptance | Result | Independent evidence |
| --- | --- | --- |
| R-T01 / L002 / AC06–07 | PASSED | Actual Inspect-selected handler with whitespace and 251-character IDs: diagnostic, previous selection/location/live preview preserved, catalog bytes unchanged. Additional independent handler call with no prior selection returned normally with visible error |
| R-T02 / L002 / AC05–07 | PASSED | Actual NUL path and junction rejection, corrupt-JSON recovery, explicit import, subsequent valid inspect/refresh and recovered real run. Repair smoke passed; 750 imported bundle evidence files unchanged across restart/repair |
| R-T03 / L001 / AC04 | PASSED | Real interpreter independently confirmed without PySpark. Actual RunFactoryAsync rejects DuckDB/local Spark ML before generated parent/catalog entry; project/pipeline and pending text preserved. Independent actual preflight also rejects included local Spark ML with Polars/pandas |
| R-T04 / L001 / AC04 | PASSED | Actual Colab and local pre-analysis Bronze preflight pass for DuckDB/Polars/pandas without PySpark. No generation/training/provider call in this independent matrix |
| R-T05 / L001 / AC04 | PASSED | Spark Silver requires PySpark in every tested runtime/stop combination. Two resolver tests cover 128 combinations and shared/legacy/report prerequisites. Fresh minimal DuckDB/PyArrow Bronze success/failure plus 2/2 optional-metadata regressions passed |
| R-T06 / AC01–03/05–09 | PASSED | Fresh 312 .NET tests, five editors, minimal Bronze success/failure, KPI strict build/preview/restart, tamper/failed-build rejection, actual repair/recovery smoke, eight concurrent processes retaining 32 entries |

## Fresh validation and commands

Exact commands, exit codes and durations: `artifacts/s001-repair-01-qa/commands.json`. Repeatable driver: `run.py`. New output root: `out/qa-s001-r1`; no original QA/developer generated bundle was used for the repaired integration flows.

- `dotnet build ContosoForge.PipelineStudio --configuration Release`: exit 0, zero warnings/errors.
- `dotnet test ContosoDGV2.sln --configuration Release --logger 'trx;LogFileName=repair-qa.trx' --results-directory artifacts/s001-repair-01-qa/tests`: exit 0; 312 passed, zero failed/skipped. Independent TRX parsing confirms 28 receipt/catalog tests and two resolver tests (128 combinations inside the matrix test): `test-counts.json`.
- Five original editor commands, all exit 0: legacy, factory V1.5, Polars, pandas and journey; fresh reports/renders under `out/qa-s001-r1`.
- Minimal Bronze desktop: exit 0 (10.74s), successful ID `studio-20260908-215115-a27612`, expected failed ID `studio-20260908-215120-cff7b7`; failed evidence retained. Isolated smoke preference points to the verified minimal interpreter.
- Minimal `scripts/test_studio_dependencies.py --root <fresh Bronze root> -v`: exit 0, 2 passed, zero skips (1.91s). Exact root/arguments in ledger.
- KPI desktop: exit 0 (349.00s), first ID `studio-20260908-215131-142604`, subsequent Bronze ID `studio-20260908-215706-da50d8`. Real strict build, loopback, index tampering, failed rebuild, edit/reopen and cleanup assertions passed.
- Separate-process history smoke against that fresh firstState: exit 0 (5.39s), `out/qa-s001-r1/restart/history-smoke.json`. Its rendered history.png was visually inspected: readable historical identity, succeeded/built outcomes and accurately limited verification text.
- Actual `--repair-smoke-output out/qa-s001-r1/repair` against the same fresh report: exit 0 (22.45s). Valid-preview preservation, malformed catalog bytes, corrupt JSON, missing PySpark, pending drafts, Colab/excluded analysis and recovered new run passed. Exact command in ledger; result `repair-smoke.json`.
- `.tools/v15/Scripts/python.exe artifacts/s001-repair-01-qa/run.py` independently hashes the imported bundle before/after restart and repair: all 750 evidence files unchanged. Excludes node_modules, .evidence, .svelte-kit and __pycache__, not served build assets. `bundle-preservation.json`.
- `dotnet run --project artifacts/s001-repair-01-qa/probe --configuration Release`: exit 0; QA-owned standalone WPF probe covers 16 real compiled-project/preflight combinations (four engines × local/Colab × Bronze/analysis stop) and the original invalid-locator handler case with no selection. `independent-preflight.log`; source retained under `probe/`. Uses actual Studio methods with isolated user-data and real absent PySpark; does not install it or substitute a mock importer.
- `.tools/v15/Scripts/python.exe artifacts/s001-repair-01-qa/concurrency.py`: exit 0; eight separate processes using the current catalog assembly retain 32 entries at 32 distinct roots. Exact process commands/exits in `concurrency.json`.
- `.tools/v15/Scripts/python.exe artifacts/s001-repair-01-qa/check_workflow.py`: exit 0; YAML parse, bounded KPI repair smoke, expanded StudioRun test filter and always-uploaded diagnostics. An initial inline QA check had a shell-quoting SyntaxError; the file-backed check corrected the harness only. Both workflow.log and workflow-final.log retained. This was not a workflow/product failure.
- Final diff check, source/report custody and owned-process inventory: `diff-check.log`, `final-source-check.json`, `final-processes.json`. No QA-owned jobs remain.

Environment: Python 3.13.1 at `.tools/v15/Scripts/python.exe`, PySpark absent; DuckDB 1.4.5, PyArrow 23.0.1, pandas 2.3.3, Polars 1.44.1, scikit-learn 1.7.2 and dbt-core 1.11.14 (`environment.json`). Minimal interpreter `out/s001-minimal-env/Scripts/python.exe` has only pip, DuckDB and PyArrow (`minimal-packages.json`). No global installs or real user preference changes. Existing .NET SDK 9.0.101/net8 and local Node 21.7.1 retained; Node warning is not a support claim, and CI specifies Node 22.

## Reused coverage and limits

QA independently hashed all 245 generator/runtime/compatibility-fixture files recorded at repair intake: zero mismatches (`runtime-custody.json`). RunCatalog and RunEvidence remain unchanged as well. The repair is confined to prerequisite selection, locator/UI handling and supporting tests/workflow/docs. Under the lead's repair-matrix instruction, the original [S001 QA](S001-qa.md) 13-journey/three-engine/six-stop/local AutoML/ML strict-report evidence and 36 V1.7/four report regressions are **reused coverage for unchanged runtime bytes**, not a fresh whole-source run on the repaired desktop. Their original logs/results were preserved.

The lead already allows native picker/default-browser and exact-revision remote CI to remain NOT_RUN for local acceptance, with release checks retained. Actual Spark training/engine, Cosmos/Airflow and providers remain NOT_RUN under the unchanged-runtime scope; the required local Spark prerequisite rejection was freshly tested. Legacy strict reports were not rerun because shared templates are unchanged. No scope exception is silently converted into a test pass. D006 Node/path and D007 rendered-asset hash limitations remain.

## Next action

**CALL TECH LEAD.** Review the repaired source and this independent packet; close or return S001-L001/P2 and S001-L002/P1. Both repairs now have passing actual-boundary and affected-integration evidence. Retain the existing lead-approved coverage limits, and decide S001 acceptance explicitly. QA has not accepted S001, started S002 or authorized release/merge.

