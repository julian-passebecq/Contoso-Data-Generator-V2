> Current direction (2026-09-11): the user requested a single-Pro-AI takeover and preservation push. Start with [the takeover guide](../handover/README.md). Prior role-switching prompts and no-push checkpoints below are historical. S001 acceptance remains pending.

# Project leadership and delivery

This folder is the current source of truth for **what we build next, why, who does it, and how we accept it**. Product/reference documentation remains in `docs/`; historical evidence retains its original scope. Created by the tech lead on 8 September 2026 after inspecting the current source and stabilization diff.

The user directs priorities and starts the chosen models. The tech lead owns architecture, difficult logic decisions, sprint acceptance and the next sprint. The medium developer implements substantial batches. Light QA independently tests, organizes evidence and maintains the backlog and branch/test registers.

Start with [current status](STATUS.md), then your role's section in [workflow](WORKFLOW.md). The active implementation contract is [Sprint S001](sprints/S001-trusted-local-runs.md), supported by [architecture](ARCHITECTURE.md), [test strategy](TESTING.md), and the [baseline review](reviews/2026-09-08-baseline.md).

**Current handoff: S001 repair QA is ready for the tech lead.** Read the [repair QA packet](reports/S001-repair-01-qa.md), [lead review](reviews/S001-lead.md) and [repair 01](sprints/S001-repair-01.md). Both returned cases passed independent QA; lead closure and acceptance remain pending. Do not restart the original sprint or start S002.

| Document | Purpose | Primary maintainer |
| --- | --- | --- |
| [STATUS](STATUS.md) | Current owner, next action, pass checkpoint and blockers | Active model; QA reconciles |
| [WORKFLOW](WORKFLOW.md) | Role boundaries, continuous passes, handoff/escalation rules | Tech lead |
| [ARCHITECTURE](ARCHITECTURE.md) | Product vision, source map, invariants and decisions | Tech lead |
| [BACKLOG](BACKLOG.md) | Feature goals, priorities, dependencies, acceptance and deferrals | QA maintains; lead orders |
| [S001](sprints/S001-trusted-local-runs.md) | Approved development scope and milestones | Lead scopes; developer checks off |
| [TESTING](TESTING.md) | Test cases, commands, change-impact gates and evidence rules | Lead specifies; QA executes |
| [BRANCHES](BRANCHES.md) | Branch/release inventory and exact revision limits | QA |
| [TEST-REGISTER](TEST-REGISTER.md) | Fresh versus historical results and outstanding gaps | QA |
| [PROMPTS](PROMPTS.md) | Copy-ready instructions for each model | Tech lead |
| [Report template](templates/REPORT.md) | Consistent development, QA and lead-review packets | Each reporting model |

`NEXT_PASS_PLAN.md` describes the earlier stabilization pass, already implemented locally. Its old "next" instructions are historical. This folder now decides subsequent work. If documents disagree, follow the user's latest instruction, then the active sprint and recorded lead decisions; flag historical contradictions without rewriting old evidence as current.

No model has to implement every backlog idea. Only S001 is approved for development. Later sprint candidates express sequencing and intent; the tech lead will scope them after reviewing S001.

