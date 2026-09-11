# S001 repair 01 — development

Date: 2026-09-08. Role: medium developer, assigned by user.
State: READY_FOR_QA. Next owner: light QA. Both returned findings are repaired in development; lead closure remains pending.
Branch: `codex/v1.7-modular-journeys-external-labs`.
HEAD: `c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296`, plus preserved dirty Studio/S001 work.
Custody: `reviews/S001-repair-01-{start,final}-files.json`; full intake, raw patch and repair-only file list in `artifacts/s001-repair-01-dev/`.

## Changes and logic

- **S001-L002 / P1:** `HistoryStudio.SelectRun` canonicalizes the candidate root and validates its run ID/state path before resetting results, assigning selection or stopping an existing preview. Catalog shape errors remain recoverable on load; locator semantics are validated per selection. Invalid locators are not rewritten or converted into replacement IDs. `MainWindow.Guard` now catches `InvalidDataException` explicitly and displays the diagnostic in the visible status line, as well as Validation. Its exception filter remains limited to expected errors. Catalog locking/replacement and all evidence/receipt verification are unchanged.
- **S001-L001 / P2:** `RuntimeRequirements` resolves compiled operations, selected engine, analysis kind and analysis runtime independently. Included local Spark ML adds `pyspark` even with DuckDB Silver; Colab/Kaggle/Databricks runtime and stop-before-analysis do not add it. Spark engine imports and mandatory engine distribution metadata remain required. Dispatch DuckDB/Arrow, dbt, shared report pandas and analysis/export imports remain checked. Actual Studio preflight uses this resolver and names the selected engine/local Spark action in setup errors. No provider calls or runtime installer were added.
- `StudioRuntimeRequirementsTests` covers 128 engine × analysis-kind × runtime × inclusion combinations, plus legacy/shared export/report imports. `RepairSmoke` exercises actual WPF handlers and preflight, existing-preview preservation, byte preservation, corrupt-catalog import, refresh, draft retention and a recovered real Bronze run. The normal Windows KPI gate runs it; the Bronze unit-test filter includes the resolver tests. README documents the recovery policy and smoke command.

## Regression history and coverage

`dotnet run --project artifacts/s001-lead/probe/Probe.csproj --configuration Release` reproduced both lead defects before edits: actual local Spark preflight passed without PySpark, and actual Inspect-selected leaked InvalidDataException. `before.txt` retains the output.

| Repair test | Development evidence | Scope |
| --- | --- | --- |
| R-T01 / AC06–07 | Actual Inspect handler rejects whitespace and 251-character IDs, visible diagnostic, previous selection/location/preview and catalog bytes preserved | Permanent WPF repair smoke |
| R-T02 / AC06–07 | Invalid NUL path, directory junction, corrupt catalog JSON, explicit import, valid inspect/refresh and new run recover; evidence unchanged | Permanent WPF repair smoke |
| R-T03 / AC04 | Real `.tools/v15` interpreter lacks PySpark; actual RunFactoryAsync rejects included local Spark ML before parent output or catalog registration, preserving project/pipeline. Direct actual preflight also preserves pending editor text | No Spark training needed |
| R-T04 / AC04 | Actual Colab and local stop-at-Bronze preflights pass without PySpark | Other imports still checked |
| R-T05 / AC04 | Resolver matrix requires PySpark for Spark engine; ordinary analysis/export imports retained; fresh minimal Bronze and 2/2 missing-optional-metadata tests pass | Minimal environment contains DuckDB/PyArrow only |
| R-T06 / AC02–09 | 312 .NET tests, five editor smokes, fresh minimal Bronze success/failure, fresh KPI strict build/preview/restart and 8 processes retaining 32 catalog entries passed | Receipt validation unchanged |

## Validation and custody

Exact command arrays, exit codes and timings are in `artifacts/s001-repair-01-dev/{checks,commands,final-commands}.jsonl`; repeatable local drivers are `checks.py`, `integration.py` and `final-integration.py`. Focused tests: `dotnet test DatabaseGenerator.Tests --configuration Release --filter FullyQualifiedName~StudioRun --logger trx --results-directory artifacts/s001-repair-01-dev/focused` passed 30/30. Full suite: `dotnet test ContosoDGV2.sln --configuration Release --logger trx --results-directory artifacts/s001-repair-01-dev/full-tests` passed 312/312, zero skips. WPF Release builds have zero warnings/errors.

Five fresh editor variants are under `out/s001-r1/{editor,v15,polars,pandas,journey}`. Fresh minimal Bronze is `out/s001-r1/bronze`; its isolated preference selects `out/s001-minimal-env/Scripts/python.exe`. `checks.jsonl` records the exact generated root for `scripts/test_studio_dependencies.py --root <root> -v` (2/2). The multi-process test reuses the independent QA writer harness, whose production RunCatalog bytes are unchanged, with a fresh repair catalog; all commands/exits and 32 retained entries are in `multiprocess.json`.

The first new WPF smoke fixture writer used reflection serialization, disabled by this application, and failed before locator testing. This was a harness error; explicit JsonObject construction fixed it. Both failure output (`out/s001-r1/probe/failure.txt`) and the successful corrected probe (`out/s001-r1/probe2/repair-smoke.json`) are retained. The probe used the previous QA report for focused development only; fresh integration is reported separately. Production fixes were stable during the focused/full/editor/runtime tests; subsequent edits were confined to the smoke harness, resolver test data (using actual contract kinds and adding Databricks export), workflow and documentation. A separate output build (`dotnet build ContosoForge.PipelineStudio --configuration Release --output artifacts/s001-repair-01-dev/bin`) avoids replacing assemblies held by the running integration process.

Environment: Python 3.13.1, DuckDB 1.4.5, PyArrow 23.0.1, pandas 2.3.3, sklearn 1.7.2, dbt-core 1.11.14; PySpark absent (`environment.json`). .NET SDK 9.0.101 targets net8. Local Node 21.7.1 retains the known engine-range warning; CI selects Node 22. No global packages or actual user preferences changed.

All `DatabaseGenerator/` files and compatibility fixtures match repair intake (`preserved-runtime.json`). The previous QA 13-journey/three-engine/six-stop/local AutoML and ML strict-report results, 36 V1.7 and 4 report regression results remain evidence for those unchanged bytes. They are **reused**, not freshly rerun whole-source validation. No receipts, hashes, business logic, generator or Python/dbt/report templates changed in this repair.

Native picker/default-browser interaction and exact-revision remote CI remain NOT_RUN release checks under the lead's explicit local-acceptance scope. Actual Spark training/engine, Cosmos/Airflow and provider execution are outside this unchanged-runtime repair. S001 remains unaccepted pending independent repair QA and lead review; S002 remains pending. No commit, push, publication or merge.


Fresh KPI execution/build/preview passed in 378.76s (`out/s001-r1/kpi/execution-smoke.json`); it also rejects a tampered index and failed rebuild, reopens read-only, stops owned servers and creates a distinct subsequent Bronze run. Final separate-process restart passed in 5.97s, with its WPF render inspected: readable historical identity, succeeded/built outcomes and verification scope. Final corrected repair smoke passed against that fresh KPI report in 31.02s (`out/s001-r1/final-repair/repair-smoke.json`); `final-commands.jsonl` records exact commands. The earlier integration driver had already loaded its original binary before the fixture correction and its queued repair step repeated the same reflection serialization harness failure (exit 1). This is retained in `commands.jsonl` and `out/s001-r1/repair/failure.txt`, not relabeled as a pass. The corrected final run supersedes that harness attempt. Canonical Release output was rebuilt successfully after the original process ended, so the normal CLI now contains the corrected smoke too.

Final focused run passed 30/30 after refining the 128-combination test matrix to actual contract kinds and all four runtimes (`final-focused.log` and TRX). Workflow YAML and gate inclusion checks passed; `git diff --check` passed. Final source inventory verifies no unexpected changes to inherited work; runtime/fixtures remain byte-identical to repair intake. Original S001 development, QA and lead packets are preserved. No owned desktop/runtime/preview processes remain.

## Exact next instruction

**READY FOR LIGHT QA.**

Act as light QA. Read projectmanagement/STATUS.md, projectmanagement/reviews/S001-lead.md, projectmanagement/sprints/S001-repair-01.md and the developer's repair report. Independently test both returned cases and affected integration paths using the repair matrix. Preserve the original QA evidence and distinguish reused unchanged-runtime coverage from fresh repaired-desktop checks. Update registers and write projectmanagement/reports/S001-repair-01-qa.md. Return clear defects to development or say CALL TECH LEAD when both repairs are ready for acceptance review. Do not accept S001 or start S002 yourself.
