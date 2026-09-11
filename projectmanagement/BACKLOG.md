# Product and engineering backlog

Priority: P1 = correctness/core journey; P2 = next product value; P3 = expansion. State: READY = scoped for the active sprint; CANDIDATE = lead must scope before development; BLOCKED = named dependency; IMPLEMENTED_PENDING_REVIEW = code exists, acceptance incomplete; NEEDS_DEV_FIX = reviewed implementation has a named repair. IDs stay stable. QA maintains facts and links; the lead owns ordering.

| ID | Priority / state | Feature and why | Completion signal | Dependencies / target |
| --- | --- | --- | --- | --- |
| B001 | P1 / IMPLEMENTED_PENDING_REVIEW | Accept existing Studio stabilization: safe edits, preflight, streaming and honest report snapshot | Regressions pass on final integrated source; lead reviews added findings and unverified interactions | Existing dirty diff; S001 |
| B002 | P1 / IMPLEMENTED_PENDING_REVIEW | Validate preview receipts and separate editor revision from selected run; avoid presenting stale/altered evidence | Identity/hash rejection and revision-switch tests; honest history labels | F001, F002; S001 A |
| B003 | P1 / NEEDS_DEV_FIX | Align action-specific preflight and runtime dependency/version collection | Missing required package rejected before generation; unused optional metadata cannot fail a bounded run | F003 fixed; S001-L001 local Spark analysis prerequisite missing; repair 01 |
| B004 | P1 / NEEDS_DEV_FIX | Durable run catalog and read-only reopening; users must be able to leave and return | Fresh/restarted/imported/missing/corrupt/unsupported cases pass without mutating runs | S001-L002 malformed-locator UI exception; repair 01 |
| B005 | P1 / IMPLEMENTED_PENDING_REVIEW | Durable desktop runtime regression gate; close the CI gap | Windows CI contains bounded execution tests and uploads failure evidence; final CI status recorded when available | T-S001 matrix; S001 C |
| B006 | P1 / CANDIDATE | Owned cancellation and interrupted-run diagnostics; long jobs must be manageable | Only owned process trees stop; C# generation/report children covered; no false success, locks/state handled honestly | S001 accepted; proposed S002 |
| B007 | P2 / CANDIDATE | Safe retry/recovery contract; recover without mixing old and new evidence | Explicit resume legality, interrupted stage policy, identity checks and fault-injection tests | B006 + lead ADR; scope separately from simple rerun |
| B008 | P2 / CANDIDATE | Guided KPI/ML forms with advanced JSON retained; reduce setup friction | Common fields work without editing JSON, preserve unknown fields, validate atomically and round-trip shared contracts | S001; proposed S003 |
| B009 | P2 / CANDIDATE | Better result interpretation; explain reconciliation, thresholds and weak predictions | Show stored baseline/validation/test context, sample sizes and limitations without recomputing metrics or test selection | B008, catalog/ML audit; proposed S003 |
| B010 | P2 / CANDIDATE | Portable setup diagnostics, short-path handling and installation guide | Fresh supported host succeeds with documented environment; bad Python/Node/path conditions actionable | B003; no automatic global package changes |
| B011 | P2 / BLOCKED | One complete external journey, chosen for a user goal | Real account-backed export/execute/reconcile or publish receipts with exact identity and returned revision | Local core accepted; user selects provider and supplies authorized access |
| B012 | P2 / CANDIDATE | Physical dependency pruning; avoid computing unrelated data | Selected path equals full-path results, dependency closure includes truth/quality/SCD2/ML requirements, measurable savings | Lead design after B008; currently `physicalPruning=false` |
| B013 | P2 / CANDIDATE | Broader wrangling semantics and eventual Spark recipe adapter | Explicit null/type/join/tie rules with adversarial multi-engine parity, unsupported cases fail clearly | Existing DuckDB/Polars recipe audit |
| B014 | P2 / CANDIDATE | Governed regression targets and useful ML datasets | Label/feature-time legality, embargo/split tests, baselines and independent evaluation | Lead ML/data-generation review |
| B015 | P3 / CANDIDATE | Safe portable model serialization | Declared supported model formats, reload parity and no executable untrusted pickle import | B014 or concrete deployment need; current exports exclude portable weights |
| B016 | P2 / CANDIDATE | Template composition hardening | Detect failed overlay assumptions, retain legacy bytes and inspect generated code, reduce brittle text rewrites if justified | Relevant exporter change or dedicated scoped sprint |
| B017 | P3 / CANDIDATE | Persistent orchestration and retry certification | Actual scheduler deployment, restart/fault injection and single-dbt-build custody maintained | B007 + runtime/infrastructure availability |
| B018 | P3 / CANDIDATE | Scale and bounded-memory processing | Published dataset sizes, time/memory measurements and retained logical parity; no blanket production claim | B012/B013 and representative user workload |
| B019 | P1 / READY | Branch/test/evidence housekeeping | Registers identify exact diff, old/new evidence, missing CI and unresolved tests; concise lead packet | QA every sprint; S001 |

No new FastAPI server, SaaS control plane, database-backed history, extra cloud provider, inference service or visual redesign is approved in S001. An idea belongs here until it has an explicit benefit, boundary, acceptance criteria and a lead-approved sprint.

## Test and release debt

| ID | Gap | Owner / disposition |
| --- | --- | --- |
| D001 | Native folder-picker/default-browser interactions not established by execution seams | QA: attempt a real supported interaction; record precise unavailable steps. Lead reviews residual gap |
| D002 | Close during preview startup or while preview is active not separately certified | Developer adds owned lifecycle checks; QA exercises S001 |
| D003 | New `--execution-smoke-output` absent from workflows at bootstrap | B005, mandatory S001 regression coverage |
| D004 | Current dirty Studio changes have no exact-commit remote CI/release ledger | QA refreshes when authorized/available; never inherit `c6e602c` workflow success |
| D005 | Fresh local Spark/Airflow/Docker and new account-backed V1.7 execution not rerun in bootstrap | Conditional test matrix; historical evidence only until required and executed |
| D006 | Node 21 long-path report failure documented in previous pass | B010; use fresh short output parents for S001 builds; don't conceal failure history |
| D007 | Build receipt hashes index.html but not the full rendered asset tree | Explicit verification limit in S001; full asset-manifest extension requires versioned design/review |

## Priority changes

2026-09-08 — Lead selected local correctness and run reopening as S001, followed by cancellation/recovery design, then guided authoring. External validation and deeper engine/ML features remain candidates. Reassess after QA findings and the user's feedback about pass size; do not expand passes merely to consume time or tokens.

## S001 developer handoff — 2026-09-08

B002–B005 are implemented and locally tested; see [S001-dev](reports/S001-dev.md). D002 now has passing actual startup/failure/active-close checks, and D003 has a Windows Bronze/KPI/ML runtime/restart workflow. Independent QA and remote CI remain separate. D001, D004 and conditional D005 coverage remain explicit; D006 is not erased by successful local Node 21 strict builds. B019 developer registers are updated; QA reconciliation and the lead packet are still required. No later candidate has been reprioritized or started.

## S001 independent QA — 2026-09-08

B001–B005 now have passing fresh independent local evidence in [S001-qa](reports/S001-qa.md); retain IMPLEMENTED_PENDING_REVIEW until lead acceptance. F001/F002/F003 regressions passed; QA recommends closure after lead audit. No new production defect was reproduced. B019 QA registers, exact dirty-source custody and acceptance packet are complete; lead review remains pending.

D002 is independently verified locally (startup/failure/active-close/restart cleanup). D003 workflow implementation is verified; D004 remote execution remains NOT_RUN. D001 native picker/default-browser remains NOT_RUN because native app APIs are disabled, despite successful in-app browser/WPF seam checks. D005 actual Spark/Cosmos/providers remains conditional NOT_RUN; local MLJAR did run. Lead must confirm this diff's gate applicability. D006/D007 remain explicit. No backlog priorities or later sprint candidates changed.

## S001 lead review — 2026-09-08

Verdict: **NEEDS_DEV_FIX**, [lead report](reviews/S001-lead.md), [one continuous repair batch](sprints/S001-repair-01.md).

| Finding | Priority / owner | Required closure |
| --- | --- | --- |
| S001-L002 | P1 / medium developer | Invalid history locator cannot escape the UI boundary, corrupt selection or prevent recovery; durable actual-handler regression |
| S001-L001 | P2 / medium developer | Preflight requires PySpark for an included local Spark ML operation independently of the Silver engine; export/early-stop controls remain valid |

F001/F002 and the specific F003 unused-metadata failure are confirmed fixed in the reviewed bytes. B003/B004 remain open for the new findings; other S001 features await integrated acceptance. B019 now has the first lead packet; light QA maintains a separate repair packet after the fixes.

D001/D004 can remain NOT_RUN for explicitly local sprint acceptance, with native/revision-specific release gates retained. D005 full Spark/Cosmos/provider execution is not required for the current unchanged runtime scope; the local Spark prerequisite negative case **is** required. These decisions do not waive L001/L002. No S002 feature is authorized yet.

Advisory maintenance: the Windows workflow's path filters cover current S001 edits but omit some core generator/build/helper paths. Broaden them when related work is scoped; this is not a current acceptance blocker. Keep diagnostics focused rather than uploading dependency caches in future CI maintenance.


Repair 01 development checkpoint: L001/L002 implemented and locally validated; B003/B004 await independent repair QA and lead closure. See reports/S001-repair-01-dev.md. No acceptance or S002 scope change.

## S001 repair 01 independent QA — 2026-09-08

L001/P2 and L002/P1 passed independent repair QA and are **READY_FOR_LEAD**, awaiting formal closure. See [repair QA](reports/S001-repair-01-qa.md): actual invalid-locator/live-preview recovery, 16 real preflights, 312 tests and affected fresh desktop integration. B003/B004 remain pending lead acceptance; B019 repair QA packet/registers complete. No new production defect was reproduced and no later sprint was started.

Earlier heavy-runtime coverage is explicitly reused after an independent 245-file unchanged-byte check. D001/D004/D005 retain the lead's local-acceptance scope and NOT_RUN labels; D006/D007 remain visible. No priority changes or release authorization.
