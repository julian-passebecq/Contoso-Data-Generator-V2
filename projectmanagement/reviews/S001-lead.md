# S001 — tech-lead review

Date: 2026-09-08. Verdict: **RETURN TO MEDIUM DEV — S001 NOT ACCEPTED**.

Reviewed source: `codex/v1.7-modular-journeys-external-labs`, HEAD `c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296`, plus the uncommitted source identified by [developer final inventory](S001-final-files.json) and [QA final inventory](S001-qa-final-files.json). This review leaves application code and existing evidence unchanged. No commit, remote operation or release occurred.

The architecture and primary flows are sound enough to retain. Two uncovered correctness cases need one focused repair batch before acceptance. Do **not** restart the original A/B/C implementation or begin S002. The next instruction is [S001 repair 01](../sprints/S001-repair-01.md).

## Findings requiring repair

### S001-L002 — P1: malformed history locator escapes the UI error boundary

Locations: `ContosoForge.PipelineStudio/HistoryStudio.cs:18` (`InspectRun_Click`), `:22` (`SelectRun`); `MainWindow.xaml.cs:607` (`Guard`); `RunEvidence.cs:19` (`StateDirectory`).

A syntactically valid catalog can contain an invalid locator, for example a whitespace-only run ID. `RunCatalog.List` accepts the string and the item can be selected. `SelectRun` resets the previous result, assigns the invalid selection and evaluates `selectedRun.State` **before** its evidence-reading try/catch. `StateDirectory` throws `InvalidDataException`. The UI `Guard` catches `IOException` but omits `InvalidDataException`, which inherits `SystemException`, not `IOException`. The exception therefore escapes the click handler; no application dispatcher exception handler was found.

Reproduced against the actual WPF handler: a selected `CatalogEntry` with a whitespace run ID produced `TargetInvocationException.InnerException == System.IO.InvalidDataException` when invoking `InspectRun_Click`. Reflection was used to observe the handler's uncaught exception without intentionally terminating the audit process. An ordinary WPF click would leave that exception unhandled. This is a malformed-locator fixture, not a claim that a normal successful run writes such an ID.

Required behavior: validate/canonicalize a candidate locator before mutating the current selection; expected invalid-evidence exceptions become a visible diagnostic. Keep the catalog and evidence bytes unchanged. A bad entry must not terminate Studio or leave an invalid selection poisoning subsequent Refresh/Run actions. Cover whitespace/overlength IDs and a rejected linked/path locator through the UI boundary; retain corrupt-JSON recovery behavior. Do not broadly swallow arbitrary programming errors to hide the defect.

Acceptance affected: **AC06/AC07**, with AC05 recovery behavior. Existing truncated-JSON and service-level exception tests do not establish UI recovery from a semantically invalid entry.

### S001-L001 — P2: local Spark ML dependency is missing from preflight

Locations: `ContosoForge.PipelineStudio/FactoryStudio.cs:94–105` (`ValidateRuntime`); runtime consequence in `DatabaseGenerator/Forge/Templates/v17/external_labs.py:179` (local Spark ML dispatch). The file references identify the reviewed working tree.

Preflight derives PySpark from the Silver engine. It adds sklearn/pandas/numpy/joblib for an analysis stage, but does not account for `analysis.kind=spark-ml` with `analysis.runtime=local` when Silver uses DuckDB/Polars/pandas. The planner supports that independent combination. It reaches local Spark training later, where PySpark is needed.

Reproduced using a copy of `examples/v17-spark-ml.project.json` with only `product.analysis.runtime` changed from `colab` to `local`. The actual WPF plan says `runnable`; actual `ValidateRuntime(false)` passes with `.tools/v15/Scripts/python.exe`; an independent `pyspark` import check using that same interpreter fails. No generation/training was needed to establish the missed prerequisite. A run would discover the missing package only after earlier pipeline work.

Required behavior: calculate requirements from all operations that will execute and their independent runtime choices. Require PySpark for local Spark ML when the analysis stage is included; do not require it solely for a Colab export or a stop boundary before analysis with a non-Spark Silver engine. Preserve mandatory Silver-engine dependencies and the optional-metadata fix. Add a matrix for engine × analysis runtime/kind × stop boundary rather than one positive fixture with every package installed.

Acceptance affected: **AC04**. This is a desktop preflight defect within S001, even though actual Spark training and provider execution are outside the fresh QA evidence.

Both reproductions: `artifacts/s001-lead/lead-probes.txt`, harness `artifacts/s001-lead/probe/Program.cs`. Command: `dotnet run --project artifacts/s001-lead/probe/Probe.csproj --configuration Release`. Harness exit 0 means both expected baseline defects were reproduced, not that the product behavior passed. Test user-data/preferences were isolated under the lead artifact directory; no existing run was modified and no runtime/preview child remains.

## What the review found sound

- F001 is addressed: preview validates selected run IDs, original identity files, declared source/compiler hashes, report contract/input/source hashes, expected index location and recorded index hash. The versioned upstream snapshot avoids circular provenance. The declared rendered-asset and unsigned-receipt limits remain accurate.
- F002 is addressed for normal applied edits: selection is cleared, old files remain, explicit reopening is labeled historical and cannot rebuild. Execution captures project/graph/interpreter/run separately from history.
- F003's specific optional-metadata failure is addressed: only `PackageNotFoundError` becomes `unavailable`; mandatory selected-engine metadata and other errors are not suppressed. L001 is an additional requirement-selection omission, not a failure of that fix.
- Catalog read/merge/write is serialized under an exclusive sibling file lock. Temporary bytes are flushed and replaced atomically, with bounded contention retries. Duplicate IDs at different roots remain distinct; corrupt JSON is preserved. L002 concerns the consumer's handling of invalid locators, not lost concurrent writes.
- Imported evidence inspection uses C# file/JSON reading and does not execute generated scripts. Preview paths reject traversal/reparse points, including links in the served tree. This is point-in-time local validation, not signed evidence or protection against coordinated concurrent rewriting.
- Existing atomic journey application, authored-graph behavior, stored KPI/ML display, owned process draining, busy guards and preview cleanup retain the intended boundaries. No alternate planner, UI business calculation, silent resume, new provider or legacy template rewrite was introduced.

## Audit and evidence scope

Read all changed/new production logic in the Studio services, runtime/selection/history integration, inherited affected journey/session changes and the V1.7 Python/report diff; reviewed new service tests, WPF execution/history smokes, dependency script, test-project linkage and workflows. Traced a successful preview/import and multiple rejection paths. The existing generator/planner/ML/dbt modules were inspected where needed to determine contract and runtime ownership; this is not a new certification of every untouched module.

The independent QA packet's main-flow results remain valid for its recorded bytes: 310 .NET passes, five editor variants, real Bronze/KPI/ML desktop flows and restart, 13-journey/three-engine local matrix, local MLJAR, strict reports, dependency checks and independent file audits. Those tests did not cover L001/L002. The lead added targeted executable probes rather than repeating the same full suites. A light evidence assistant checked custody and retained QA/workflow records separately.

## Explicit decisions on residual coverage

| Gap | Lead decision |
| --- | --- |
| D001 — native folder picker/default browser | May remain NOT_RUN for **local sprint acceptance** once code blockers are repaired. Standard native dialogs/shell launch are thin boundaries; WPF seams, real process flow, loopback and visual report checks provide useful local coverage. Keep a native walkthrough pending before declaring a desktop release ready. No user interruption is needed solely to waive a claim we are not making. |
| D004 — current remote CI | Not required to accept an explicitly local dirty-worktree sprint. Exact committed/pushed revision gates remain required for release/merge validation when authorized. Do not infer success from the old `c6e602c` workflows; no push is authorized by this review. |
| D005 — Spark/Cosmos/providers | Fresh full Spark engine/training, Cosmos DagRuns and account-backed execution are not required for the present implementation-only scope: transformation, training, orchestration and provider code did not change. **Local Spark-ML prerequisite rejection is required**, because the changed desktop dependency selection affects that path. Reassess if the repair changes actual runtime/engine logic. |
| Legacy V1.5/V1.6 strict builds | Shared templates remain byte-preserved. Contract/source review, legacy editor/compatibility gates and owned-preview version tests suffice for local acceptance of this diff. A modified shared builder or legacy template would trigger affected real builds. |
| D006/D007 — Node/path and asset hash limits | Remain visible backlog limits. Do not call Node 21 supported based solely on one successful build; CI declares Node 22. Index/input integrity is not a full rendered-asset manifest. Neither requires redesign in this repair. |

These are scope decisions, not acceptance: S001 is returned because of L001/L002. QA must keep NOT_RUN results distinct from a lead-approved local scope exception.

## Handoff

Medium developer executes [repair 01](../sprints/S001-repair-01.md) continuously, then light QA tests the repaired cases and affected integration paths, then **CALL TECH LEAD** again. The lead will verify closure, accept S001 if justified, and only then finalize S002. The next feature priority remains owned cancellation/interruption handling; it is not authorized as part of this repair.
