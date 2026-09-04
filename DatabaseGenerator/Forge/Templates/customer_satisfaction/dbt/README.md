# Contoso Forge dbt project

Artifact status: **validated**

This generated dbt Core project uses `dbt-duckdb`. It registers views over Spark's
portable Silver Parquet directories at `/workspace/lake/silver`, builds the Gold
star in the `gold` schema, and stores the local DuckDB database at
`/workspace/lake/gold/contoso_forge.duckdb`.

The project name is `__PROJECT_NAME__` and its scenario is `__SCENARIO__`.

From the repository's dbt container, run:

```sh
dbt deps --project-dir /workspace/out/dbt --profiles-dir /workspace/out/dbt
dbt build --project-dir /workspace/out/dbt --profiles-dir /workspace/out/dbt --target local
```

`FORGE_LAKE_ROOT`, `FORGE_DUCKDB_PATH`, and `FORGE_TRUTH_MANIFEST` may override
the defaults. Spark must complete Silver first. The `on-run-start` hook fails
fast when a required Parquet directory is absent, which makes orchestration
errors visible instead of silently creating empty Gold models.

The tests cover source keys and accepted values, Gold keys and relationships,
the expected `__EXPECTED_ORDER_COUNT__` orders, and all five KPI values in
`truth_manifest.json`.

