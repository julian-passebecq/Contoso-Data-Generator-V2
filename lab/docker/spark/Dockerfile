FROM apache/spark:3.5.9-scala2.12-java17-python3-ubuntu@sha256:f3d6eaa8bab8ec2e38f3c3918a5b2f8b253c95bcb19f9e87ad0e0cdacf9df2d5

USER root
ADD --checksum=sha256:4912bf902a1c453aba3bd838ff212dedd357155212f9bb37a6b47d97f2827ebc \
    https://repo1.maven.org/maven2/io/delta/delta-spark_2.12/3.3.3/delta-spark_2.12-3.3.3.jar \
    /opt/spark/jars/delta-spark_2.12-3.3.3.jar
ADD --checksum=sha256:45ad193053fd30cebe6d9ab1889873cde39b223a0f210e00ebee8b6642476901 \
    https://repo1.maven.org/maven2/io/delta/delta-storage/3.3.3/delta-storage-3.3.3.jar \
    /opt/spark/jars/delta-storage-3.3.3.jar

ENV PYSPARK_PYTHON=python3
ENTRYPOINT ["/opt/spark/bin/spark-submit", \
    "--master", "local[2]", \
    "--conf", "spark.driver.host=127.0.0.1", \
    "--conf", "spark.sql.extensions=io.delta.sql.DeltaSparkSessionExtension", \
    "--conf", "spark.sql.catalog.spark_catalog=org.apache.spark.sql.delta.catalog.DeltaCatalog"]
