# ReactOracle handoff — minimal Contoso Forge generator

This branch is a handoff point for extracting a much smaller Contoso Forge derivative specifically for ReactOracle.

## Product boundary

ReactOracle owns:

- React / Fluent UI control plane
- architecture / provider map
- Airflow and Spark orchestration
- Kubernetes operations
- Grafana / Prometheus / Loki
- external providers such as Kaggle, Neon and Kafka
- BI and ML result visualization

The Contoso derivative should therefore be a **headless synthetic-data source generator**, not another platform UI.

## Preserve

Keep the pieces that make the current Forge implementation useful:

- deterministic C#/.NET generation with a stable seed
- Contoso-style relational source entities
- shipments, shipment events, returns, support tickets and reviews
- optional DE problems: duplicates, CDC, late arrivals, SCD2 and quality issues
- the current ML generation contract:
  - `profile=causal-v1`
  - `positiveOutcomeRate`
  - `signalStrength`
  - `noiseLevel`
- truth/provenance manifest and checksums
- schema/contracts
- CSV + Parquet output
- tests for determinism and causal-signal behavior
- Linux/ARM64-compatible headless execution if practical

## Reduce from the minimal derivative

Do not carry these unless they are required by generation itself:

- WPF / Pipeline Studio
- architecture-preset and planner complexity
- Minikube / local platform orchestration
- embedded Airflow execution
- OpenTofu / GCP infrastructure experiments
- report/BI UI
- MotherDuck and Hugging Face publication
- Colab runtime support
- full multi-engine parity work

The current V1.7 MLJAR/Kaggle implementation is useful reference material. If retained, keep only a narrow **ML package/export contract**. ReactOracle/Airflow should own remote execution.

## Desired minimal CLI

```text
contoso-forge generate --spec project.json --output ./out
contoso-forge validate --output ./out
```

Suggested output:

```text
out/
  source/
    customers.parquet
    products.parquet
    stores.parquet
    orders.parquet
    order_rows.parquet
    shipments.parquet
    returns.parquet
    support_tickets.parquet
    reviews.parquet
  manifest/
    generation.json
    truth.json
    schema.json
    checksums.json
```

The command result should expose:

- run ID
- status
- seed
- scenario
- row counts
- output locations
- dataset fingerprint
- ML ground truth settings
- validation result

## ReactOracle target flow

```text
ReactOracle Data Factory
        |
        v
Contoso Forge Lite
        |
        | Parquet + truth/provenance
        v
MotherDuck / DuckLake
  Raw / Bronze / Silver / Gold / Features
        |
        v
Airflow
        |
        v
Spark on Oracle K3s
        |
        | publish validated outputs back to the durable lakehouse
        v
MotherDuck / DuckLake Gold + Features
        |
        +--> BI / SQL consumers
        |
        v
Kaggle + MLJAR
        |
        +--> historical predictions -> lakehouse
        +--> compact metrics / serving state -> optional Neon
        +--> model/artifacts -> artifact storage
```

## Durable output target

The canonical generated-data path for ReactOracle is Parquet into MotherDuck / DuckLake.

The Oracle VM is compute, not durable business-data storage. Raw, Bronze, Silver, Gold and feature tables must survive VM shutdown or rebuild.

Neon remains an optional exercise/serving component:

- PostgreSQL source simulation when we specifically want to learn JDBC/incremental ingestion;
- small ML metrics / latest-prediction serving tables;
- ReactOracle operational metadata.

Do not make Neon the canonical generated-data store and do not force large analytical tables through PostgreSQL.

## ReactOracle V1 vs V2

**V1** should already understand and display the future Data Factory node, contracts and intended pipeline, but should not depend on the generator.

**V2** can add execution:

1. submit generator spec;
2. track run;
3. validate manifest;
4. publish generated Parquet into MotherDuck / DuckLake;
5. trigger Airflow;
6. process with Spark;
7. publish validated Bronze/Silver/Gold/features back to the durable lakehouse;
8. launch Kaggle/MLJAR from a bounded feature package;
9. import metrics/predictions;
10. retain historical analytical results in the lakehouse and optional compact serving metadata in Neon;
11. expose Gold tables to BI / SQL consumers.

The fork should be intentionally boring: one generator, one contract, strong deterministic tests, no second control plane.
