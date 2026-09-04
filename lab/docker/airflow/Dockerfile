FROM apache/airflow:3.3.1-python3.12@sha256:b01a795dfbd113bbbfdf3ee169b8f27e9a0090ccef105f1a452b3594a11ed316

RUN pip install --no-cache-dir \
    apache-airflow-providers-docker==4.5.9 \
    apache-airflow-providers-standard==1.17.0 \
    --constraint https://raw.githubusercontent.com/apache/airflow/constraints-3.3.1/constraints-3.12.txt
