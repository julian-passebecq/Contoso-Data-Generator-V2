# S001 — Trusted local runs and reopening

State: NEEDS_DEV_FIX after lead review. Lead decision date: 2026-09-08. Developer: medium model. Independent QA: light model. Final acceptance: tech lead.

**Current implementation instruction:** [repair 01](S001-repair-01.md), following the [lead review](../reviews/S001-lead.md). Original A/B/C and the first QA batch are complete; two uncovered correctness cases need repair before S001 can be accepted. Do not redo completed passes.

**User outcome:** run a local journey, inspect results belonging to that run, close Studio, and reopen the evidence later without accidentally executing anything or confusing an old result with the current edited project.

This sprint incorporates the existing uncommitted Studio stabilization and closes the baseline findings before building history on top. Use the current branch and preserve all earlier work. Read [architecture](../ARCHITECTURE.md), [baseline review](../reviews/2026-09-08-baseline.md) and [tests](../TESTING.md).

## Development batch

Execute A → B → C in one continuous assignment. Each pass is substantial and may take roughly 2–4 hours depending on findings; this is a sizing guide, not a timer or required duration. Do not ask the user to start the next pass. Run focused checks between passes, save checkpoints, and continue. The normal independent QA/lead handoff is after C.

### Pass A — Close correctness gaps and define run ownership

Relevant code: `ContosoForge.PipelineStudio/FactoryStudio.cs`, `StudioRuntime.cs`, `StudioSession.cs`, `MainWindow.xaml.cs`; `DatabaseGenerator/Forge/Templates/v17/run.py`, `journey_runtime.py`; report contracts/build helper and `JourneyExporter.cs` if a V1.7 overlay is necessary.

1. Capture the exact starting diff/file hashes including untracked source. Reproduce the negative cases F001–F003 and make durable tests that would fail on this baseline. Use focused tests and fresh small fixtures; do not rerun the whole release matrix before implementing.
2. Replace window-level assumptions about `reportBuildSucceeded`, `lastFactoryState` and `lastFactoryRoot` with a small explicit execution/selection model. Keep current edit revision distinct from recorded run identity. Preserve atomic editor behavior and authored graphs.
3. Implement receipt-based preview eligibility per ADR-005/006. Read selected run evidence and report contract, check run ID, successful build status, report-contract hash, recorded index hash and permitted artifact location. Validate immutable report input/source hashes. An in-memory flag alone cannot authorize preview. Unsupported/malformed or inconsistent evidence fails clearly without modifying it.
4. When editing/applied revision changes, show the old selected run as historical with its identity, or clear the active selection. The chosen UI must never silently imply the old result came from the new revision. Disable mutating actions against historical selections in this sprint.
5. Align V1.7 preflight with runtime imports and metadata requirements. Required dependencies fail before generation; unused optional package metadata is recorded as unavailable or omitted under a documented consistent rule. Preserve selected-engine checks. Keep changes out of legacy templates unless an additive overlay cannot preserve the contract; escalate a required legacy contract change.
6. Make preview-startup, preview-active, run/build and close guards consistent. Closing during preview startup must stop/abort owned startup cleanly or be blocked until safe; it cannot leave a child server behind. Keep execution cancellation deferred and honestly unavailable.

**Checkpoint A:** negative regressions pass with fixes; receipt validator and state rules are testable; original journey editor smoke still passes. Record decisions and move directly to B. Ask the lead only if an architectural boundary must change or intended behavior remains ambiguous.

### Pass B — Persist and reopen evidence safely

1. Implement a minimal versioned local JSON run catalog per architecture. Treat cached status/labels as hints, not truth. Use atomic writes and a concurrency policy that prevents lost entries across application instances.
2. Register a new run once it has a durable generated root/identity; preserve failed/incomplete runs for diagnosis. Preflight failures that generated nothing should not become fake measured runs.
3. Add APIs to list local runs, explicitly import a V1.7 root/state, inspect a selected run, and locate a moved/missing folder. Do not recursively discover everything on disk. Canonicalize paths; reject path escape and mismatched root/state/run identities. Inspect manifest/hash paths without following them outside the selected bundle.
4. Reopening reads data and receipts only. Do not run imported Python, rebuild, resume, repair hashes, change outcomes, or alter portable project files. A stale `running` receipt is "last recorded running; live execution unconfirmed", not a new success/failure invented by Studio.
5. Handle missing/corrupt catalog, duplicate registration, duplicate IDs at different locations, unsupported versions, corrupt/missing receipts, moved folders, failed/export-only runs and partially written evidence. Keep original evidence untouched. Preserve conflicting entries or flag ambiguity instead of silently overwriting a different run.

**Checkpoint B:** service-level round-trip, corruption, identity and concurrency cases pass; reopening survives a new application instance. Record schema/version decisions and move directly to C.

### Pass C — Integrate the complete desktop journey and durable gates

1. Integrate a straightforward history/results selector into the existing WPF design. Show selected run ID, project identity/label, location, recorded outcome, report build outcome and verification state. Distinguish the currently edited project from historical results. Provide concise recovery messages and access to existing logs/evidence paths.
2. Verify run → strict report build → preview → close → reopen → preview for KPI and ML-only paths. Include a Bronze-only run, project/revision switching, failed execution, tampered receipt/index/input, missing folder and unsupported imported version. Do not recompute KPIs or choose ML thresholds in the UI.
3. Add durable Windows tests for the actual asynchronous execution path, not only hidden editor renders. Wire bounded runtime/preview/history tests into the Windows workflow with explicit timeouts and retained diagnostics. A small real Bronze run plus process/receipt/reopening regressions is the minimum CI runtime gate. Keep at least one real strict KPI and one ML-only report build in the final validation matrix; they may reuse existing journey CI infrastructure when provenance is clear.
4. Run the required developer integration checks once after the source settles. Generate from the new binaries/templates. Inspect report output and the history UI where supported. Record any unavailable native interaction precisely.
5. Update docs only to reflect implemented behavior. Write `reports/S001-dev.md`, update STATUS to READY_FOR_QA and identify exact revision/diff manifest, known limits, acceptance mapping and commands. Tell the user **READY FOR LIGHT QA**.

## Acceptance checklist

Only mark an item complete with a report/test reference. Checks initially recorded developer evidence in [S001-dev](../reports/S001-dev.md); the lead has reopened AC04/AC06/AC07 for concrete uncovered cases. Prior test results remain historical evidence, not full acceptance.

- [x] AC01: Existing atomic apply/pending-edit/authored-graph behavior preserved; legacy artifact compatibility unchanged.
- [x] AC02: Preview verifies selected-run identity, report-contract/input hashes and recorded index hash; tampered, mismatched and unsupported evidence rejected. Verification scope is accurately stated.
- [x] AC03: Editor revision, active operation and selected history remain distinct; old outcomes never silently become results for a new revision.
- [ ] AC04: V1.7 dependency preflight agrees with the runnable stage; missing required dependencies stop before generation; unused optional metadata does not fail a bounded run. Reopened for S001-L001.
- [x] AC05: One owned mutating operation at a time; close/switch/preview-startup cleanup leaves no owned server behind; full logs and responsive bounded display preserved.
- [ ] AC06: Catalog persists across restart, writes atomically without lost concurrent updates, and handles corrupt/missing entries without modifying evidence. Reopened for invalid-locator recovery, S001-L002; concurrency evidence remains valid.
- [ ] AC07: Explicit V1.7 reopen/import is read-only, handles all specified statuses/identity/path/version cases, and invokes no generated code. Reopened for S001-L002.
- [x] AC08: Real KPI, ML-only and Bronze flows plus repeat/reopen/failure scenarios pass with fresh run identities and correct report semantics.
- [x] AC09: Durable Windows runtime/history regression gate exists; required tests pass or clearly identified blockers are escalated. No unsupported claim of remote CI success.
- [ ] AC10: Developer and QA packets, branch/test registers, baseline/diff provenance and remaining backlog are complete; lead reviews changed logic before acceptance.

## Scope boundaries and lead gate

No execution resume, cancellation feature, historical-run rebuild, cloud execution/publication, physical pruning, new algorithms/targets, portable model weights, dependency upgrade campaign, WPF framework migration or full rendered-asset-manifest redesign. Do not turn history into a generic workflow database. Newly generated V1.7 behavior can evolve under this plan; legacy contract/fixture changes require lead review.

QA begins after A/B/C. A clear QA defect returns to the developer for an integrated repair batch; another architecture decision or repeated unexplained failure calls the lead early. QA completion always calls the lead. The lead reviews all sprint-changed logic and inherited stabilization areas affected by the sprint, then decides acceptance and S002 scope. Do not implement S002 merely because S001 tests are green.

Development milestones A, B and C completed continuously on 2026-09-08. Acceptance evidence and exact commands: [development report](../reports/S001-dev.md). AC10 remains unchecked until independent QA and lead packets are complete.

Independent QA completed on 2026-09-08: [S001 QA packet](../reports/S001-qa.md). Fresh local checks pass; residual native/remote/conditional scope goes to the lead. AC10 remains unchecked pending the lead review/acceptance packet.

Lead review completed later on 2026-09-08: [S001-lead](../reviews/S001-lead.md). Verdict RETURN TO MEDIUM DEV; L001/L002 and [repair 01](S001-repair-01.md) supersede the earlier acceptance-pending handoff. AC10 is intentionally withheld until repaired behavior is reviewed; S001 is not accepted.

