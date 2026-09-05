"""Secondary educational Spark ML; uses the identical Gold legality and split contract."""
import argparse
from pathlib import Path
import numpy as np
import pandas as pd
from common import read, write, sha, now
from ml_lab import FEATURES, NUMERIC, CATEGORICAL, TARGET, temporal_split, evaluate


def train(features, config, spec, output):
    import pyarrow.parquet as pq
    if pq.read_metadata(features).num_rows * 4096 > config["materializationLimitMb"] * 1024 * 1024:
        raise ValueError("This small comparison package exceeds its materialization budget")
    if set(spec["features"]) != set(FEATURES): raise ValueError("Feature allowlist mismatch")
    data, partitions = temporal_split(pd.read_parquet(features), config["labelAsOf"])
    from pyspark.sql import SparkSession, functions as F
    from pyspark.ml import Pipeline
    from pyspark.ml.feature import StringIndexer, OneHotEncoder, Imputer, VectorAssembler
    from pyspark.ml.classification import LogisticRegression, RandomForestClassifier, GBTClassifier
    from pyspark.ml.functions import vector_to_array
    spark = SparkSession.builder.master("local[2]").appName("Contoso Forge Spark ML comparison").config("spark.sql.session.timeZone", "UTC").getOrCreate()
    try:
        # This explicit bounded conversion is for small educational comparisons, not a large-data default.
        frame = spark.createDataFrame(data[["order_key", "split_name", TARGET] + FEATURES].assign(**{n: data[n].astype(float) for n in NUMERIC}))
        training = frame.filter(F.col("split_name") == "train")
        indexers = [StringIndexer(inputCol=c, outputCol=c + "_idx", handleInvalid="keep") for c in CATEGORICAL]
        encoder = OneHotEncoder(inputCols=[c + "_idx" for c in CATEGORICAL], outputCols=[c + "_vec" for c in CATEGORICAL], handleInvalid="keep")
        imputer = Imputer(inputCols=NUMERIC, outputCols=[c + "_imputed" for c in NUMERIC], strategy="median")
        assembler = VectorAssembler(inputCols=[c + "_imputed" for c in NUMERIC] + [c + "_vec" for c in CATEGORICAL], outputCol="features")
        seed = config["seed"]
        estimators = {"logistic_regression": LogisticRegression(labelCol=TARGET, maxIter=100),
                      "random_forest": RandomForestClassifier(labelCol=TARGET, numTrees=100, maxDepth=5, seed=seed),
                      "gradient_boosting": GBTClassifier(labelCol=TARGET, maxIter=30, maxDepth=3, seed=seed)}
        metrics, predictions = {}, []
        prior = float(data.loc[data.split_name == "train", TARGET].mean())
        for name, estimator in [("dummy", None), *estimators.items()]:
            fitted = Pipeline(stages=indexers + [encoder, imputer, assembler, estimator]).fit(training) if estimator is not None else None
            metrics[name] = {}
            for split in ("validation", "test"):
                subset = frame.filter(F.col("split_name") == split)
                observed = (fitted.transform(subset).select("order_key", TARGET, vector_to_array("probability")[1].alias("probability"))
                            if fitted else subset.select("order_key", TARGET, F.lit(prior).alias("probability"))).orderBy("order_key").toPandas()
                metrics[name][split] = evaluate(observed[TARGET], observed.probability.to_numpy(), config["threshold"])
                observed["algorithm"], observed["split_name"] = name, split
                predictions.append(observed)
        output.mkdir(parents=True, exist_ok=True)
        pd.concat(predictions).to_parquet(output / "predictions.parquet", index=False)
        selected = max(metrics, key=lambda n: (metrics[n]["validation"]["average_precision"], n))
        result = {"status": "executed", "framework": "spark-ml", "sparkVersion": spark.version, "completedAt": now(),
                  "identity": {"featureSha256": sha(features)}, "partitions": partitions, "models": metrics, "selectedModel": selected,
                  "selectedBy": "validation Average Precision only", "embargoDays": 14}
        write(output / "metrics.json", result)
        return result
    finally: spark.stop()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    for name in ("features", "config", "spec", "output"): parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    train(args.features, read(args.config), read(args.spec), args.output)
