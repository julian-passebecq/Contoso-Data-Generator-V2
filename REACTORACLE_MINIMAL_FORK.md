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
        +--> optional source Neon/PostgreSQL
        |
        v
Airflow
        |
        v
Spark on Oracle K3s
        |
        v
Bronze / Silver / Gold / feature set
        |
        v
Kaggle + MLJAR
        |
        +--> metrics/predictions -> Neon
        +--> model/artifacts -> artifact storage
        |
        v
dbt / BI
```

## Neon source option

A dedicated Neon database/project can be used to simulate a real operational PostgreSQL source for small and medium generated datasets. Airflow would then extract from Neon into the Oracle/Spark pipeline.

This remains optional:

- small/medium scenario: generator -> Neon source -> Airflow -> Spark
- large scenario: generator -> Parquet/object/file output -> Airflow -> Spark

Do not force large synthetic datasets through Postgres only to preserve the architecture diagram.

## ReactOracle V1 vs V2

**V1** should already understand and display the future Data Factory node, contracts and intended pipeline, but should not depend on the generator.

**V2** can add execution:

1. submit generator spec;
2. track run;
3. validate manifest;
4. optionally load source Neon;
5. trigger Airflow;
6. process with Spark;
7. publish an ML feature package;
8. launch Kaggle/MLJAR;
9. import metrics/predictions;
10. surface results in ReactOracle and BI.

The fork should be intentionally boring: one generator, one contract, strong deterministic tests, no second control plane.
