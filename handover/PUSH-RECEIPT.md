# Verified preservation push — 11 September 2026

The collected implementation and takeover guide were committed and pushed successfully:

- Branch: `codex/pro-ai-handover-2026-09-11`.
- Preservation commit: [`4db3b9243b84a104510bb28b756c1aab5232ba3a`](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/commit/4db3b9243b84a104510bb28b756c1aab5232ba3a).
- 60 files changed: 4,616 insertions and 66 deletions, including all previously nonignored uncommitted work.
- `git ls-remote origin refs/heads/codex/pro-ai-handover-2026-09-11` independently returned that exact commit after push. Tracking was established; working tree was clean and no local-only branch commits remained.
- This receipt is a documentation-only follow-up commit. The branch tip therefore advances beyond the preservation commit. No source/test/workflow changes are made in the receipt follow-up.

Fresh packaging checks: prior repair-QA manifest matched at intake; staged file inventory inspected; common credential/private-key patterns returned no matches; no staged file exceeded 10 MB. The full whitespace check reported only existing extra blank lines at EOF in five management files. Those historical records were preserved; the check excluding those five passed. No new application test execution or acceptance is claimed.

Eight GitHub Actions workflows started for the preservation commit. At the post-push snapshot they were queued or running, with no final conclusions. Their eventual results belong to the exact source commit above:

| Workflow | Run |
| --- | --- |
| pipeline-studio-windows | [34590955105](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/34590955105) |
| journeys-v17 | [34590955119](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/34590955119) |
| validate | [34590955139](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/34590955139) |
| factory-v15 | [34590955033](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/34590955033) |
| factory-v16 | [34590955166](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/34590955166) |
| spark-parity-v16 | [34590955107](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/34590955107) |
| orchestration-v16 | [34590955126](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/34590955126) |
| free-gcp-contracts | [34590955100](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/34590955100) |

The takeover AI should inspect these results before a release decision. This preservation task does not wait for the expensive matrix or claim it passed. No new PR, merge, release, cloud publication or upstream push was performed. PR #4 remains on the older V1.7 branch.

No manual source push is outstanding. Ignored generated runs, raw evidence and environments remain local as explained in [DELIVERY.md](DELIVERY.md).
