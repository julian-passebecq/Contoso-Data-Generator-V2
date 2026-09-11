# Roles and sustainable execution

## How a sprint runs

The user starts the medium developer once with the sprint prompt. The developer completes all approved passes, including focused regression checks and routine fixes, without waiting for "new pass" after each milestone. Pass A/B/C are coherent packages of work; a pass can take several hours if needed. Time is a planning guide, never a reason to pad work or stop midway through a useful implementation.

At the end of the development batch, the developer writes a report and says **READY FOR LIGHT QA**. The user can then start the light model once. QA runs the agreed independent validation, maintains the registers, and says either **RETURN TO MEDIUM DEV** with actionable failures, or **CALL TECH LEAD** with an acceptance packet. The lead reviews the actual changed logic and evidence, accepts or returns the sprint, and defines the next sprint.

Within an active session, keep progressing across milestones and use durable checkpoints across context changes. If the host ends a turn or hits an execution/usage limit, checkpoint the precise next step and stop honestly. Documentation cannot wake an idle model or change its model automatically. Do not create scheduled jobs or extra tasks merely to simulate unattended continuation. If the user explicitly enables a coordinated multi-agent run, the same role boundaries and acceptance rules apply.

## Medium developer

- Read the active sprint, architecture decisions, review findings and test cases before coding. Inspect current changes and preserve existing work.
- Own application implementation, useful regression tests and testability seams. Write meaningful tests for defects and state transitions while developing; independent QA is not a substitute for developer validation.
- Make routine class/method/UI-layout decisions within the approved boundaries autonomously. Avoid wholesale rewrites and speculative features.
- Complete A, B and C before the normal QA handoff. At each milestone update STATUS and the sprint checklist, run the relevant focused checks and continue.
- On a test failure, investigate the root cause. Do not change expected hashes, relax provenance checks, skip tests or redefine acceptance to obtain green results.
- Handoff once the integrated feature is ready, with files changed, important decisions, exact checks, limitations and fresh fixtures. Explicitly tell the user when light QA should begin.

## Light QA and backlog support

- Independently inspect the final diff and map every acceptance criterion to evidence. Verify actual behavior and failure cases, not just the developer's summary.
- Run the agreed tests in a separate, fresh output directory after the developer finishes writing source. Do not build/test concurrently with another model editing the same files.
- Maintain STATUS, BACKLOG, BRANCHES and TEST-REGISTER. Record failures, skipped checks, environment blockers, and the precise revision/worktree manifest. Preserve historical evidence and never relabel it as fresh.
- Add small regression fixtures, test scripts or documentation corrections when their meaning is clear. Route production-code fixes to the developer; ask the lead when deciding the correct behavior requires architectural or business reasoning.
- A clear implementation defect goes back to the medium developer with a reproduction and expected result. After correction, rerun affected tests and the required integration gate if affected; do not repeat every expensive suite automatically.
- A completed sprint, an invariant conflict, ambiguous correctness, persistent unexplained failures, or proposed acceptance with a gap goes to the lead. QA recommends a verdict; it does not accept the sprint or invent the next one.

## Tech lead

Own product direction, architectural boundaries, backlog priority, sprint scope and the logic review. Use light assistance for bounded inventory, test execution and evidence preparation. Independently inspect high-risk implementation: state transitions, identity/hash binding, compiler/runtime agreement, business semantics, temporal ML evaluation, cross-engine semantics and failure/retry behavior. Passing tests alone do not establish correct logic.

At review, read the complete sprint diff from the recorded baseline, including untracked source files, inspect negative-test quality, and trace at least one successful and one failing journey through the changed boundaries. Audit all changed logic, with emphasis proportional to risk; do not claim to have certified untouched providers. Record findings with severity, reproduction/source evidence, required fix and test. Mark ACCEPTED only after blockers are closed and required checks pass or a specifically reasoned exception is approved. Then order and scope the next sprint.

## Escalation without frequent interruptions

| Situation | Action |
| --- | --- |
| Routine implementation detail within S001 | Developer decides, records material tradeoff and continues |
| Ordinary assertion failure with known intended behavior | Developer fixes; QA retests |
| Cannot explain failure after two materially different investigations | Record evidence and ask lead; continue independent approved work |
| Need to change public contracts, identity, KPI/ML rules, legacy bytes, supported platforms, or sprint scope | Ask lead before dependent change; prepare concrete alternatives and recommendation |
| Missing account, explicit publication/merge authority, or product priority decision | Ask user with a prepared, reviewable result; continue work that does not depend on it |
| QA complete, milestone marked as lead gate, or high-severity uncertain logic | CALL TECH LEAD with the review packet |
| Long test makes no progress | Inspect logs/process state and documented timeout; stop only owned work when justified, retain failure evidence; do not wait indefinitely |

An escalation packet contains: the decision needed, expected versus actual behavior, smallest reproduction, exact revision, affected acceptance IDs, investigated causes, two options when useful, recommendation and remaining independent work. A status update is not a permission request.

## Branches and handoff records

For S001 use the current branch and preserve its existing dirty diff. Do not move the current changes onto another branch as an incidental housekeeping step. No commit/push/merge is required to finish this planning or development assignment unless the user separately instructs it. If authorized later, stage only reviewed files and record the resulting SHA. A later sprint can use `codex/s002-...` from a verified accepted base when the lead specifies it.

Each report uses [the report template](templates/REPORT.md), saved under `reports/S001-dev.md`, `reports/S001-qa.md`, or `reviews/S001-lead.md`. These files are created when work actually occurs, not prefilled with success. QA preserves finding IDs across repair cycles and records what changed between test runs. Track new ideas in BACKLOG; only the lead changes priority or acceptance intent.
