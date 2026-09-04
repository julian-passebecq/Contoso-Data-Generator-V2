# Architecture presets and the free GCP lab

`free-gcp-lab` is the default **new Studio project** preset. The existing V1 project and positional CLI still use their original Spark/Docker/dbt/DuckDB path. Selecting an architecture never changes the C# business generator or its truth manifest.

New Studio projects use a `version: "1.2.0"` envelope containing `sourceProject` (the unchanged V1 spec), `architecture`, and nonsecret `gcp` and `git` options. There is no speculative V1.1 parser: this checkout contained only the V1 implementation. Existing V1 JSON remains strict and valid without modification.

## Start and generate

Run commands from the repository root. Prerequisite: .NET 8 SDK.

```powershell
dotnet run --project DatabaseGenerator -- forge presets list
dotnet run --project DatabaseGenerator -- forge project init --output artifacts/my-project
dotnet run --project DatabaseGenerator -- forge validate --project artifacts/my-project/project.json
dotnet run --project DatabaseGenerator -- forge generate --project artifacts/my-project/project.json --output out/free-gcp
```

`project init` creates `project.json` and `pipeline.json`. Edit the project GCP ID and Git repository before running cloud or GitSync work. No credentials are needed to generate or compile artifacts. Pipeline edits are picked up from the `pipeline.json` beside the input project, or explicitly with `--pipeline`.

The supplied smaller example is also ready for generation:

```powershell
dotnet run --project DatabaseGenerator -- forge generate --project examples/free-gcp-lab.project.json --output out/free-gcp
```

Compile contracts/artifacts without generating data:

```powershell
dotnet run --project DatabaseGenerator -- forge pipeline compile --project examples/free-gcp-lab.project.json --output artifacts/compiled
```

Compilation needs an empty output directory or a previous Forge-owned output. Its outputs include canonical `pipeline.json`, `resolved_project.json`, `local_plan.json`, an Airflow DAG, runtime helpers, Minikube values, infrastructure, and a deterministic `run_manifest.json` of generated file hashes. A compile-only directory must be supplied generated source data and truth before a work order can be issued.

## Edit the architecture

Engine, runtime, orchestrator, storage, file format, table format, warehouse, cost profile, and IaC are separate fields. `architecture.overrides` changes only the fields provided:

```json
{
  "presetId": "free-gcp-lab",
  "overrides": {
    "storage": "azure-adls",
    "warehouse": "none",
    "runtime": "docker",
    "costProfile": "external"
  }
}
```

See `examples/azure-adls-airflow.project.json`. Other preset contracts include `local-spark`, `local-fast`, `databricks-free`, `fabric-lakehouse`, `sqlserver-bi`, and `open-lakehouse-iceberg`. The registry describes editable intent. It does **not** assert executable parity for every connector. Unsupported activity/runtime mappings are recorded by the compiler and stop execution rather than returning success. The existing V1 Spark reference continues through the existing `lab` commands.

The neutral C# contract lives in `DatabaseGenerator/Forge/Pipeline`. It provides activities, graph dependencies, datasets, typed parameters, connection references, retries/timeouts, validation, and compilers. JSON is canonical; generated Python is disposable. The optional [WPF Pipeline Studio](../ContosoForge.PipelineStudio/README.md) edits these same contracts and previews the actual compiler outputs.

## Two distinct cost profiles

| Profile | Default storage | Capabilities |
| --- | --- | --- |
| `gcp-sandbox-no-card` | local files | Interactive Colab, native BigQuery batch loads, manual result handoff; no GCS, BigLake, Workflows, or paid Airflow dependency |
| `gcp-free-tier-billing-enabled` | optional GCS | Normal GCP billing project with opt-in infrastructure; free allowances do not guarantee zero charges |

The native loader handles CSV, JSONL, Avro, ORC, and Parquet. Delta and Iceberg are table formats and cannot be selected as native BigQuery batch files. BigLake/open-table integrations remain separate reference work.

Query jobs enforce `gcp.maximumBytesBilled`. Authentication uses application default credentials at runtime; connection JSON holds references rather than credential values. IaC defaults to OpenTofu. `none`, `terraform-community`, and `dual-validate` are supported choices. IaC provisions resources, not data transformations; the no-card flow does not require applying it.

## Interactive execution

The generated `colab` and `gcp` instructions describe packaging, notebook execution, and verification. The flow is:

1. Generate C# sources and the truth manifest locally.
2. Issue a unique work order and package for the current run.
3. Upload the package to Colab and run the generated Spark notebook interactively.
4. Load native BigQuery tables and query the actual counts/KPIs.
5. Return the result manifest and reconcile it against the issued work order and original truth.

The manual checkpoint must receive a matching completed result. Missing, stale, incomplete, or mismatched results do not unlock downstream success. Local sequential execution uses the same boundary. Colab is not an unattended Airflow worker.

V1.3 adds separately recorded classic and true local Spark Connect modes with `colab-native` version detection; the current tested target is PySpark 4.0.4. [Spark modes and evidence import](colab-spark-modes.md) explains the optional settings and returned-result workflow. An explicit `--scope spark` package can prove Spark before Google authentication. It cannot complete a full BigQuery work order. Native BigQuery results can then feed the separately generated `dbt_bigquery/` Gold adapter and explicit `bqml/` training commands.

## Minikube and validation

Follow the generated `minikube/README.md` to bootstrap Minikube, create local secrets, and install the pinned official Airflow chart. GitSync pulls the generated DAG source from the configured repository/branch/subpath. Commit the generated project artifacts at the configured repository path before expecting the DAG to run. GitHub hosts source/CI; Minikube runs Airflow. Access the UI using a localhost port-forward.

The existing Docker Compose and kind/OpenTofu paths remain in `compose.yaml`, `scripts/lab.py`, `scripts/v1c.py`, and `infra/`. The new profile does not launch a local Spark service.

The infrastructure validator records separate CLI versions and results for OpenTofu, Terraform, and Helm. A successful format/schema/render check proves only that check. Generated templates retain `generated-reference` or `experimental`; validated executions are recorded in their returned per-run evidence.

The [V1.3 live Minikube report](v1.3-minikube-live.md) records actual GitSync and Airflow, including adoption/reconciliation of an already-issued hosted full work order. Its Git server was local. Both generated classic and true Connect-local notebooks have successfully imported hosted Colab results; see [Spark execution evidence](colab-spark-modes.md). Native Sandbox loading reconciled all 13 Silver tables and five truth KPIs; the corrected native dbt run passed all 24 models and 121 tests. Native ML feature SQL also passed, while model training and GitHub publication remain unvalidated.

The separate `examples/free-gcp-bqml.project.json` uses 1,200 orders and optional `generation.timeSpanDays: 365`. Local Spark and exact generated feature SQL produced both classes in every split after the unchanged 14-day embargo. Use a label observation cutoff of `2025-02-01T00:00:00Z` for that fixture. This establishes sample viability, not a trained model or a free-training guarantee; see [the ML boundaries](bigquery-ml-sandbox-boundaries.md).
