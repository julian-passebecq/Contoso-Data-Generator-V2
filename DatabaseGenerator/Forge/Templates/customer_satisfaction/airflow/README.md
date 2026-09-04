# Airflow orchestration

Artifact status: **validated** for Airflow 3 in the local Docker reference lab.

The DAG runs the exact same Forge, Spark, and dbt images used by the command-line
lab helpers. It uses Airflow's Docker provider and an explicit repository bind
mount. For this local learning stack only, the Airflow worker is granted access
to the Docker socket. That socket is equivalent to host-level control; run only
trusted DAGs and never expose it or reuse this topology for production.

GitHub Codespaces uses the same DAG and named, bind-backed workspace volume.
The portable lab helper creates that volume against the current checkout.
