FROM python:3.12.14-slim-bookworm@sha256:782412e85d0f0984994c290652577d4018aff08145c85b262bb63dc0c7522254

ENV PIP_DISABLE_PIP_VERSION_CHECK=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

RUN pip install --no-cache-dir \
    dbt-core==1.11.14 \
    dbt-duckdb==1.11.0 \
    duckdb==1.5.5

WORKDIR /workspace
ENTRYPOINT ["dbt"]
