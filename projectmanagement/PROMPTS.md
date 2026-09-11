# Starting the next model

These prompts are deliberately short because the detailed working contracts live in this repository. Model labels describe roles; select the desired model in your own workflow. No particular model switch or automated continuation is assumed.

## Current handoff — medium developer, S001 repair 01

```text
Act as the medium developer. Read AGENTS.md, projectmanagement/STATUS.md, projectmanagement/reviews/S001-lead.md and projectmanagement/sprints/S001-repair-01.md. Repair both S001-L001 and S001-L002 as one continuous batch, with durable regressions and the specified affected integration checks. Preserve existing work and historical evidence. Do not restart the original A/B/C passes or start S002. Update the repair report/source manifest, then say READY FOR LIGHT QA and provide the repair QA handoff.
```

## Repair QA — after the repaired development batch

```text
Act as light QA. Read projectmanagement/STATUS.md, projectmanagement/reviews/S001-lead.md, projectmanagement/sprints/S001-repair-01.md and the developer's repair report. Independently test both returned cases and affected integration paths using the repair matrix. Preserve the original QA evidence and distinguish reused unchanged-runtime coverage from fresh repaired-desktop checks. Update registers and write projectmanagement/reports/S001-repair-01-qa.md. Return clear defects to development or say CALL TECH LEAD when both repairs are ready for acceptance review. Do not accept S001 or start S002 yourself.
```

## Original medium developer — start S001 (historical; use current handoff above)

```text
You are the medium developer for this repository. Read AGENTS.md, projectmanagement/README.md, projectmanagement/STATUS.md, projectmanagement/ARCHITECTURE.md, projectmanagement/reviews/2026-09-08-baseline.md, and projectmanagement/sprints/S001-trusted-local-runs.md. Execute approved passes A, B and C continuously, preserving the existing uncommitted Studio work. Do not stop after each pass to ask me for a new pass. Write useful regression tests and run focused checks as you develop, then the required integration checks. Update the checkpoint at each milestone and resume from it if context changes. Make routine implementation decisions yourself; escalate only under projectmanagement/WORKFLOW.md. When the complete development batch is ready, write projectmanagement/reports/S001-dev.md and say READY FOR LIGHT QA with the exact next instruction. Do not start the next sprint or publish/merge.
```

## Light QA — after the developer's handoff

```text
You are light QA and backlog maintainer for this repository. Read AGENTS.md, projectmanagement/STATUS.md, projectmanagement/WORKFLOW.md, the active sprint, projectmanagement/TESTING.md and projectmanagement/reports/S001-dev.md. Independently validate the final source against every acceptance criterion using fresh outputs and exact revision/diff evidence. Audit straightforward failure handling and provenance; refer ambiguous architectural or business logic to the tech lead. Maintain projectmanagement/BACKLOG.md, projectmanagement/BRANCHES.md and projectmanagement/TEST-REGISTER.md. Do not treat the developer's summary, old generated output, skipped checks or old CI as current proof. Write projectmanagement/reports/S001-qa.md. Route clear production defects to medium development with a reproduction. When the sprint is ready for acceptance, or a lead decision is needed, say CALL TECH LEAD and give a concise review packet. Do not accept the sprint or choose the next sprint yourself.
```

## Medium developer — repair batch

```text
Read projectmanagement/STATUS.md and the S001 QA/lead findings under projectmanagement/reports/ and projectmanagement/reviews/. Fix the returned implementation issues as one coherent repair batch, preserving accepted behavior and the lead's scope. Add regressions for the root causes, rerun affected checks, update the development report and evidence manifest, then hand back to light QA. Escalate an unresolved architecture decision; do not weaken acceptance criteria or test expectations to close a finding.
```

## Tech lead — acceptance and next sprint

```text
Act as tech lead. Read projectmanagement/STATUS.md, projectmanagement/ARCHITECTURE.md, the active sprint, baseline review, development/QA reports and registers under projectmanagement/. Audit the actual complete sprint diff and changed code logic, especially state/identity/hash binding, runtime/compiler agreement and failure handling. Use light assistance for bounded evidence checks if helpful. Record findings and acceptance/rejection in projectmanagement/reviews/S001-lead.md. If accepted, update projectmanagement/STATUS.md and projectmanagement/BACKLOG.md and define the next substantial sprint with several continuous development passes, acceptance criteria, test instructions and handoff gates. Preserve unverified execution limits and do not infer merge/publication authority.
```
