# BigQuery ML after Gold

This optional adapter trains a logistic regression baseline or a separately selected boosted-tree candidate. It is generated as reference code, with no trained-model claim. The default Colab/Sandbox pipeline never starts training automatically.

First finish the native BigQuery reconciliation and `dbt_bigquery/run_dbt.py`. Then preview SQL with an explicit label observation cutoff:

```sh
python bqml/run_bqml.py --root . --label-as-of 2026-01-01T00:00:00Z
```

Inspect the generated model directory's `train.sql`. To run, repeat with `--execute --allow-training-cost`. Use `--model boosted-tree` for the candidate. These commands use the same ADC authentication, dataset, location, run prefix, and query byte limit. They do not enable billing or create a Vertex endpoint. BigQuery ML training has separate pricing/quotas and boosted trees use external training infrastructure: the query byte guard is not an overall ML cost cap. Sandbox availability is checked through the actual service response, not assumed. See [BigQuery ML pricing](https://cloud.google.com/bigquery/pricing#bqml) and [CREATE MODEL](https://docs.cloud.google.com/bigquery/docs/reference/standard-sql/bigqueryml-syntax-create).

Features exclude current-order review, refund, return and support outcomes. Labels require a closed 14-day post-delivery window. Customer versions and shipment events respect their ingestion timestamps. V1 review/support data have no separate ingestion timestamp, so label availability uses review/closed time. Missing observed dissatisfaction is treated as a negative only after window closure; selective observation remains a model limitation.

Splits are oldest 70%, next 15%, newest 15% by order date/key. A 14-day label embargo prevents training labels from crossing into the validation horizon, and validation labels from crossing into test. Execution fails if any partition has fewer than two examples per class; small demos may need a much larger sample and longer time span.

The runner records actual jobs, split counts, ML.EVALUATE output, holdout average precision/ROC-AUC/log-loss/balanced accuracy/precision/recall/F1/Brier score, majority and delay baselines, slice metrics, predictions Parquet, global feature importance and a model card. It does not assert a model quality score. A content-addressed model name plus plain CREATE MODEL prevents replacement of an existing trained model.
