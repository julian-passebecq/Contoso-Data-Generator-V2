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
