# Remaining outcomes

There is no approved specification in which every backlog idea is mandatory for a “full app.” The practical completion target is a dependable local generate → execute → inspect → reopen experience, with clear limits. The priorities below come from the recorded product plan; the Pro AI should resolve scope with the user when necessary, not invent a broader platform.

## Immediate unfinished decision

- Close or return S001-L001/L002 after reviewing the repaired logic and independent QA. Both passed repair QA; no new production defect was reproduced there. S001 has not been formally accepted. Backlog rows still marked NEEDS_DEV_FIX reflect the earlier verdict and require reconciliation after acceptance, not automatic reimplementation.
- Establish CI results for the newly committed source. Green results on `c6e602c` do not validate this handover revision. Keep PR #4's older V1.7 head distinct from the handover branch.

## Core product completion priorities

| Outcome still needed | Done means | Existing backlog |
| --- | --- | --- |
| Controlled cancellation and interruption | Users can stop long owned jobs; unrelated processes survive; interrupted state and diagnostics are honest; no false success or stranded ownership | B006, proposed S002 |
| Safe recovery/retry | Clear distinction between a fresh rerun and legal resume; interrupted stages cannot mix identities or reuse invalid evidence | B007 |
| Guided KPI/ML configuration | Common journeys work without hand-editing JSON, while advanced fields survive round trips and rejected edits are atomic | B008 |
| Understandable results | Users see reconciliation context, validation/test separation, sample sizes, baselines and prediction limitations from stored results | B009 |
| Reproducible first-run setup | A fresh supported Windows host has clear installation/run instructions and actionable Python/Node/path diagnostics | B010 |
| Local release readiness | Supported native interactions and relevant workflows pass for the delivered revision; remaining limits and installation requirements are documented | B001–B005, D001/D004 |

## Known limits and unverified behavior

- Native folder-picker and default-browser interactions were NOT_RUN because native automation was unavailable. WPF execution seams and an in-app browser were tested; these are not equivalent to complete native interaction coverage.
- Node 21/long generated paths caused a report-build failure in earlier work. Short-path builds later passed. Portable setup/long-path handling is still unfinished; successful short-path tests do not erase this limitation.
- Report build receipts cover `index.html`, not every rendered asset. Receipts are unsigned and do not protect against coordinated rewriting of all local evidence.
- Local history reopening is deliberately read-only. Durable cancellation/resume certification is not already delivered by history support.
- Physical dependency pruning is off (`physicalPruning=false`); selecting fewer KPI outputs does not prove less upstream computation.
- Full fresh Spark/Cosmos/provider execution was not repeated for repaired Studio source. Lead-approved local acceptance reused unchanged runtime coverage; required missing-PySpark rejection was freshly tested.
- In this handover session the default PowerShell profile raised broken Anaconda `_ctypes`/`_socket` imports. No-profile PowerShell and the existing `.tools/v15` interpreter worked for inventory. This is an observed host setup issue, not a newly reproduced app defect.

## Expansion candidates, not mandatory local completion

One account-backed external journey remains blocked on choosing a user goal/provider and supplying authorized access (B011). MotherDuck, Kaggle and Hugging Face exports do not constitute executed/published cloud results. Account credentials must not enter Git.

Other candidates: physical pruning (B012), broader wrangling/Spark recipe semantics (B013), stronger governed ML datasets and regression targets (B014), safe portable model serialization (B015), template composition hardening (B016), persistent orchestration and fault/retry certification (B017), and measured bounded-memory scale (B018). Each needs concrete acceptance scope before expanding the app.

No new API service, SaaS control plane, UI rewrite, additional provider collection or deployment platform is required by the active S001 scope. Existing historical Codespaces/Kubernetes/cloud evidence must retain its actual version and execution limits.
