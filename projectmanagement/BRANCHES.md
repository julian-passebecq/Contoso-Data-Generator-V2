# Branch and release register

Latest lead review: 2026-09-08. Same branch/HEAD and uncommitted source; [S001 returned for repair](reviews/S001-lead.md). Product hashes matched the developer/QA snapshots. Lead changes are management records and ignored probes only. No remote refresh, commit, push, publication, merge or branch operation.

Inventory date: 2026-09-08. **Local Git observation only; no fetch or remote API refresh in this leadership bootstrap.** Tracking refs are cached local state. QA refreshes this register at each handoff and explicitly records whether remote state was consulted.

| Branch | Local head | Cached tracking head | Role / disposition |
| --- | --- | --- | --- |
| `main` | `ea7c47a` | `origin/main` at same SHA | Local known merged V1.6 orchestration base; no new work here |
| `codex/v1.5-data-factory` | `d60dc69` | Same | Historical feature/evidence branch; preserve |
| `codex/v1.6-multi-engine-parity` | `e5ba7b3` | Same | Historical parity branch; preserve |
| `codex/v1.6-final-orchestration-hardening` | `c176de3` | Same | Historical orchestration branch; preserve |
| `codex/v1.7-modular-journeys-external-labs` | `c6e602c` | Same | Current branch, dirty stabilization plus management docs; S001 development target |

Earlier documentation recorded V1.7 as PR #4 and eight passing workflows on `c6e602c`. Its current open/merged state was **not** verified here. Those workflow results cannot validate the uncommitted Studio changes or future S001 implementation. No branch was created/deleted/moved, and no commit/push/merge occurred in bootstrap.

## Existing application changes at intake

Tracked modifications: `.github/workflows/journeys-v17.yml`; Studio `App.xaml.cs`, `FactoryStudio.cs`, `JourneyStudio.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `StudioSession.cs`; V1.7 `journey_report.py`; `README.md`; `HANDOFF.md`.

Existing untracked files: `ContosoForge.PipelineStudio/StudioRuntime.cs`, `NEXT_PASS_PLAN.md`, `docs/project-status.md`, `scripts/test_studio_report.py`. These are earlier work, not artifacts created by the management setup. Intake product hashes are in [baseline file manifest](reviews/2026-09-08-baseline-files.json).

Bootstrap adds `AGENTS.md`, this management folder, and short routing links in existing entry documentation. Ignored bootstrap test/probe artifacts remain under `artifacts/projectmanagement-bootstrap/` and `out/pm-bootstrap-report/`.

## QA update checklist

- Record branch, full HEAD SHA, dirty/tracked/untracked source and baseline diff range or manifest. Never omit untracked source from review.
- List relevant open PRs and CI runs only when actually read, with date, full tested SHA, workflow/job/run IDs and artifact links.
- Distinguish local accepted implementation, committed branch, pushed PR, CI-verified revision and merged release. These are different states.
- Do not delete old branches, rebase/reset user work, or repair branch history as incidental QA housekeeping. Propose cleanup only when useful and authorized.
- If the current branch changes unexpectedly, stop dependent writes and reconcile the new base with the developer/lead; continue read-only inventory if useful.

## S001 developer handoff — 2026-09-08

Current branch and full HEAD are unchanged: `codex/v1.7-modular-journeys-external-labs`, `c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296`. Starting/final dirty source manifests are [S001-start-files](reviews/S001-start-files.json) and [S001-final-files](reviews/S001-final-files.json), including untracked source. Raw tracked diffs are `artifacts/s001-dev/start.patch` and `final.patch`. The final source recheck passed with zero mismatches.

S001 adds RunEvidence/RunCatalog/HistoryStudio/HistorySmoke services, run-service tests, dependency regression and Windows runtime/history gates. The report distinguishes these from inherited stabilization. Unaffected inherited JourneyStudio, StudioSession, journey_report, report tests, journey workflow and routing docs retain their intake hashes (`artifacts/s001-dev/preserved-baseline.json`). Compatibility fixtures were not replaced.

Development and local integration are complete; independent QA/lead review are pending. Remote branches/PR/CI were not refreshed. No branch movement, reset, commit, push, merge or publication occurred. These local results do not certify a clean release checkout.

## S001 independent QA — 2026-09-08

Fresh local branch inventory is `artifacts/s001-qa/local-branches.txt`. Current branch/HEAD remain `codex/v1.7-modular-journeys-external-labs`, `c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296`. Full tracked/untracked dirty custody: [QA intake](reviews/S001-qa-start-files.json), [QA final](reviews/S001-qa-final-files.json), `artifacts/s001-qa/{start,final}.patch` and final-source-check.json. Product source matched throughout; QA edited management records only. Existing Studio work and fixtures preserved.

Independent local QA passed; see [S001-qa](reports/S001-qa.md). State is READY_FOR_LEAD, not accepted, committed, CI-certified or released. Remote refs/PR/CI were not refreshed, and cached branch refs remain cached. No branch movement, reset, commit, push, publication or merge occurred. No old workflow result is evidence for this dirty implementation.


## S001 repair 01 development — 2026-09-08

Branch/HEAD unchanged. Repair custody: reviews/S001-repair-01-{start,final}-files.json, artifacts/s001-repair-01-dev intake inventory/raw patch and final source check. Existing uncommitted Studio work, original evidence packets and compatibility fixtures preserved. READY_FOR_QA; no remote refresh, commit, push, publication or merge. S001 remains unaccepted.

## S001 repair 01 independent QA — 2026-09-08

Current branch/HEAD remain codex/v1.7-modular-journeys-external-labs at c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296. Dirty source identified by reviews/S001-repair-01-qa-{start,final}-files.json and artifacts/s001-repair-01-qa/{start,final}.patch. Final source/report custody checks passed; QA changed management records only, preserving all application work and original reports.

Fresh repaired-desktop QA passed; original heavy matrix reused for independently unchanged runtime bytes. See reports/S001-repair-01-qa.md. State READY_FOR_LEAD, not accepted/committed/remote-CI-certified/released. No branch move/reset/commit/push/publication/merge; remote refs/PR/CI were not refreshed. Old CI does not validate the dirty repair.
