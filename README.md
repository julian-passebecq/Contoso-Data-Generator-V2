# Contoso Data Generator V2

Contoso Forge V1.4.0 adds an offline C# architecture planner: choose a business scenario and preset, inspect the resolved DAG, manual checkpoints and evidence, then compile. The explicit free-gcp-connect preset preserves free-gcp-lab classic behavior. See [planning and CLI usage](docs/planning.md) and [Pipeline Studio](ContosoForge.PipelineStudio/README.md).



DataGenerator is a tool for generating sample data, ready to be imported into PowerBI or Fabric OneLake for analysis. This is the V2 version, evolution of the [older one](https://github.com/sql-bi/Contoso-Data-Generator).

## Contoso Forge architecture presets

New Studio projects default to `free-gcp-lab`: GitHub + Minikube/Airflow 3/Helm/GitSync + interactive Colab Spark + BigQuery + OpenTofu. Presets are editable configuration for the neutral C# `project.json` / `pipeline.json` contracts. Engine, runtime, storage, format, warehouse, and IaC remain separate choices. BigQuery Sandbox and billing-enabled free usage have distinct capability gates.

See [the preset and compiler guide](docs/free-gcp-lab.md) for initialization, compilation, Colab work orders, reconciliation, and validation limits. The original V1 commands and backends below remain available without modification.

V1.3 introduced an optional [WPF Pipeline Studio](ContosoForge.PipelineStudio/README.md), separately verified [classic and Spark Connect modes](docs/colab-spark-modes.md), strict returned-runtime evidence, and generated dbt-bigquery/BigQuery ML adapters. Hosted Colab, native BigQuery Sandbox and the corrected native dbt run have successful execution evidence: 24 models and 121 tests passed with exact Gold KPI reconciliation. The [live Minikube report](docs/v1.3-minikube-live.md) records real GitSync and returned-result reconciliation. Native ML feature SQL works; model training remains unvalidated. See [the current handoff](HANDOFF.md) for exact results and remaining work.

For a larger ML-ready sample, generate `examples/free-gcp-bqml.project.json`. Its optional `generation.timeSpanDays: 365` produces 1,200 orders with viable chronological splits after the 14-day embargo; omitting the field preserves the original 60-day horizon. Configure its project/dataset before issuing a cloud work order:

```powershell
dotnet run --project DatabaseGenerator -- forge generate --project examples/free-gcp-bqml.project.json --output out/my-bqml-run
```

## Contoso Forge V1

Contoso Forge is an additive, deterministic project-generation workflow in the existing C#/.NET codebase. The upstream engine and legacy positional CLI remain intact and keep their CSV, Parquet, Delta-oriented, distribution, seasonality, and spike contracts; Forge adds a separate ProjectSpec-driven workflow around them. V1's first vertical slice generates a Customer Satisfaction scenario with `Shipment`, `ShipmentEvent`, `Return`, `SupportTicket`, and `Review` data, plus reproducible duplicate, CDC, late-arrival, SCD2, and quality-rule cases.

The reference path is:

```text
Contoso Forge -> lake/raw -> Spark Delta Bronze -> Spark Parquet Silver
              -> dbt-duckdb Gold -> dbt tests -> Airflow 3
```

Docker is the canonical V1 runtime. It requires no cloud account or credentials. Kafka, production Kubernetes, and full Airflow-on-Kubernetes are intentionally outside V1.

### Local quick start

Prerequisites are Docker Desktop/Engine with Compose v2 and Python 3. PowerShell and POSIX-shell wrappers are provided; both delegate to the same Python command surface. The first build downloads pinned images and packages.

```powershell
.\lab.ps1 smoke
```

```bash
sh ./lab.sh smoke
```

The smoke command builds the images, generates the sample project, checks Spark itself, runs Bronze/Silver, builds and tests Gold, starts Airflow, and runs the generated DAG. Airflow is then available at <http://localhost:8080> with the local-only credentials `admin` / `admin`.

Useful incremental commands:

```text
lab prepare
lab build
lab generate
lab run-spark --stage smoke|bronze|silver|all
lab run-dbt
lab up-airflow
lab run-pipeline
lab validate
lab down
```

On Windows use `.\lab.ps1 <command>`; on Linux, macOS, and Codespaces use `sh ./lab.sh <command>`. Both wrappers call the same [`scripts/lab.py`](scripts/lab.py), [`compose.yaml`](compose.yaml), images, generated project, tests, and `lake/raw|bronze|silver|gold` layout. `lab down` removes the running containers and network but preserves the bind-backed workspace and named database/log volumes.

Generated data and code land in `out/`; runtime lake files land in `lake/`. The generated `out/truth_manifest.json` records source hashes, deterministic evidence, expected Silver row counts, and expected KPIs. The checked-in small project is [`examples/customer-satisfaction.project.json`](examples/customer-satisfaction.project.json); the JSON contracts are in [`schemas/`](schemas/).

The Airflow container mounts the local Docker socket so its generated DAG can launch the same short-lived Forge, Spark, and dbt images. This is a development-only trust boundary. Do not expose this lab as an always-on server or reuse its sample password outside a disposable local/Codespaces environment.

See [`docs/codespaces.md`](docs/codespaces.md) for the same-stack Codespaces flow and [`docs/kubernetes.md`](docs/kubernetes.md) for the deliberately small V1C kind/OpenTofu target.

If you are just interested in **ready to use sets of data** , [download them here.](https://github.com/sql-bi/Contoso-Data-Generator-V2-Data)

Supported output formats:
 - Parquet
 - Delta Table (files)
 - CSV
 - CSV multi file
 - CSV multi file - gz compressed
 - Sql Server, via bulk insert script of the generated CSV files


Delta Table output can be directly used in Fabric LakeHouse without any conversion:

<img src="docs/imgs/fabric_01.png" width="700px"/><br/><br/>

Data schema:

|  |  |
| -- | -- |
| ![Schema Sales](docs/imgs/schema_sales.svg) | ![Schema Sales](docs/imgs/schema_orders.svg)  |


## Usage overview

<br/> 

**FULL DOCUMENTATION** available here: **[&#x21D2; &#x21D2; https://docs.sqlbi.com/contoso-data-generator/ &#x21D0; &#x21D0;](https://docs.sqlbi.com/contoso-data-generator/)**

<br/> 

DataGenerator requires four mandatory elements to run:
 - a configuration file (json)
 - a data file (excel)
 - an output folder
 - a cache folder
 - [optional parameters]

```
databasegenerator.exe  configfile  datafile  outputfolder  cachefolder   [param:AAAAA=nnnn] [param:BBBBB=mmmm]
```
Example:

```
databasegenerator.exe  c:\temp\config.json  c:\temp\data.xlsx  c:\temp\OUT\  c:\temp\CACHE\
```

**Note**: the tool needs some files containing static data: fake customers, exchange rates, postal codes, etc. The files are cached after been downloaded over the Internet from a specific SQLBI repository.
 
 <br/>
 <br/>
 <br/>
