# Native BigQuery and interactive Colab

These are additive V1.3 adapters on the existing V1.2 contracts. BigQuery artifacts are **generated-reference**;
the Colab runtime is **experimental** until actually executed in your account.
The original V1 Spark Docker and dbt/DuckDB artifacts remain available.

## Run a small dataset

1. Set `gcp.projectId`, `gcp.dataset`, `gcp.location`, and a positive
   `gcp.maximumBytesBilled` in the V1.2 project, then generate the project.
2. For `gcp-sandbox-no-card`, create the selected dataset in the BigQuery UI.
   There is no mandatory GCS bucket, card, service account key, streaming, or DML.
   Generated IaC is optional and does not promise Sandbox provisioning.
3. From the generated output directory, run:

   ```sh
   python colab/work_order.py verify-source --root .
   python colab/work_order.py package --root . --run-id demo-001
   ```

4. Open `colab/contoso_free_gcp.ipynb` in Google Colab. Run its cells, upload
   `colab/work_package.zip`, sign in, and download `result_manifest.json`.
5. Save the result in the matching run's state directory and reconcile:

   ```sh
   python colab/work_order.py reconcile --root . --work-order colab/work_order.json --result colab/result_manifest.json
   python colab/work_order.py import-evidence --root . --work-order colab/work_order.json --result colab/result_manifest.json --output runs/demo-001/evidence.json
   ```

Airflow uses `package --work-order <run-state>/work_order.json --package
<run-state>/work_package.zip`; return the manifest to that run's requested path.
The uploaded work order stays at `colab/work_order.json` inside the ZIP.
An explicit run ID owns an issued work order for 24 hours by default (configurable
1–168 hours). A scheduler retry reuses that identity. A new run needs a new state
directory, for example `--work-order runs/demo-002/work_order.json --package
runs/demo-002/work_package.zip`. Expired orders fail instead of accepting stale
results during execution. The evidence importer permits later archival import only when recorded completion was within the issued window. Compiling alone emits an **unstarted template**, never a completed result.

To test Spark before Google sign-in, issue a separately scoped package with
`--scope spark`. The generated notebook returns `spark_result_manifest.json`
without attempting BigQuery. For a full work order the same intermediate Spark
result can be imported as partial evidence, but cannot complete its Airflow
checkpoint. The requested scope and requested/actual API modes are recorded.

The default classic mode detects Colab's installed PySpark before installing.
It preserves supported native 4.0.4 rather than downgrading it. Set the project
override `sparkApiMode` to `connect-local` for true local Spark Connect using
DataFrame/SQL only. `spark_config.json` carries the resolved request; the runtime
checks `is_remote()` and writes exact versions/session class and physical
Bronze/Silver fingerprints to `spark_runtime.json`. It never labels classic
fallback as successful Connect. Remote Connect requires shared object-store
paths; its transformation storage adapter remains explicitly unsupported.

The notebook runs existing V1 transformation functions, changing only Bronze IO
to Parquet for the `tableFormat:none` preset. It retains CDC deduplication, SCD2
event ordering, invalid shipment/review quarantine, and late-arrival handling.
The original V1 source file hashes and truth manifest stay unchanged.

## Native batch load CLI

```sh
pip install -r gcp/requirements.txt
gcloud auth application-default login
python gcp/bigquery_runtime.py load --file data.csv --format csv --table my-project.contoso_forge.my_table
```

`python gcp/bigquery_runtime.py preflight --config gcp/bigquery_config.json`
checks credentials and dataset location before upload. Add `--create-dataset`
only to explicitly create a missing dataset with a 60-day default expiration.
No billing account or GCS bucket is created. Completed jobs record native
destination, IDs, timestamps, location and processed/billed query bytes.

Supported `--format`: `csv`, `jsonl` (newline-delimited JSON), `avro`, `orc`,
`parquet`. CSV has a header and supports quoted newlines. CSV/JSONL can use
`--schema fields.json` containing BigQuery field-schema objects; otherwise the
service infers their schema. Avro/ORC/Parquet carry schemas. Delta and Iceberg are
table integrations, not native load-file formats, and are rejected here.

Each native load is `WRITE_EMPTY` with zero tolerated bad records, a stable job ID
derived from destination, file bytes, source format and schema, and an explicit
job completion wait. An interrupted retry recovers the same job; a different file
cannot silently overwrite existing data. The notebook writes one Parquet file per
table with the existing V1 `coalesce(1)` behavior. Multiple shards are rejected
rather than appended with ambiguous retry semantics. Each issued work order gets
unique destination tables; dataset expiration controls their lifecycle.

BigQuery results are measured from actual `COUNT(*)` queries and GoogleSQL business
facts in `reconcile_kpis.sql`. This SQL retains the V1 temporal customer joins and
fact grains for sales, shipments, returns and order counts. It is a runnable KPI
projection. The separate `dbt_bigquery/` project ports all 24 staging/Gold models;
its runner requires native load evidence and independently reconciles the five
Gold KPIs. The existing `dbt/` project is preserved. Optional `bqml/` commands
preview/train leakage-aware models only after measured BigQuery Gold, with an
explicit ML cost opt-in and chronological holdout sufficiency checks.
Results also contain observed Parquet counts and successful load/query job IDs.
Local reconciliation checks those results against every expected source/Silver
count and KPI, the work-order/run identity, destination, timestamps, source bytes,
and hashes of the exact execution package. Missing and mismatched results fail.
Offline verification is evidence consistency checking, not signed remote
attestation; execute packages and accept results only from trusted operators.
The versioned `colab/work_order.schema.json` and `colab/result_manifest.schema.json`
describe issued work orders and observed results. The unstarted template is not an
issued work order. Runtime checks additionally enforce matching identity, hashes,
destination, timestamps, complete table sets and finite KPI values.

## Cost and validation boundaries

- Sandbox and billing-enabled free usage are distinct. Sandbox restrictions and
  automatic table expiration apply. Small native loads work without GCS.
- `maximumBytesBilled` caps **each query**, not the sum of all queries or storage.
  `maximumLocalFileBytes` defaults to 100 MB per upload; notebook extraction is
  capped at 500 MB. These guards are teaching limits, not zero-billing guarantees.
- A billing-enabled project can incur charges. The configured cost profile does
  not enable billing, create cloud resources, or bypass provider restrictions.
- Local tests cover packaging, identity/freshness rejection, format dispatch,
  retry/job reuse and measured reconciliation with a fake client. They do not
  establish successful Colab execution or BigQuery permissions/quota availability.
- Changing the neutral contract's destination or runtime requires the matching
  adapter. This helper fails clearly for a non-BigQuery warehouse.

Repository test commands:

```sh
python scripts/test_free_gcp_runtime.py
# Optional: install into a dedicated test venv, and use an existing V1 Silver lake.
pip install duckdb==1.5.5 pyarrow==19.0.1 sqlglot==30.18.0 google-cloud-bigquery==3.44.0
FORGE_TEST_GENERATED_ROOT=out/free-gcp FORGE_TEST_SILVER_ROOT=lake/silver python scripts/test_free_gcp_runtime.py
```

The optional suite reads real Parquet, executes the GoogleSQL business projection
after dialect translation in DuckDB, constructs the real BigQuery client config,
and checks a complete package/load/query/result round trip with local transport.
It does not contact BigQuery or execute the Colab notebook.

Primary references checked 2026-09-04:
[native batch loading](https://docs.cloud.google.com/bigquery/docs/batch-loading-data),
[LoadJobConfig](https://docs.cloud.google.com/python/docs/reference/bigquery/latest/google.cloud.bigquery.job.LoadJobConfig),
[Sandbox](https://docs.cloud.google.com/bigquery/docs/sandbox),
[query cost controls](https://docs.cloud.google.com/bigquery/docs/best-practices-costs).
