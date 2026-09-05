# Contoso Forge V1.6 final orchestration handoff — 2026-09-05

Implemented the final orchestration pass on `codex/v1.6-final-orchestration-hardening`, from fetched `origin/main` at the verified PR #2 merge `35d1959c6ecb891b50ba45a47f44d373f76acac7`. [PR #3](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/pull/3) remains open; do not auto-merge. The measured implementation is `05071147fd670287b4864e6279e049c23837bfe0`. This evidence/documentation follow-up is separately gated on all seven workflows before delivery.

[The orchestration guide](docs/v1.6-orchestration.md) gives the architecture, versions, commands and boundaries. [The exact evidence ledger](docs/v1.6-orchestration-evidence.json) records four real CI DagRuns and their invocation, project, warehouse, callback, manifest/run-results, Airflow metadata and report hashes. The capture script checks the GitHub archive digests and that the artifacts belong to the recorded commit/run, then verifies retained witness bytes and identities.

- **Airflow 3.3.1 / Cosmos 1.15.1** executed the generated DAG using `airflow dags test`, with zero import errors and **58 successful tasks in each of four runs**. This proves complete local DagRuns, not a persistent scheduler or Kubernetes deployment.
- Every run executed **exactly one full dbt build** through the Cosmos Watcher producer against `warehouse.duckdb`: **27 models, 135 tests, zero failures/skips**. The callback archived whole-project artifacts before Cosmos cleanup. Artifact adoption supplied the exact canonical `dbt/target/manifest.json` and `run_results.json` to reconciliation; no second plain build ran.
- All five KPIs reconciled in all four runs. sklearn ML, BI without ML, and export-ML each passed strict Evidence production builds. The export remains `exported-not-executed`. Two no-ML DagRuns on the same generated project used different invocation IDs/nonces and could not adopt one another's evidence.
- **250 .NET tests passed**, including all **152 audited legacy artifact hashes**. **43 new orchestration Python checks** pass in each CI matrix job, alongside the existing **123 distinct Python checks** and **three explicitly skipped optional Google-client integrations**. Release capture also rejected a different CI revision, different artifact revision and incorrect archive digest.
- DuckDB/Polars/pandas logical parity, actual Spark 4.0.4 parity, direct dbt, ML legality/embargo/validation-only selection, Evidence, Spark classic/Connect, free-GCP and WPF gates remain green.
- Only the evidenced V1.6 DuckDB/Cosmos dbt stage becomes `runnable`/`reconciled`, citing this ledger. Newly generated plans remain `currentExecutionStatus=not-executed`. Other Cosmos engine/product combinations remain generated. Historical direct-dbt ledgers are unchanged.
- Active Airflow CI and explicit V1.6 Minikube exports align to 3.3.1. Byte-audited legacy/V1.5 Minikube exports retain 3.2.2; both Helm variants validate. No new cluster execution is claimed.

All seven workflows succeeded on the measured implementation:

| Workflow | Run |
| --- | --- |
| factory-v15 | [33987236497](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33987236497) |
| factory-v16 | [33987236493](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33987236493) |
| free-gcp-contracts | [33987236539](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33987236539) |
| orchestration-v16 | [33987236531](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33987236531) |
| pipeline-studio-windows | [33987236491](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33987236491) |
| spark-parity-v16 | [33987236486](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33987236486) |
| validate | [33987236505](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33987236505) |

The final follow-up's checks are attached to PR #3. Raw workflow artifacts are `v16-orchestration-ml`, `v16-orchestration-export` and `v16-orchestration-bi` under the orchestration run; archive SHA-256 values and exact evidence file hashes are in the ledger. Live source/Silver/warehouse checks run inside CI; release capture verifies the retained artifacts and does not pretend that omitted large files were reexecuted locally.

Remaining boundaries: no persistent Airflow service, managed deployment or fresh Kubernetes proof; no retry/fallback certification; other Cosmos engine/product combinations remain unverified. Integrity checks assume a trusted runtime directory. Historical hosted/cloud and ML export records retain their original scope. No V1.7, FastAPI product, Iceberg, NoSQL, Polars Cloud or new provider was added. Large-data parity sorting and generic CDC tie policies remain separate future work.

---

The following handoffs are historical and retain their original evidence scope.

# Contoso Forge V1.6 release handoff — 2026-09-05

Implemented real Polars and pandas Bronze/Silver adapters and canonical logical parity on `codex/v1.6-multi-engine-parity`, starting from merged remote main `b004a4d98e65eb9693a144db17f64676470946eb`. PR #1 and the complete V1.5 final head were verified in main before branching. The measured implementation is `4661b7df433e5265e83fd6ca9d4c1a41d139fd0d`; the final follow-up adds the ledger, stricter evidence recapture and accurate per-stage planner engine labels. The final branch is separately gated before PR delivery. Do not merge automatically.

[The V1.6 guide](docs/v1.6.md), [canonical encoding specification](docs/v1.6-canonical-encoding.md) and [machine-readable evidence ledger](docs/v1.6-evidence.json) describe the executable behavior and retained measurements.

- `local-fast` remains DuckDB; `local-polars` and `local-pandas` use real DataFrame transformations, typed CSV ingestion and persisted Parquet. Existing C# contracts, compiler, graph and Studio remain authoritative. `product.version=1.6` is additive; V1.5 and legacy defaults remain supported. Studio displays the selected Silver engine and shared warehouse. Source verification uses Python; independent persisted-count/KPI checks and the warehouse use DuckDB.
- All three local engine runs under `out/v16-release/<engine>/.forge/v15/v16/` passed seven stages: **27 dbt models, 135 tests, zero failures/skips**, all five Gold KPI reconciliations, four sklearn candidates with preserved legality/embargo/validation-only selection, and strict Evidence production builds. Runtime versions, report HTML hashes, model metrics hashes and source/run identities are captured in the ledger. Local Node 21.7.1 builds passed; CI repeats with Node 22.
- `out/v16-release/engine_parity.json` is a real successful DuckDB/Polars/pandas comparison. **All 13 governed Silver tables / 10,477 rows match** in logical schema, key/multiplicity, row/null counts and canonical SHA-256. The source fingerprint is `8333b366a8a76ada6b1e4102fdc2a9a0bae9f97b041811ff70857cc27e90a8ad`. Physical Parquet integrity and logical hashes are distinct checks.
- `out/v16-release/spark_engine_parity.json` also matches every table after actual Spark **4.0.4** classic execution in WSL/Java 17. This is explicitly **Bronze/Silver-only Spark parity**, not a new full local Spark factory preset or hosted execution claim. The dedicated Spark CI artifact independently matches the local logical hashes. Existing classic and true Connect checks remain green.
- **246 .NET tests passed**, including all 152 audited legacy artifact hashes. **123 distinct Python checks passed**, with **three optional Google-client integrations skipped**. V1.5's 19 checks also ran separately for each local engine. The 34 new checks cover encoding, value/key/null/empty/schema mutations, bounded diagnostics, persisted tampering, duplicate replay, empty optional tables, quarantine and the exact 24-hour lag boundary. Tests prohibit DuckDB connections inside Polars/pandas transformations. Five intentionally failed comparison reports are retained under `artifacts/v16/negative-parity/`.
- Four local WPF smokes passed: legacy, V1.5, Polars and pandas. The Windows workflow repeats all four and renders the actual UI. Pending edits, shared compiler identity, selected-engine visibility and save/load/compile are verified.

All six workflows passed on measured implementation `4661b7d`:

| Workflow | Successful run |
| --- | --- |
| validate | [33980017294](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33980017294) |
| pipeline-studio-windows | [33980017409](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33980017409) |
| free-gcp-contracts | [33980017283](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33980017283) |
| factory-v15 | [33980017342](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33980017342) |
| factory-v16 | [33980017309](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33980017309) |
| spark-parity-v16 | [33980017334](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33980017334) |

The final evidence/label follow-up must also pass these workflows; its final checks are attached to the V1.6 PR. The ledger deliberately records the completed measured implementation rather than inventing its own future commit SHA or future Actions IDs.

Run the reproduction commands in the guide with fresh output locations. Capture rejects changed source/run/Parquet/report artifacts, modified table reports, dirty recorded implementation checkouts, and CI records from another implementation revision. Raw outputs, npm packages, databases and credentials remain untracked.

Remaining boundaries: Cosmos/Airflow task execution, native MotherDuck/Dive, hosted notebooks, native BigQuery/BQML and infrastructure deployments are not recertified by V1.6. Historical evidence remains historical. FastAPI, Polars Cloud, Iceberg and NoSQL were not added. The comparator materializes tables and sorts canonical rows, so large-data parity needs bounded external sorting. Conflicting CDC events with identical precedence fields need a governed tie policy before external ingestion expands. Cosmos still needs invocation-bound task evidence before removing its duplicate dbt build. These are explicit later concerns, not fabricated release proofs.

---

# V1.5 hardening audit — 2026-09-05

Hardened the existing `codex/v1.5-data-factory` branch in implementation commit `172525abccd8661aba801925ee7cc61ffa352d4f`. The architecture remains unchanged. [The hardening design note](docs/v1.5-hardening.md) describes the exact controls, threshold policy, dependency trial and remaining boundaries. [The recaptured evidence ledger](docs/v1.5-evidence.json) binds local output hashes and all four successful GitHub Actions runs to this implementation. The subsequent documentation/evidence commit is also gated on all four workflows before PR creation; its checks are available on the PR. Do not merge automatically.

Changes and measured results:

- Optional `sourceProject.generation.ml` profile `causal-v1` adds deterministic `positiveOutcomeRate=0.10`, `signalStrength=0.5`, `noiseLevel=0.1`, each in `[0,1]`. Delivery delay causes later review/survey outcomes through a separate seeded random stream. Prediction-time source bytes never depend on those controls. Omission preserves legacy generation and all **152 audited artifact hashes**. The new example is `examples/v15-local-ml-causal.project.json`; the original ML example remains unchanged.
- sklearn and secondary Spark ML retain threshold **0.5** and choose alternatives by **validation F1 only**, with explicit deterministic ties. Model selection remains validation AP only. Both selections freeze before test evaluation. Metrics, confusion matrices, validation PR/threshold curves, both persisted decisions and the model card are measured artifacts. Evidence projects those artifacts into ranking, PR/threshold charts and comparison tables; KPI/label logic stays in dbt and ML logic stays in Python.
- The legacy fixture still selects logistic regression. Its test AP `0.18754007883316906` and ROC-AUC `0.765427643450018` remain unchanged. Baseline 0.5: F1/precision/recall `0`, confusion `[[163,0],[17,0]]`. Frozen threshold **0.12136322464521697**: test F1 **0.34408602150537637**, precision **0.21052631578947367**, recall **0.9411764705882353**, confusion **`[[103,60],[1,16]]`**. That includes 60 false positives; these are educational results, not a deployment recommendation.
- The causal defaults produce 88/796 train, 14/140 validation and 22/180 test positives. Validation selects histogram gradient boosting; its frozen threshold `0.07563245043365832` gives test F1 `0.14736842105263157`, precision `0.0958904109589041`, recall `0.3181818181818182`, confusion `[[92,66],[15,7]]`, AP `0.11734035938330517`. Defaults were not tuned on test performance.
- **Adopted DuckDB 1.4.5 LTS and dbt-core 1.11.14**, the latest stable, non-yanked 1.11 patch checked on PyPI. dbt-duckdb 1.10.1, pandas 2.3.3, sklearn 1.7.2 and PyArrow 23.0.1 remain pinned. Installation, `pip check`, actual pipelines, strict Evidence builds and all workflows passed; no compatibility fallback was needed. The local resolver also updated dbt's sqlparse dependency to 0.6.0.
- Fresh `out/v15-audit-{bi,ml,ml-causal}/.forge/v15/audit/` runs passed six/seven/seven stages respectively. Each passed **27 dbt models, 135 tests, zero failures/skips**, and all five independent Gold KPI reconciliations. All three strict Evidence sources/production builds passed. HTML hashes and exact dependency versions are in the ledger. Local Node 21.7.1 emitted known engine/deprecation warnings; CI uses Node 22 and also passed.
- **233 .NET tests passed**, including legacy compatibility, deterministic controls and truthful Cosmos status. **89 distinct Python checks passed**, with **3 optional Google-client integrations explicitly skipped**. The 19 V1.5 checks passed against both ML fixtures, independently recomputing both operating points and verifying that changed test labels cannot change selection. Four notebook exports validated and the portable sklearn package executed independently. The V1.5 WPF smoke passed locally; GitHub Windows CI repeated both legacy and V1.5 smokes.
- Actual secondary Spark ML **4.0.4** ran in WSL Ubuntu/Python 3.10.12/Java 17 using the upgraded dependency pins, recorded under `artifacts/v15-harden-spark-ml/`. This is a model comparison on the legal Gold mart, **not Spark↔DuckDB logical table parity**. The earlier same-session Spark continuation and hosted/cloud runs are retained as historical evidence, not re-certified.

All four implementation workflows succeeded:

| Workflow | Exact green run |
| --- | --- |
| factory-v15 | [33976180497](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33976180497) |
| free-gcp-contracts | [33976180463](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33976180463) |
| pipeline-studio-windows | [33976180457](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33976180457) |
| validate | [33976180504](https://github.com/julian-passebecq/Contoso-Data-Generator-V2/actions/runs/33976180504) |

The [logical parity TODO](docs/v1.5-hardening.md#logical-engine-parity-todo) specifies `engine_parity.json`: both real engine identities, row counts, keys, governed nulls, canonical logical schema/value encoding and SHA-256 hashes. No parity file or success claim was fabricated. Cosmos remains **generated/unverified**: its TaskGroup executes dbt against `cosmos.duckdb`, then a second plain build executes against `warehouse.duckdb`. Plain results cannot certify the Cosmos invocation; promotion requires actual task execution with complete, unambiguous invocation-bound results. The planner now enforces that boundary.

Reproduce with fresh `out/v15-audit-*` locations, the existing V1.5 commands and run ID `audit`; run both ML examples and the BI example. Recapture retained results with:

```powershell
.tools/v15/Scripts/python.exe scripts/capture_v15_evidence.py --bi-root out/v15-audit-bi --ml-root out/v15-audit-ml --causal-ml-root out/v15-audit-ml-causal --spark-ml artifacts/v15-harden-spark-ml --previous-ledger artifacts/v15/pre-hardening-evidence.json --ci-runs artifacts/v15-audit-ci.json --run-id audit --output docs/v1.5-evidence.json
```

The previous ledger is preserved byte-for-byte in the ignored artifact path and in Git at `b44fd57:docs/v1.5-evidence.json`. Raw data, runtime state, npm dependencies and credentials remain outside Git. The following handoffs describe earlier implementation passes and retain their original scope.

---

# V1.5 continuation — 2026-09-05

Implemented the Data Factory / ML Lab / BI Validation slice on branch `codex/v1.5-data-factory`. The first repository operation was `git fetch origin main`. Remote main matched the audited ZIP pin, `b9fe2b6f8708a57a91d6a6ba4241e4a4a1661b8f`; no reset or historical checkout was used. All 20 continuation-pack files were read, starting in the user's specified order. The user's direct request governed scope; embedded continuation prompts were treated as source material.

The additive `product.version=1.5` contract retains the existing C# source project, neutral compiler and offline planner. WPF now starts at Business and exposes all ten requested sections. Opt-in local-fast is executable with a real DuckDB Bronze/Silver adapter, dbt staging/intermediate/Gold/tests, independent truth reconciliation, optional bounded sklearn training, and Evidence even with ML disabled. Existing Spark, Colab classic/true Connect, BigQuery Sandbox/dbt-bigquery/BQML, Airflow, Kubernetes/GitSync, IaC, semantic/KPI/truth contracts and all previous tests remain. Legacy default generation retains its audited 152 hashes.

Exact artifact paths, hashes, versions, timestamps, dbt counts, KPI comparisons and model metrics are in [docs/v1.5-evidence.json](docs/v1.5-evidence.json). [docs/v1.5.md](docs/v1.5.md) contains the full changed behavior, capability matrix, limitations and commands. The new HEAD is the commit containing this section (`git rev-parse HEAD`); its changed-file list is reproducible with `git diff --name-only b9fe2b6f8708a57a91d6a6ba4241e4a4a1661b8f HEAD`.

Actual release evidence:

- `out/v15-release-bi/.forge/v15/release/`: six stages succeeded, 60 orders, ML disabled, 11 Bronze/13 Silver tables, 27 dbt models/135 tests, all five Gold KPIs reconciled, strict Evidence sources/build succeeded. Built HTML SHA-256: `456b6a8edb85c9c903e09c834cbc4e2e522170c0f5c78d1e22df64eb1e0d7e8b`.
- `out/v15-release-ml/.forge/v15/release/`: seven stages succeeded, 1,200 orders/365 days, same dbt model/test gate, real dummy/logistic/RF/histogram-gradient-boosting fits, and strict Evidence build. Built HTML SHA-256: `fb0d553fcea497e802e171e2623180de02ccdcbf0c701f5efe6bd26e82473663`.
- ML partitions after the 14-day embargo: train 796 (75 positive), validation 140 (10 positive), test 180 (17 positive). Selected logistic regression by validation AP only. Held-out test AP `0.18754007883316906`, ROC-AUC `0.765427643450018`; F1/precision/recall `0` at threshold 0.5; confusion `[[163,0],[17,0]]`. These are measured educational results, not a claim of useful threshold performance.
- `artifacts/v15/spark-ml/`: actual secondary Spark ML 4.0.4 comparison completed in WSL. `artifacts/v15/connect-session/`: issued/extracted package executed true Connect (`isRemote=true`) → dbt → Gold reconciliation → sklearn → Evidence package in the same local session. Hosted V1.5 Colab and native BigQuery were not executed by this proof.
- `artifacts/v15/exported-sklearn/`: portable exported sklearn code executed independently. Four notebook exports passed notebook/code validation; hosted destinations remain unexecuted.
- 230 .NET tests passed, including all 216 previous tests. Python: 85 passed, 3 optional Google-client integration tests explicitly skipped. Legacy WPF editor/planner and V1.5 WPF flow smokes passed; latest V1.5 renders are in `artifacts/v15/wpf-release/`. Generated project schemas, resolved-plan schema, notebook/Python syntax and real `dbt parse` for Cosmos preparation passed. The Evidence report was also inspected in a browser.

Reproduce the core result from a fresh output location:

```powershell
dotnet test ContosoDGV2.sln
python -m venv .tools/v15
.tools/v15/Scripts/python.exe -m pip install -r DatabaseGenerator/Forge/Templates/v15/requirements.txt
dotnet run --project DatabaseGenerator --no-build -- forge generate --project examples/v15-local-ml.project.json --output out/v15-release-ml
.tools/v15/Scripts/python.exe out/v15-release-ml/pipeline/run_local.py --root out/v15-release-ml --run-id release
.tools/v15/Scripts/python.exe out/v15-release-ml/factory/build_evidence.py --state out/v15-release-ml/.forge/v15/release
.tools/v15/Scripts/python.exe scripts/test_v15.py --root out/v15-release-ml --state out/v15-release-ml/.forge/v15/release -v
```

Use `v15-local-bi.project.json` / `out/v15-release-bi` for the no-ML run. Node/npm is required for Evidence rendering. Raw evidence and generated dependencies remain untracked. `scripts/capture_v15_evidence.py` validates retained hashes before regenerating the ledger. Linux/WSL Spark commands, exports, WPF checks and explicit MotherDuck commands are in the V1.5 guide.

Remaining boundaries: MotherDuck and Gold-only Dive are generated/auth-gated and unexecuted; optional Cosmos/Airflow tasks were not run. Cloud/Minikube/IaC historical evidence below is preserved, not re-certified. Local modes replay the scenario's batch/CDC/SCD2/late/quality exercises; persistent incremental processing and prevalence/noise/causal controls are not implemented. Regression/KMeans/IsolationForest candidate factories exist, while the authored ML scenario is classification. BQML stays SQL export or explicitly billing-gated training. Fabric/Databricks remain exporters/consumers, Polars/Pandas engines and Hugging Face model demos remain deferred. New CI definitions were added but remote CI has not been run in this local task. All newly planned projects retain `not-executed`; only the evidenced local adapter is promoted from reference-only.

---

The following sections are historical handoffs; their status claims apply to their stated runs and dates.
# Contoso Forge V1.4.0 release handoff — 2026-09-05

V1.4.0 was published in `ec1ea2d6bb2e408480277ef11eef46333de00939`, following audited V1.3 baseline `407c1d250addb7b3f0cc3f9ce21f5ae676c2132d`. Offline C# Plan, separate business scenarios, explicit `free-gcp-connect`, capability/evidence scope and WPF Plan-before-Compile are implemented. Plan output remains opt-in. Local-only plans do not require Git; explicit time horizons require enough orders to cover every requested day.

The final release fix preserves all 152 audited default artifact hashes on Windows and Linux. The Airflow failure came from platform-dependent JSON newlines encoded inside Base64; both revisions matched within each environment. Overall implementation now follows `unsupported > reference-only > generated > runnable`, and offline plans remain `currentExecutionStatus=not-executed`. Independent JSON/work-order contract versions are unchanged. See [the release-fix report](docs/v1.4-release-fix.md) for the byte comparison, focused tests and checks left to GitHub CI.

Release-fix validation: **88 focused .NET tests passed on Windows and 88 on Linux**, including default artifact compatibility, planner status combinations, legacy/opt-in compilation and Studio integration. The prior implementation audit recorded 203 .NET tests, 73 Python checks, 20 scenario/preset plans and Windows WPF rendering/CLI parity; its Spark, dbt, Airflow, Helm and IaC checks are retained as earlier evidence. Those runtime paths were not repeated for this release fix.

Public GitHub → GitSync → exact work-order adoption/reconciliation remains **pending publication of a reviewed generated fixture**. Hosted Colab, native BigQuery and Minikube results below remain historical evidence. No new cloud jobs, IaC apply or ML training were run. The semantic-intent bridge retains `models/semantic_model.json`.

Start with [planning and CLI usage](docs/planning.md) or [Pipeline Studio](ContosoForge.PipelineStudio/README.md). The earlier planning audit and local evidence remain under `artifacts/v131-plan/`; the release-fix comparison and test logs are under `artifacts/v140-release-fix/`. The local live project remains outside the Git index and protected by ignore/CI rules.

The V1.3/V1.2 handoff below is retained as historical context.

---
# Contoso Forge V1.3 handoff — 2026-09-05

**Current runtime status:** both generated classic and true Connect-local Forge notebooks succeeded in hosted Colab. Full classic/Sandbox work orders loaded and reconciled native BigQuery in `psychic-sun-415817.contoso_forge` (`US`), and the corrected native dbt run passed all 24 models and 121 tests with exact Gold KPI reconciliation. Returned warehouse results passed the C# evidence importer. Native ML feature SQL also executed successfully; no model has been trained. The V1.2 handoff is retained unchanged below as historical context.

This pass continues directly from audited HEAD `8bb31cb4d2d40f092daced753f1f7758d5fb3a6a` (`1.2`). V1 runtime templates, existing backends and tests remain available. The generator/source spec gain only an additive optional `generation.timeSpanDays` field (1–3650); omission preserves the original 60-day behavior, verified by 152 byte-identical default artifacts. The supplied generic hosted-Colab notebook is retained as an unchanged reference fixture; its RDD calculation proves classic Spark, not Connect or a generated Forge run.

## V1.3 implementation

- Existing C# architecture and neutral Pipeline Studio contracts gain optional Spark mode/version fields; the contract versions remain compatible with V1.2. `free-gcp-lab` remains a replaceable default preset.
- Colab classic uses an explicit native/pinned version policy, preserving compatible 4.0.4 installations. True `connect-local` uses public DataFrame/SQL APIs and verifies the actual remote session. `connect-remote` requires shared URIs and remains explicitly unsupported for the generated local-package transport.
- Work packages, Spark/BigQuery results and the C# evidence-import command bind exact runtime/API mode, source/package hashes, timestamps, counts and reconciliation. Spark-only work orders return evidence before warehouse authentication. Optional dbt/ML authored inputs travel in the package without automatic execution or prior run outputs.
- Minikube Airflow/GitSync has real work-order/result-return cycles, including an intentional manual checkpoint. It also adopted the already-issued hosted full work order and verified its actual Colab/BigQuery result. Execution exposed and fixed the chart migration-hook wait problem and scheduler selection.
- WPF Pipeline Studio MVP edits the existing contracts, validates dependencies/credentials, saves projects/pipelines and previews compiler output. The final Windows render/edit/compile smoke and all eight independent checks passed, including pending edits, simultaneous panel edits, malformed contracts and companion-file preservation.
- Separate dbt-bigquery Gold and explicit BigQuery ML adapters preserve V1 DuckDB/Spark ML routes. Gold now has successful native execution evidence and exact KPI reconciliation. ML requires measured Gold, chronological label windows and sufficient classes; native feature SQL is verified, while model training remains unvalidated.

## Current execution evidence

| Check | Observed result |
| --- | --- |
| .NET regressions | 161 passed, 0 failed/skipped; latest TRX retained |
| Python suites | 69 passing checks across runtime, pipeline, evidence, Spark and analytics suites (20 + 9 + 16 + 16 + 8), including native numeric accepted-values regression |
| Final deterministic generation | `out/v1.3-gold-fixed-verified-a` and `out/v1.3-gold-fixed-verified-b`: all 152 files byte-identical after the numeric accepted-values fix, before work-order issuance |
| Latest generated schema/notebook/syntax | Passed: 21 JSON files, 15 Python files, 1 notebook; notebook code cells also parse as Python |
| Classic Spark 4.0.4 | Actual generated Forge Bronze/Silver passed locally; `isRemote=false`; strict result creation/import passed |
| Spark Connect local 4.0.4 | Actual DataFrame/Window/dedup/Parquet and generated Forge Bronze/Silver passed locally; `isRemote=true`; strict result creation/import passed |
| Minikube/Airflow/GitSync | Actual pods Ready, generated DAG parsed, real local Git checkout; all four tasks succeeded for both the original local Spark cycle and adoption/return of the already-issued hosted Spark/BigQuery order |
| dbt-bigquery 1.10.3 / dbt-core 1.10.15 | Latest isolated offline parse passed: 24 models, 121 tests, 13 sources |
| GoogleSQL Gold offline execution | All 24 models translated to DuckDB over real Spark Silver; all five exact KPIs matched; two singular grain/SCD2 tests returned zero failures |
| WPF | Final Windows render/edit/save/compile smoke passed; independent review passed all 8 checks |
| Hosted generated Colab classic | Actual generated Forge run succeeded in [the hosted notebook](https://colab.research.google.com/drive/1QKuVNgZN2NbgDfOugkvsktHHHyLMEawn); byte-exact result imported as `validated-user-runtime` |
| Hosted Colab Connect local | Actual generated Forge run succeeded in [the Connect notebook](https://colab.research.google.com/drive/1uVwCR27C5m7Da3Dw5ReYbcSIt-HDObuL); requested/actual `connect-local`, `isRemote=true`, byte-exact result imported as `validated-user-runtime` |
| Native BigQuery Sandbox | Actual hosted full work order succeeded: 13 native Parquet loads, 14 count/KPI queries, 536 Silver rows and all five exact KPIs reconciled; imported as `validated-user-runtime` |
| Native dbt build | Corrected fresh order: 24 models succeeded, 121 tests passed, 0 failed/skipped; independent native Gold query matched all five truth KPIs |
| Native ML feature query | Actual GoogleSQL job succeeded against measured Gold; the 60-order fixture has insufficient validation/test classes. Model training was not run |

The actual local Spark environment was WSL Ubuntu, Python 3.10.12, OpenJDK 17.0.20 and PySpark/Spark 4.0.4. Both modes reconciled 11 Bronze and 13 Silver tables for the same 60-order fixture. The dataset fingerprint remains `9dffebc2987043f92c937e07dfee52eebb348d26d6e94156e7fa9514cb1d3609`. Local SQL checks observed 60 orders, 51284.73 gross sales, 0.610169 on-time delivery, 0.116667 return rate and 3.842105 average review rating. These local results do not establish native BigQuery execution.

Hosted classic ran with Python 3.13.15, OpenJDK 21.0.11 and PySpark/Spark 4.0.4 using the native version policy. Work order `2a731733-3053-4c6f-8737-e72f20800413` completed all 11 Bronze and 13 Silver tables, the DataFrame/Window/dedup/Parquet probes and truth reconciliation. Its returned manifest SHA-256 is `ce06811738a2c97280be934868e28c4776e1877ce306e703dd1db1b9f3690c52`. This is actual generated-pipeline proof beyond the original generic notebook fixture; its scope is Spark only, with no native BigQuery success claim.

Hosted Connect used the same Python 3.13.15 / OpenJDK 21.0.11 / Spark 4.0.4 versions. Work order `584e8490-4b0d-407f-8709-a82e71394104` completed from 22:37:31 to 22:38:57 UTC on 2026-09-04, with `pyspark.sql.connect.session.SparkSession`, requested/actual `connect-local`, `isRemote=true`, all four DataFrame probes, 11 Bronze tables, 13 Silver tables and truth reconciliation. The returned manifest SHA-256 is `96ad0e31022eee374f679f05e66c547bb950758b42d5b4aec609229c61a8f1bb`; exact byte restoration and the original hash were independently checked before import. Both hosted orders are Spark-only. Both Spark-only sessions were released after preserving their outputs. The full BigQuery/Gold notebook remains connected at this handoff snapshot.

The Google Cloud console for the confirmed project visibly displayed the `sandbox` heading and a banner inviting a billing upgrade, recorded in `artifacts/v1.3-hosted-colab/bigquery-sandbox-observation.json`. Billing settings were not changed and the billing-account API was not inspected. Full work order `6384c757-68a6-46e2-a064-9fa0f303e383` completed in [a separate hosted notebook](https://colab.research.google.com/drive/1QNnAUpNCWpOVCFpbolELdCJU2E-U4lcw). Hosted authentication worked; all 27 native BigQuery jobs finished `DONE` with no errors. Thirteen loaded tables contain 536 Silver rows; fourteen count/KPI queries independently reconciled the same five truth values. The returned manifest SHA-256 is `f9f4856be6e1c85d3495c67932cf8248c7f34035e0fab3cbac83e80b87f84df4`. Query evidence reports 7,416 processed bytes and 60 MiB in the billed-byte metric, which is a service usage metric rather than proof of a monetary charge. Local ADC refresh still fails, but hosted authentication and native execution are proven.

The first native dbt attempt exposed two numeric accepted-values tests comparing INT64 values to quoted strings: 109 nodes passed, 2 errored and 34 were skipped. The scoped `quote: false` correction and regression were followed by fresh work order `9ca5fc66-829a-4d18-bc29-8c8d919e339f`, including a new Spark run and native load. All 24 dbt models then succeeded and all 121 tests passed, with no failures/skips. Its independent Gold reconciliation job matched all five truth KPIs. The exact returned Gold SHA-256 is `2b56de035ab24b562b0dda7879d99501b76741f86d6b18dc1c98d5c234107748`; its bound warehouse result is `132923b9a221932a1de35f83a08d49d55ec95224edfd4be09612956822f466e9` and passed the C# importer. The initial proof remains preserved.

The native ML feature query against that Gold completed `DONE` with no errors. With label cutoff 2025-01-01, it observed train 21 negative/5 positive, no validation rows and test 9 negative/0 positive. The readiness guard therefore reports insufficient partitions; no `CREATE MODEL` ran.

Minikube used the real official Airflow chart and a read-only local Git server. After the first local Spark cycle, it adopted the already-issued hosted full work order, reached its manual checkpoint and reconciled the exact returned Colab/BigQuery manifest; all four tasks succeeded. Airflow did not originally issue that hosted order, and this does not prove GitHub publication. The tiny sample has insufficient class/split support after the ML label embargo; the ML runner correctly refuses training instead of inventing scores.

The additive `examples/free-gcp-bqml.project.json` produces 1,200 orders over 365 days. Real local Spark and all 24 translated Gold models passed, with all five truth KPIs reconciled. Using the unchanged feature SQL, label cutoff 2025-02-01 and 14-day embargo, its measured negative/positive counts are train 721/75, validation 127/9 and test 163/17. Both temporal boundaries and label maturity passed. This establishes viable splits for the larger fixture without claiming native ML training.

Evidence locations:

- `artifacts/v1.3-spark/final-*-spark-runtime.json`, `final-*-spark-result-manifest.json`, `final-*-imported-evidence.json` and execution logs.
- `artifacts/v1.3-spark/gold-fixed-generation-hashes.json` for the latest 152-file deterministic comparison and static validation after the numeric accepted-values fix; earlier snapshots are retained.
- `artifacts/v1.3-hosted-colab/{classic,connect}-result-manifest.json` and corresponding `*-imported-evidence.json` for both successful generated hosted runs; `connect-transport.json` records exact returned-byte verification.
- `artifacts/v1.3-hosted-colab/bigquery-result-manifest.json` and `bigquery-imported-evidence.json` for the successful native Sandbox run; [BigQuery ML capability research](docs/bigquery-ml-sandbox-boundaries.md) records current official-source limits separately from execution proof.
- `artifacts/v1.3-hosted-colab/gold-evidence.json`, `gold-fixed-bigquery-result-manifest.json`, `gold-fixed-bigquery-imported-evidence.json` and `gold-fixed-transport.json` for the corrected native Gold run; `bqml-readiness-evidence.json` for the actual native feature query.
- `artifacts/v1.3-minikube-runtime.json` and its bound Spark result; details in [the Minikube execution note](docs/v1.3-minikube-live.md).
- `artifacts/v1.3-minikube-hosted-{runtime,checkpoint,adoption}.json` for Airflow's adoption and reconciliation of the already-issued hosted Spark/BigQuery work order.
- `artifacts/v1.3-final-dbt-parse/parse-summary.json` and `artifacts/v1.3-spark/offline-bigquery-gold-report.json`.
- `artifacts/v1.3-generation-span/compatibility-report.json` for unchanged default output, and `offline-ml-viability-report.json` plus `local-*` logs/evidence for the larger fixture's real Spark and offline ML split checks.
- `artifacts/test-results/v1.3-generation-span/julia_JULP_2026-09-05_01_11_07.trx` for 161 passing .NET tests; earlier TRX files remain preserved.
- `artifacts/v1.3-wpf-review-fixes4/smoke-report.json` and its `pipeline-studio.png` screenshot; `artifacts/v1.3-studio-independent-review.json` for the eight independent checks.
- [Machine-readable V1.3 status](docs/v1.3-handoff.json) and [Spark modes/run commands](docs/colab-spark-modes.md).

Do not regenerate an output directory after its work order has been issued. Preserve each generated project and its returned result together. Native BigQuery, dbt build and ML may only be marked validated after their own real execution evidence is returned and reconciled. No cloud infrastructure apply or model-training success is claimed.

---

# Contoso Forge V1.2 handoff — 2026-09-04

Implemented the requested preset/compiler/free-GCP slice on baseline `ea03fc6ed39693cf9bdd7444fa6b052abfcc1009`. The supplied `CODEX_NEXT_VERSION_PROMPT.md` was read first, then `CODEX_MASTER_PROMPT.md`. Their planning notes were reconciled with the actual checkout and the user's explicit priorities; no historical roadmap item was treated as permission to replace existing code.

## Preserved baseline

All 48 original .NET tests passed before implementation and remain unchanged. The V1 `ProjectSpec`, generator, deterministic business data, truth manifest, source templates, Spark/Docker/dbt/Airflow lab, kind/OpenTofu path, SQL Server scripts, and legacy positional CLI remain available. There was no implemented V1.1 dispatcher in this checkout. New `version: "1.2.0"` projects embed the unchanged V1 source spec instead of changing its semantics.

Existing files changed: `ForgeCommand.cs` adds dispatch; `ForgeIo.cs` and the project file exclude Python bytecode caches from template copying; `README.md` links the new workflow. Existing tests and runtime templates were not weakened or rewritten. The remaining implementation is in new files.

## Delivered

- Editable architecture registry with `free-gcp-lab` default, separate Sandbox/billing-enabled profiles, deterministic resolution, and independent engine/runtime/storage/file/table/warehouse/IaC settings.
- Neutral C# pipeline contracts, source-generated JSON, schema, graph/parameter/reference validation, and validation of inherited runtime settings before generation changes any files.
- `forge presets list`, `forge project init`, `forge validate`, and `forge pipeline compile`; V1 generation stays unchanged unless the new envelope or explicit preset is selected.
- Deterministic canonical pipeline, sequential plan/runner, Airflow 3 compiler, architecture summary, file-hash run manifest, and staged compilation with stale compiled-output cleanup.
- Native BigQuery CSV/JSONL/Avro/ORC/Parquet load adapter with job waits, deterministic retry identity, WRITE_EMPTY, query byte limits, and actual count/KPI queries.
- Generated Colab notebook, versioned work-order/result schemas, uploadable package, verified source/runtime hashes, run isolation, expiry, manual checkpoint, and strict result reconciliation.
- Official Airflow Helm 1.22.0 / Airflow 3.2.2 Minikube LocalExecutor/GitSync profile, writable persistent work-order state, generated local secret bootstrap, and safe port-forward instructions. The retained V1 Docker DAG is ignored only in this new Minikube profile.
- Shared Google-provider 7.45.0 HCL for BigQuery dataset/tables/IAM and opt-in GCS. OpenTofu defaults; Terraform Community and isolated dual validation are available. No data transformation is run by IaC.
- Separate finite GitHub Actions lane for regression tests, deterministic generation, schema/notebook/Python checks, runtime contract tests, dual HCL validation, Helm rendering, and real Airflow DAG parsing on Linux.

## Evidence

| Check | Result |
| --- | --- |
| Baseline .NET tests | 48 passed |
| Final .NET tests | 121 passed, 0 failed/skipped |
| BigQuery/Colab Python tests | 20 passed, including actual client configuration, translated SQL against existing Silver files, and a local package-to-result round trip |
| Generated pipeline runtime tests | 7 passed; actual subprocess packaging/resume, pending exit 75, and strict synthetic result verification |
| Default CLI generation | Passed; source fingerprint `9dffebc2987043f92c937e07dfee52eebb348d26d6e94156e7fa9514cb1d3609` |
| Repeated generation | All generated files byte-identical |
| JSON/notebook/Python validation | Passed: 20 JSON files, 1 notebook, 10 Python files |
| OpenTofu 1.12.6 | fmt/init/validate passed in an isolated temporary directory |
| Terraform 1.13.4 | fmt/init/validate passed independently on the same HCL |
| Helm 3.19.0 | Official pinned chart lint/render passed, Airflow 3 image/GitSync present |
| Existing Compose configuration | Passed |

Raw local reports: `artifacts/test-results/baseline/baseline.trx`, `artifacts/test-results/final/final.trx`, `artifacts/final-infrastructure-validation.json`. The tracked summary is `docs/free-gcp-handoff.json`. SDK used: .NET 9.0.101 targeting net8.0; Python 3.13.1. Offline integration packages: google-cloud-bigquery 3.44.0, DuckDB 1.5.5, PyArrow 19.0.1, SQLGlot 30.18.0.

Measured existing Silver business results match truth: 60 orders, 51284.73 gross sales, 0.610169 on-time delivery, 0.116667 return rate, and 3.842105 average review rating. This test translates GoogleSQL to DuckDB and uses real local Parquet; it is **not a live BigQuery execution claim**.

## Reproduce

```powershell
dotnet test ContosoDGV2.sln --configuration Release
dotnet run --project DatabaseGenerator -- forge generate --project examples/free-gcp-lab.project.json --output out/free-gcp
python scripts/test_free_gcp_runtime.py
python scripts/test_pipeline_runtime.py --project out/free-gcp
python scripts/validate_studio_artifacts.py --project out/free-gcp
python scripts/validate_free_gcp_infra.py --project out/free-gcp --iac dual-validate --require-tools
```

The schema validator needs `jsonschema==4.23.0 nbformat==5.10.4`. The infrastructure validator needs OpenTofu, Terraform and Helm on PATH, or explicit `--tofu`, `--terraform`, `--helm` paths. The 17 standard-library BigQuery tests run without cloud packages. To enable the three extra integration tests, install the versions above plus jsonschema and set `FORGE_TEST_GENERATED_ROOT=out/free-gcp` and `FORGE_TEST_SILVER_ROOT=lake/silver` to matching generated truth and existing validated V1 Silver data.

See `docs/free-gcp-lab.md` and the generated `gcp/FREE_GCP_README.md`, `pipeline/COMPILER.md`, and `minikube/README.md` for the interactive run.

## Explicit limits and next execution gate

No cloud infrastructure was applied. Hosted Colab, live BigQuery, and an installed Minikube Airflow run remain unverified; their artifacts retain `experimental` or `generated-reference`. Actual Airflow DagBag parsing is configured in Linux CI and has not been run on this Windows host. Docker's Linux engine is unavailable, so the existing Docker Spark runtime was not rerun; its files and tests remain preserved.

This pass delivers Pipeline Studio contracts/CLI, not the graphical WPF editor. Azure/Fabric/Databricks/SQL Server/R2 and other selections are neutral authoring contracts with explicit unsupported execution mappings where adapters are absent. BigLake, dbt-bigquery Gold, BigQuery ML, and other historical roadmap additions are not presented as completed implementations.

Next execution gate: configure a real project and sample Git repository, run the generated Colab notebook and BigQuery reconciliation, then install the rendered Minikube profile and return a real run-scoped result. Preserve that evidence before labeling cloud/runtime artifacts validated.
