# BigQuery Gold

This separate dbt-bigquery project ports the validated V1 grains and SCD2 joins to GoogleSQL. The original `dbt/` DuckDB project remains available. Silver sources and Gold views share the issued work order's unique table prefix; Gold uses views and avoids Sandbox-unsupported incremental DML. There are no service-account key fields.

After the native BigQuery load and truth reconciliation, from the generated project:

```sh
python -m pip install -r dbt_bigquery/requirements.txt
python dbt_bigquery/run_dbt.py --root . --work-order colab/work_order.json --result colab/result_manifest.json
```

Locally use Application Default Credentials (`gcloud auth application-default login`); in Colab use the interactive Google authentication cell. The wrapper derives project, dataset, location, run prefix and bytes limit from the issued work order/configuration. It rejects a different destination and requires successful BigQuery load evidence before building Gold. It runs all 24 models, grain/relationship tests, and the five exact truth KPI comparisons. `gold_evidence.json` records the dbt results and hashes; parsing alone writes `parse_evidence.json` and never claims cloud execution.

For offline adapter parsing, export `FORGE_GCP_PROJECT`, `FORGE_BQ_DATASET`, `FORGE_BQ_PREFIX`, and optionally `FORGE_BQ_LOCATION`/`FORGE_BQ_MAXIMUM_BYTES_BILLED`, then run `dbt parse --project-dir dbt_bigquery --profiles-dir dbt_bigquery --no-partial-parse`. Parsing does not validate server SQL or authenticate.

See [dbt BigQuery authentication](https://docs.getdbt.com/docs/core/connect-data-platform/bigquery-setup) and [BigQuery configurations](https://docs.getdbt.com/reference/resource-configs/bigquery-configs). Runtime cloud validation remains pending until actual dbt build evidence is captured.
