# PySpark Bronze/Silver

Artifact status: **validated** for the pinned Docker reference stack.

`bronze_silver.py` reads Forge CSV from `lake/raw`, writes Bronze as Delta Lake,
then applies duplicate removal, CDC ordering, late-arrival flags, SCD2 history,
and quality quarantine before writing Silver as Parquet for dbt-duckdb.

The source schemas also document the legacy normalization boundary: original
Contoso CSV names are not rewritten at generation time.
