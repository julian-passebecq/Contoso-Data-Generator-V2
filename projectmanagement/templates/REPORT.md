# <Sprint> — <development / QA / lead review>

Date:
Role/model selected by user:
State / next owner:
Branch and full HEAD:
Baseline SHA plus dirty-file manifest:
Final source SHA plus dirty-file manifest:
Source unchanged during validation: yes/no, evidence:

## Outcome

What the user can now do; what remains incomplete. Distinguish implemented, tested, reviewed, committed, CI-verified, merged and externally executed.

## Acceptance coverage

| Acceptance ID | Result | Test/reproduction and exact evidence | Limitation |
| --- | --- | --- | --- |
| AC... | PASSED / FAILED / NOT_RUN / BLOCKED | Command, exit code, counts, artifact path | Scope |

## Changes and logic

Files/components changed and why. Important state transitions, identity checks, data semantics, decision tradeoffs and compatibility impact. Cite existing ADRs; identify any proposed new decision.

## Findings and repair history

| Finding ID / severity | Expected versus actual | Reproduction/source evidence | Owner / state / fix test |
| --- | --- | --- | --- |

Keep finding IDs stable. A reproduced defect, a source-based risk, an environment failure and a missing test are different kinds of evidence.

## Validation

Exact commands, runtime versions, fresh output roots, run IDs, exit codes, pass/fail/skip counts and raw-log paths. Identify reused historical data and all unperformed checks. Include real runtime/visual scope versus mocked/seam coverage. Record owned-process cleanup.

## Next action

Use READY FOR LIGHT QA, RETURN TO MEDIUM DEV or CALL TECH LEAD as applicable. Give one clear next instruction, blockers/decision needed and the precise checkpoint for resuming. Lead reports include ACCEPTED or returned findings and the next scoped sprint only when acceptance is justified. No implicit release/merge approval.
