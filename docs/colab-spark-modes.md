# Colab Spark modes and execution evidence

V1.3 keeps classic Spark and adds a separate local Spark Connect mode. Both use the
existing Forge Bronze/Silver business transformations. Docker's validated Spark
3.5.9 / Delta 3.3.3 templates remain unchanged. The Colab adapter writes Parquet
without Delta and does not inherit Docker's version pin.

| Requested mode | Session construction | Data location | Current evidence |
| --- | --- | --- | --- |
| `classic` | `SparkSession.builder.master("local[2]")` | Same VM as the driver | Generated Forge run passed locally and in hosted Colab on Spark 4.0.4; returned hosted result imported as `validated-user-runtime` |
| `connect-local` | `SparkSession.builder.remote("local[2]")` | Same VM as the local Connect server | Generated Forge run passed locally and in hosted Colab with `is_remote() == True`; returned hosted result imported as `validated-user-runtime` |
| `connect-remote` | Explicit `sc://host:port` endpoint | Shared object-store URIs | Session boundary only; generated Forge transport remains explicitly unsupported |

The user's unchanged notebook in `references/user_colab/pysparktestj.ipynb`
demonstrates hosted **classic** PySpark 4.0.4. Its use of SparkContext/RDD does not
prove Spark Connect or execution of the generated Forge pipeline.

## Configuration through the existing contracts

The Studio envelope remains `version: "1.2.0"` and the neutral pipeline remains
`contractVersion: "1.2"`. These optional fields extend the existing
`ArchitectureSettings` and `PipelineActivity` models:

```json
{
  "sparkApiMode": "classic",
  "sparkVersionPolicy": "colab-native",
  "sparkVersion": "4.0.4"
}
```

Set them in `architecture.overrides`, or on the one supported `colab-work-order`
activity. The compiler carries the effective values into the execution plan and
the exporter writes them into `colab/spark_config.json`. An activity override
therefore reaches the issued work package. `sparkRemote` is optional and valid
only for `connect-remote`; it must contain a credential-free `sc://host:port`
endpoint. Existing runtime aliases `google-colab-connect-local` and
`google-colab-connect-remote` imply their corresponding API modes.

Examples:

- `examples/free-gcp-classic-native.project.json`
- `examples/free-gcp-connect-local.project.json`

`colab-native` first inspects installed PySpark. Classic currently permits 3.5.9
and 4.0.4; Connect permits 4.0.4. A compatible installation is reused even if the
installation target is different. An unknown installed version stops with an
explicit error. A missing installation uses the requested compatible target.
Connect installs the version-matched `pyspark[connect]` dependencies.

`pinned` requires the exact requested version. If bootstrap would replace an
installed version, it prints the old/new versions and requires the explicit
`--allow-version-change` flag. The notebook exposes this as
`ALLOW_PINNED_VERSION_CHANGE = False`. No execution silently downgrades Spark or
falls back from Connect to classic.

Spark 4.0.4 requires Java 17 or later. Its official packaging supports a local
Connect server through `remote("local[...]")` with the Connect dependencies.
See [Spark 4.0.4 installation](https://spark.apache.org/docs/4.0.4/api/python/getting_started/install.html)
and [Spark Connect quickstart](https://spark.apache.org/docs/4.0.4/api/python/getting_started/quickstart_connect.html).

## Execute and return a generated notebook

From the repository, generate a new project into a fresh output directory:

```powershell
dotnet run --project DatabaseGenerator -- forge generate --project examples/free-gcp-classic-native.project.json --output out/my-classic-run
python out/my-classic-run/colab/work_order.py package --root out/my-classic-run --run-id classic-001 --scope spark
```

Use the Connect example and another output directory/run ID for `connect-local`.
Open the generated `colab/contoso_free_gcp.ipynb` in hosted Colab, then upload its
`colab/work_package.zip`. Run the cells in order. Bootstrap reads the hashed
configuration before installing dependencies; Spark executes in a fresh Python
subprocess so notebook session state cannot silently select a different mode.

The notebook downloads `spark_result_manifest.json` immediately after the Spark
gate. A `--scope spark` work order skips BigQuery authentication and loading.
For the complete architecture, configure a real BigQuery project and existing
dataset before generation and issue a new order with `--scope spark-and-bigquery`.
That flow then authenticates, loads native tables and returns the full result.
The Spark-only result cannot satisfy the warehouse checkpoint of a work order
issued for Spark and BigQuery.

Import the downloaded Spark result against its original generated project and
issued order:

```powershell
dotnet run --project DatabaseGenerator -- forge evidence import --root out/my-classic-run --work-order out/my-classic-run/colab/work_order.json --result downloaded/spark_result_manifest.json --output artifacts/my-classic-evidence.json
```

The equivalent Python command is `colab/work_order.py import-evidence` with the
same arguments. Preserve the project files after issuing an order: package
hashes bind the source, transform code, configuration and pipeline to the result.
Changing a mode, version policy, destination or source requires a new work order.

For a local reproduction in a Python environment with Java and Spark installed,
run these commands from the generated project:

```text
python colab/bootstrap_runtime.py --config colab/spark_config.json
python colab/run_spark.py --root . --lake-root lake --work-order colab/work_order.json
python colab/work_order.py spark-result --root . --work-order colab/work_order.json --runtime colab/spark_runtime.json --output colab/spark_result_manifest.json
```

`run_spark.run(root, lake_root, work_order)` retains its original three-argument
API. Optional CLI/API settings must match a V1.3 order's hashed configuration.
The default evidence output is `colab/spark_runtime.json`.

The repository also provides a Linux/CI smoke command. Supply a generated
free-GCP project and a separate new or empty output directory:

```sh
python scripts/run_colab_spark_smoke.py --project out/generated --output /tmp/forge-connect-proof --mode connect-local
```

Use `--mode classic` and another directory for the classic gate. This helper
copies only execution inputs, selects the requested mode in the isolated
configuration and neutral contract, issues a new Spark-only work order, executes
the real transform, creates the result and imports it. It keeps logs and
`evidence.json` in the output directory and never overwrites existing evidence.
It requires preinstalled Java/PySpark and does not install dependencies itself.

## What the execution evidence proves

The runtime records requested and actual API mode, session class, `isRemote`,
master/remote endpoint, exact Python/PySpark/Spark/Java versions, CPU/memory,
start/end time, package/source hashes, actual Bronze/Silver Parquet hashes and
counts, and truth reconciliation. Connect requires successful DataFrame,
Window, deduplication and Parquet round-trip probes. Neither the Connect adapter
nor the reused transformation bodies accesses SparkContext, RDD, `_jvm` or
`_jdf`. The untouched V1 session factory is not invoked by the Colab adapter.

Actual local validation used WSL Ubuntu, Python 3.10.12, OpenJDK 17.0.20 and
PySpark/Spark 4.0.4. Both modes processed the same 60-order fixture with source
fingerprint `9dffebc2987043f92c937e07dfee52eebb348d26d6e94156e7fa9514cb1d3609`.
All 11 Bronze and 13 Silver tables reconciled, including 32 customers, 115 order
rows, 59 valid shipments, 19 valid reviews, 34 SCD2 rows and 2 quality issues.

The classic run used `pyspark.sql.session.SparkSession`, `isRemote=false`; Connect
used `pyspark.sql.connect.session.SparkSession`, `isRemote=true`. Both result
manifests passed strict result creation, truth reconciliation and evidence import.
The local reports correctly retain `executionRuntime: "local-python"` and mark
only the corresponding mode `validated-local-runtime`.

Evidence in this workspace is under `artifacts/v1.3-spark/`: original issued
orders, runtime reports, result manifests, imported reports and both complete
execution logs. The generated projects are `out/v1.3-classic` and
`out/v1.3-connect`. These runtime directories are intentionally separate from
source fixtures. Their hash bindings must not be overwritten by regeneration.
Final rechecks after stricter configuration binding are recorded in
`final-classic-*` and `final-connect-local-*` evidence files. The latter also
exercised the reusable smoke command from package creation through import.

The generated classic notebook also completed a real hosted Colab run on Python
3.13.15, OpenJDK 21.0.11 and PySpark/Spark 4.0.4. It used `colab-native` and
`local[2]`, reported the classic session class with `isRemote=false`, and
reconciled every Bronze/Silver table plus all four DataFrame smoke checks. The
[hosted notebook](https://colab.research.google.com/drive/1QKuVNgZN2NbgDfOugkvsktHHHyLMEawn)
ran work order `2a731733-3053-4c6f-8737-e72f20800413` from 2026-09-04
22:23:43 UTC to 22:24:44 UTC. Its byte-exact returned manifest is
`artifacts/v1.3-hosted-colab/classic-result-manifest.json`, SHA-256
`ce06811738a2c97280be934868e28c4776e1877ce306e703dd1db1b9f3690c52`.
The C# importer accepted it as `validated-user-runtime`; see
`artifacts/v1.3-hosted-colab/classic-imported-evidence.json`.

The separately issued generated Connect notebook also completed in hosted Colab
on Python 3.13.15, OpenJDK 21.0.11 and PySpark/Spark 4.0.4. Work order
`584e8490-4b0d-407f-8709-a82e71394104` ran from 2026-09-04 22:37:31 UTC to
22:38:57 UTC in the [Connect notebook](https://colab.research.google.com/drive/1uVwCR27C5m7Da3Dw5ReYbcSIt-HDObuL).
Requested and actual mode were `connect-local`; the session was
`pyspark.sql.connect.session.SparkSession` with `isRemote=true`. All four
DataFrame/Window/dedup/Parquet checks and all 11 Bronze / 13 Silver table counts
passed truth reconciliation. The C# importer accepted the returned result as
`validated-user-runtime`.

Its byte-exact manifest is
`artifacts/v1.3-hosted-colab/connect-result-manifest.json`, SHA-256
`96ad0e31022eee374f679f05e66c547bb950758b42d5b4aec609229c61a8f1bb`.
The original notebook hash matches the restored bytes; the transport record is
`connect-transport.json` and the successful importer report is
`connect-imported-evidence.json` in the same directory. Both Spark-only sessions
were released after evidence preservation. The full BigQuery/Gold notebook
remains connected at this handoff snapshot. Colab sessions remain ephemeral.

The separate full work order `6384c757-68a6-46e2-a064-9fa0f303e383` completed
native BigQuery Sandbox loading and reconciliation in
`psychic-sun-415817.contoso_forge` (`US`): 13 Parquet loads and 14 count/KPI
queries all succeeded, and all five truth KPIs matched. Its returned manifest
`artifacts/v1.3-hosted-colab/bigquery-result-manifest.json` has SHA-256
`f9f4856be6e1c85d3495c67932cf8248c7f34035e0fab3cbac83e80b87f84df4`; the C#
importer recorded `bigquery-sandbox: validated-user-runtime`. A corrected fresh
full order also passed native dbt: 24 models, 121 tests and all five Gold KPIs.
Native ML feature SQL passed; model training remains unvalidated. The two earlier Spark-only orders
cannot independently satisfy a warehouse checkpoint.
JSON evidence is run-bound observational evidence, not
cryptographic attestation of a remote environment.

## Remote storage boundary

`connect-remote` never treats `/content`, a relative directory or a client-local
`file://` path as server-visible. The architecture rejects local storage for
this mode. The storage adapter recognizes shared `gs://`, `s3://`, `s3a://`,
`abfs://` and `abfss://` locations, then explicitly reports that the generated
Forge input/metadata transport for a remote server is still unsupported.
The compiler leaves this mapping unsupported; it does not produce an executable
local-file work order with a remote label. No remote-server execution is claimed.
