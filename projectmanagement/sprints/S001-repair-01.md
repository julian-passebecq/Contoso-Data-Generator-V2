# S001 repair 01 — prerequisite selection and history error recovery

State: READY_FOR_LEAD within S001; development and independent repair QA completed, lead closure pending. Ordered by tech lead on 2026-09-08. Source/decision: [S001 lead review](../reviews/S001-lead.md). Two returned findings; no new feature sprint.

**Work as one continuous repair batch.** Complete both sections below, integrated regression checks and the handoff without asking for a new pass. Preserve all current application work and the original QA evidence. Do not repeat the original A/B/C implementation or add S002 cancellation work. Scope is determined by correctness, not a minimum number of hours.

## R1 — Correct the two boundaries

1. **S001-L002 (P1): history locator recovery.** Reproduce an invalid catalog run ID through the actual Inspect-selected handler, not only a call to `StateDirectory` expected to throw. Validate candidate root/run ID/state before replacing the selected run or stopping a valid preview. Catch expected evidence-format/path errors at the UI boundary, including `InvalidDataException`; preserve diagnostics and a usable previous/no-selection state. Keep malformed catalog bytes and evidence intact. Verify subsequent selection of a good entry, refresh, project edit and new run remain usable. Do not convert a malformed locator into a valid run ID by inventing or silently rewriting it.
2. Validate catalog-entry shape/semantics either on load with an explicit recoverable catalog error or per selection with clear diagnostics. Pick the smallest coherent policy and document it. Both whole-catalog recovery and per-entry recovery are permitted; neither may overwrite corrupt data. Keep multi-process serialization and atomic replacement unchanged unless a demonstrated defect requires a change.
3. **S001-L001 (P2): composite runtime prerequisites.** Model prerequisites from the compiled operations plus independently selected Silver engine and analysis runtime/kind. A local Spark-ML analysis included by the stop boundary requires PySpark even with DuckDB Silver. A Colab export or a stop before analysis does not acquire this requirement merely from `kind=spark-ml`. Keep existing shared import requirements (including export packaging helpers), mandatory engine metadata and optional unused metadata behavior. A small testable requirement resolver is appropriate; no planner/runtime rewrite or new environment installer.
4. Review supported local/export combinations against actual dispatch imports. Add the missing cross-layer case rather than installing every optional package to mask selection mistakes. Preserve credential/provider boundaries; do not execute provider APIs in preflight. Surface which selected action needs the missing prerequisite.

## R2 — Durable regressions and integrated handoff

| Test | Expected result |
| --- | --- |
| R-T01: catalog contains whitespace/overlength ID; inspect through actual UI handler | No uncaught handler exception, visible diagnostic, no invalid selected state, original catalog/evidence bytes unchanged |
| R-T02: rejected linked/path locator and corrupt JSON; then valid entry | No process termination or broken subsequent refresh/import/new-run controls; previous selection kept or safely cleared under documented policy |
| R-T03: DuckDB Silver + included local Spark ML, interpreter lacking PySpark | Actual Studio preflight rejects before generated output or catalog entry exists; project/pipeline/pending edits preserved |
| R-T04: same analysis kind with Colab runtime, and local analysis excluded by stop boundary | PySpark absence alone does not block these non-Spark local operations; other actually required packages still checked |
| R-T05: Spark Silver regardless of downstream ML choice; ordinary sklearn path; absent unused metadata | Required engine prerequisite remains required; ordinary local paths and the minimal DuckDB/PyArrow Bronze case still work |
| R-T06: real desktop integration and recovery | Fresh Bronze success/failure, KPI build/preview/restart and catalog concurrency still pass; new error handling cannot bypass receipt validation |

Use an isolated user-data directory and fresh short output roots. Do not alter the user's saved interpreter or install global packages. Use available validated environments or deterministic dependency-test seams that exercise the actual requirement selector. At least the missing-PySpark negative case must use a real interpreter where PySpark is absent; the current `.tools/v15/Scripts/python.exe` met that condition at lead review, recheck it.

Add durable tests to the normal .NET/WPF/Windows gate as appropriate. Production fixes belong to the medium developer. Record regression-before/fix-after evidence, affected source hashes and exact commands in a new repair section or separate `projectmanagement/reports/S001-repair-01-dev.md`; preserve the original development/QA packet rather than rewriting its historical results.

Run the full .NET suite once after integrated repairs, affected editor/history smokes, new preflight/invalid-locator cases, minimal dependency tests, and R-T06. Light QA independently repeats the new cases and affected desktop integration after handoff, updates the registers and writes `projectmanagement/reports/S001-repair-01-qa.md`.

**Avoid redundant expensive work:** if the repair is confined to C# prerequisite selection, locator/UI error handling and their tests, retain the earlier full V1.7/ML matrix as evidence for its unchanged generator/runtime/template bytes. Record that reuse explicitly; it is not a fresh whole-source run. A fresh full ML report/AutoML/three-engine matrix is not required solely for those C# repairs. If runtime/templates, receipts, business logic or shared artifact verification change, rerun the affected matrix per TESTING and explain the expanded scope.

The dev handoff is **READY FOR LIGHT QA** after both repairs. QA calls the lead after successful independent validation or an unresolved design question. S001 remains unaccepted until the lead closes L001/L002; S002 stays a candidate. No commit/push/publication/merge is implied.


## Development checkpoint — 2026-09-08

- [x] R1: both lead cases reproduced before edits; locator state mutation and composite prerequisites repaired.
- [x] R2: durable actual-handler/preflight and dependency-matrix regressions; full .NET, affected editors, fresh minimal Bronze and KPI strict/restart, catalog concurrency and dependency checks completed.
- [x] Development report and final source custody written: [S001-repair-01-dev](../reports/S001-repair-01-dev.md). READY FOR LIGHT QA.
- [ ] Independent repair QA and lead closure. Original sprint remains unaccepted.

Independent repair QA passed R-T01–06: [S001-repair-01-qa](../reports/S001-repair-01-qa.md). CALL TECH LEAD. Combined QA/lead checkbox remains unchecked until lead closure; S001 is not accepted.

