# Delivery and validation custody

## Git inventory at handover intake

- Origin: `https://github.com/julian-passebecq/Contoso-Data-Generator-V2.git` (user's public fork). `upstream` points to sql-bi; no push there is intended.
- Origin fetched on September 11, 2026. Base: `c6e602ce153ecf5f2a3d2ffc9a046e8cfb246296` on `codex/v1.7-modular-journeys-external-labs`.
- All five existing local branches matched their origin tracking heads. `git log --branches --not --remotes` and `git stash list` returned nothing. One registered worktree exists, at `D:/dotnet/Contoso-Data-Generator-V2`.
- Before handover additions, 14 tracked files were modified and 43 nonignored files were untracked. These include product services, tests, workflows and the entire management packet; all are included in the preservation commit.
- GitHub explicitly queried on the user's fork: PR #1/#2/#3 merged; PR #4 open at `c6e602c`. The handover branch includes PR #4's history plus the formerly dirty work. It is not merged into main and does not update PR #4's head.

## Other task/agent work audit

The desktop listing showed the three S001 tasks sharing this checkout and inactive. Their latest turns were inspected. The complete local Codex thread index was also queried read-only for this repository, finding 36 task/subagent records, including this handover and 22 specialist-agent records. Every matching record uses the same repository directory (Windows extended-path spelling). Historical branch values are main, V1.5, the two V1.6 branches and V1.7, all covered by the Git audit above. The archived-task listing was exhausted and contained no additional project checkout.

No separate agent worktree, stash or local-only branch commit was found. Agent edits in the shared checkout are included regardless of which task wrote them. This is an audit of available local Codex records and registered Git worktrees; it cannot certify unknown copies on another computer or unsaved editor buffers. Raw conversations are not needed to build the app and are not uploaded.

## Validation already performed before handover

These are recorded prior results, not a new test run in this preservation task:

| Evidence | Result and scope |
| --- | --- |
| Original independent S001 QA | 310 .NET tests; WPF Release; five editors; Bronze success/failure; KPI/ML strict execution and restart; 13 journeys; 13-table three-engine parity; six stop boundaries; local AutoML; Python regressions |
| Independent repair QA, September 8–9 | 312 .NET tests, zero skipped; WPF Release zero warnings/errors; five editors; fresh minimal Bronze/dependency checks; KPI strict build/preview/restart; real repair/recovery smoke |
| Repair-specific additions | 16 actual preflight combinations; invalid-locator handler including no-selection recovery; eight writer processes retaining 32 entries; 750 evidence files unchanged across restart/repair |
| Reuse boundary | 245 runtime/fixture files matched repair intake; expensive unchanged ML/AutoML/engine coverage explicitly reused, not rerun |

Primary packet: [repair QA](../projectmanagement/reports/S001-repair-01-qa.md). Detailed historical results: [test register](../projectmanagement/TEST-REGISTER.md). Source manifests: `projectmanagement/reviews/S001-repair-01-qa-{start,final}-files.json`.

During handover, before editing navigation documents, every entry in the repair-QA final file manifest matched its recorded SHA-256. This ties the collected source to that prior QA snapshot without pretending to rerun tests. Navigation/handover documentation is subsequently changed; old manifests remain historical. Git may normalize text line endings in the commit, so working-copy byte hashes and Git blob hashes are not interchangeable.

Fresh handover checks and the actual pushed revision are recorded in [PUSH-RECEIPT.md](PUSH-RECEIPT.md). No application test suite is rerun just to package unchanged application code.

## Deliberately local only

Ignored `out/`, `artifacts/`, `.tools/`, build directories and dependency caches are not source delivery. They remain on this machine. Concise QA reports and source manifests are committed; raw logs, temporary probes, generated data/reports, virtual environments and binaries are not force-added.

Useful raw evidence locations: `artifacts/s001-qa`, `artifacts/s001-lead`, `artifacts/s001-repair-01-dev`, `artifacts/s001-repair-01-qa`; fresh generated runs: `out/qa-s001-r1`. Local validated Python in earlier reports: `.tools/v15/Scripts/python.exe`. These paths will not exist in a fresh Git clone. Durable tests/smokes and workflow definitions are included; ignored one-off QA drivers are not promised as portable deliverables.

No manual source push should be needed once the receipt confirms success. If the successor needs raw evidence on another machine, the user must separately transfer the selected ignored evidence folders or reproduce the relevant checks. Credentials, dependency installations and local run history are not transferred through Git. Release/merge and external execution remain future actions, not consequences of this preservation push.
