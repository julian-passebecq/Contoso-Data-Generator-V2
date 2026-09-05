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
