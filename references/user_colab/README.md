# User-supplied classic PySpark evidence

`pysparktestj.ipynb` is the unchanged notebook supplied in the V1.3 planning package. SHA-256:

```text
307b66286e696272c1f2928e69c166177491c1be6893d57952df8a6fa903b726
```

Its saved outputs show hosted Colab with PySpark 4.0.4 and Py4J 0.10.9.9, a classic `SparkSession`, and a successful 100,000,000-sample RDD calculation (Pi 3.14168116).

This is user-provided environment evidence. It demonstrates classic local PySpark availability, not Spark Connect or a generated Forge Bronze/Silver/BigQuery run. Keep it as a reference fixture; product notebook generation belongs under `DatabaseGenerator/Forge/Templates/free_gcp/colab`.
