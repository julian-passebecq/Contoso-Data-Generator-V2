> Current direction (2026-09-11): the user requested a single-Pro-AI takeover and preservation push. Start with [the takeover guide](handover/README.md). Prior role-switching prompts and no-push checkpoints below are historical. S001 acceptance remains pending.

# Working in this repository

Read `projectmanagement/README.md` and `projectmanagement/STATUS.md` before sprint work. They identify the current sprint, role instructions, architecture decisions and evidence requirements. Follow the user's assigned role (tech lead, medium developer, or light QA); do not infer a model identity or switch models yourself.

The current working tree contains pre-existing, uncommitted Studio stabilization work. Preserve it. The baseline inventory is in `projectmanagement/reviews/2026-09-08-baseline.md`. Do not reset, discard, replace compatibility fixtures, or treat old CI results as validation of new edits.

Complete the assigned sprint's development passes continuously. Pass boundaries are checkpoints, not requests for the user to type "next pass". Save a durable checkpoint before a genuine handoff or execution limit. `projectmanagement/WORKFLOW.md` defines when development hands off to QA and when QA calls the tech lead.

Application architecture remains C# contracts/planner/compiler, generated Python execution, dbt business calculations, and WPF presentation. Changes to these boundaries require tech-lead review. Normal implementation choices within the approved sprint do not.

Keep generated data, dependencies, raw logs and temporary probes in ignored `out/` or `artifacts/`. Record concise evidence summaries in `projectmanagement/`. Commit/push/publication/merge authority follows explicit user instructions; the management plan does not itself authorize remote actions or a release.
