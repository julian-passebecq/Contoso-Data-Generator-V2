# BigQuery ML Sandbox boundaries

Official sources checked on 2026-09-05. This note distinguishes published
capabilities from the actual Contoso Forge execution evidence.

The current Sandbox documentation permits use without a billing account, with
10 GB active storage, 1 TB monthly query processing and automatic 60-day expiry
for tables, views and partitions. Its unsupported-feature list includes
streaming, DML and Data Transfer Service. That list does not explicitly address
each BigQuery ML model type, so it establishes neither a blanket prohibition
nor a guarantee for `CREATE MODEL`.
[Sandbox documentation](https://docs.cloud.google.com/bigquery/docs/sandbox)

For logistic regression, Google's current tutorial asks users to enable billing.
The current pricing page identifies logistic regression as a built-in model;
its free-usage table covers model/data storage and ordinary query allowances but
does not publish a separate current free model-training allowance. Therefore this
repository does not promise that a no-billing project can train logistic models.
This is a conservative interpretation of the current documents, not an observed
service rejection.
[Logistic tutorial](https://docs.cloud.google.com/bigquery/docs/create-machine-learning-model),
[BigQuery pricing](https://cloud.google.com/bigquery/pricing)

Boosted-tree models use external training services. Pricing includes BigQuery
preprocessing and the external training cost; ordinary query free usage does
not establish a free training entitlement. The generated boosted-tree adapter
must remain opt-in, and its availability and results must be established for
the actual project. Dry runs cannot estimate processed bytes in advance for
every model type.
[BigQuery ML pricing](https://cloud.google.com/bigquery/pricing)

An older official 2019 article advertised 10 GB of Sandbox model-creation queries.
That historical statement is not a current quota guarantee. A newer official
article states that some ML features are restricted in Sandbox and illustrates
enabling billing for an advanced forecasting feature; it does not enumerate
logistic or boosted-tree eligibility.
[Historical Sandbox ML article](https://cloud.google.com/blog/products/ai-machine-learning/show-off-your-bigquery-ml-and-kaggle-skills-competition-open-now),
[Current SQL/Python article](https://cloud.google.com/blog/products/data-analytics/bridge-sql-and-python-with-bigquery/)

The current general ML quota is 20,000 `CREATE MODEL` statements per project
over 48 hours, with a usual 24-hour job timeout; time-series, AutoML and
hyperparameter-tuning jobs have 48 hours. External services can impose additional
quotas. These are service limits, not free allowances or evidence that an
individual Sandbox project can train a model.
[BigQuery ML quotas](https://docs.cloud.google.com/bigquery/quotas#bigquery_ml)

Contoso Forge's actual native Sandbox proof covers 13 batch loads and 14 count/KPI
queries, with all five KPIs reconciled and the returned result imported. The
corrected native dbt run passed 24 models and 121 tests, and native ML feature SQL
also executed successfully. These checks do not establish `CREATE MODEL` support.
The native 60-order feature query measured train 21 negative/5 positive, no
validation rows and test 9 negative/0 positive, so that fixture lacks adequate
validation/test class support after its label embargo;
training must not bypass that guard merely to produce a model. A separate
1,200-order/365-day example now has viable measured offline splits with the
unchanged 14-day embargo: train 721 negative/75 positive, validation 127/9 and
test 163/17. That offline check does not establish native model-training support.
See the
[current handoff](../HANDOFF.md) for the latest native dbt/ML execution status.
