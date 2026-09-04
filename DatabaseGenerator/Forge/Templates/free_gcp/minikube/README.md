# Minikube Airflow control plane

Status: **generated-reference**, pending installation, pod readiness, GitSync,
DAG parsing, work-order execution, and reconciliation. Helm rendering proves
manifest compatibility only. The official Apache chart is pinned to **1.22.0**,
Airflow to **3.2.2**, and git-sync to **4.4.2**. Use Helm **3.19.0 or newer**.

LocalExecutor runs small orchestration tasks. Spark runs in the selected external
runtime; this profile launches no Spark, Celery, Redis, or Composer service.
Free Colab is an interactive manual checkpoint. GitHub hosts source/DAGs and runs
finite CI jobs; it is not an always-on Airflow runtime.

Install Docker, Minikube, kubectl, Helm, and Python. Allow roughly 5 GiB for this
small lab cluster; requests are modest but startup and task memory vary. Publish
the complete generated project to `git.projectSubPath` (default `generated`) in the
configured `git.repository` and `git.branch` before installation. The Airflow
scan folder `git.subPath` defaults to `generated/airflow/dags`. Keep both paths
aligned. The full tree must include `truth_manifest.json`, `resolved_project.json`,
`pipeline.json`, `data/source/*.csv`, `pyspark/bronze_silver.py`, `gcp/*`, `colab/*`,
and both Python files in `airflow/dags`. Only commit a small synthetic sample;
never include `.forge`, returned results, credential files, or infrastructure state.
`values.yaml` sets both branch and the git-sync
v4 `ref`; edit the project and regenerate to keep them aligned.

Run these commands from this generated `minikube` directory:

```sh
minikube start --profile contoso-forge --driver=docker --cpus=2 --memory=5120 --keep-context
python bootstrap_secrets.py --context contoso-forge
kubectl --context contoso-forge apply -f metadata-postgres.yaml
kubectl --context contoso-forge apply -f runtime-state.yaml
kubectl --context contoso-forge -n contoso-forge rollout status statefulset/contoso-forge-postgres --timeout=300s
helm repo add apache-airflow https://airflow.apache.org
helm repo update
helm upgrade --install airflow apache-airflow/airflow --version 1.22.0 --kube-context contoso-forge --namespace contoso-forge --values values.yaml --timeout 15m --wait
kubectl --context contoso-forge -n contoso-forge get pods
kubectl --context contoso-forge -n contoso-forge port-forward service/airflow-api-server 8080:8080 --address=127.0.0.1
```

Open `http://127.0.0.1:8080`. The bootstrap helper generates random local metadata
and admin passwords inside a Kubernetes Secret, retaining it on subsequent runs.
It does not write credential files. Read the admin password only in your terminal:

```sh
kubectl --context contoso-forge -n contoso-forge get secret contoso-forge-metadata -o jsonpath='{.data.simple_auth_manager_passwords\.json}'
```

Decode that base64 value locally to read the `admin` password. Base64 is not
encryption. The chart-supported external PostgreSQL connection uses a small local
StatefulSet with an official PostgreSQL **16.15** image and a 1 GiB PVC. The bundled
legacy Bitnami database is disabled. This metadata database is for the teaching lab;
backups, production availability, and secret rotation require separate operations.
Task logs are ephemeral; stop/start of pods can remove them.
Airflow 3's simple auth manager opens its password JSON for writing even when
passwords already exist. Init containers copy the Secret into a writable per-pod
emptyDir before startup; no password is printed or committed.
The migration Job has `useHelmHooks: false`: it must run while Helm waits for the
pods that depend on its database migration. A post-install hook creates a startup
deadlock with `--wait`. Match kubectl to the cluster's Kubernetes minor version.

Public HTTPS repositories need no Git credentials. Private repositories can use
the chart's `dags.gitSync.credentialsSecret` referencing a separately created
Secret with `GITSYNC_USERNAME` and `GITSYNC_PASSWORD`, or an SSH deploy-key Secret
with the chart's `sshKeySecret` and verified `knownHosts`. Never put PATs, private
keys, or Helm expressions in project JSON or committed values.

Check the control plane after install:

```sh
kubectl --context contoso-forge -n contoso-forge logs deployment/airflow-dag-processor -c git-sync --tail=50
kubectl --context contoso-forge -n contoso-forge get pods -l component=scheduler
kubectl --context contoso-forge -n contoso-forge exec SCHEDULER_POD -c scheduler -- airflow dags list-import-errors
kubectl --context contoso-forge -n contoso-forge exec SCHEDULER_POD -c scheduler -- airflow dags list
```

GitSync clones the full repository. `FORGE_PROJECT_ROOT` points at its generated
project under `/opt/airflow/dags/repo`; the DAG scan path remains separate.
`FORGE_STATE_ROOT=/opt/airflow/forge-state` is backed by a writable 1 GiB PVC shared
by Airflow pods on this one-node Minikube lab. It holds each run's ZIP, work order,
and result manifest, separate from the GitSync checkout. No input or result is
stored in the Airflow metadata database.

Trigger the DAG and inspect `prepare_colab` task logs for the exact run directory.
Replace `SCHEDULER_POD` and `RUN_HASH` below with the actual scheduler pod name and
run hash printed in the task log:

```sh
kubectl --context contoso-forge -n contoso-forge get pods -l component=scheduler
kubectl --context contoso-forge cp contoso-forge/SCHEDULER_POD:/opt/airflow/forge-state/runs/RUN_HASH/work_package.zip ./work_package.zip -c scheduler
```

Upload that ZIP in the generated notebook, execute it manually, and download its
result manifest. Return it using a temporary filename, then atomically publish it
so the waiting sensor never reads a partial upload:

```sh
kubectl --context contoso-forge cp ./result_manifest.json contoso-forge/SCHEDULER_POD:/opt/airflow/forge-state/runs/RUN_HASH/result_manifest.json.partial -c scheduler
kubectl --context contoso-forge -n contoso-forge exec SCHEDULER_POD -c scheduler -- mv /opt/airflow/forge-state/runs/RUN_HASH/result_manifest.json.partial /opt/airflow/forge-state/runs/RUN_HASH/result_manifest.json
```

The sensor verifies the returned identity, source hashes, expiry and measured
reconciliation before continuing. Do not mark it successful without that evidence.
Deleting the state PVC loses resumable work orders. Use a new run ID for a new run.

The existing Docker Airflow/Spark and kind/OpenTofu profiles remain available.
Minikube bootstrap stays outside IaC; no shell provisioner starts a cluster.
Infrastructure for BigQuery is a separate optional tree under `infra/gcp`.

From the Forge repository, run `python scripts/run_minikube_smoke.py --project
<generated-project> --context contoso-forge --install --trigger --output
artifacts/minikube-runtime.json` to record the observed cluster, chart, pod,
GitSync byte checks, real DAG parse, and task states. It leaves the manual sensor
waiting for an actual returned result. It does not publish a repository or mark
the full architecture validated while a checkpoint is pending. A local Git server
can test GitSync separately; that is explicitly different from the GitHub gate.

Before Google authentication, `--scope spark` issues an explicitly Spark-only work
order. In Airflow's trigger configuration this is `{"executionScope":"spark"}`;
the default remains `spark-and-bigquery`. Resume with the same scope. After
returning a result, use `--observe-run --run-id <original-run-id>` instead of
`--trigger` to record that run's observed reconciliation without issuing another
work order.

Primary references: [official chart values](https://github.com/apache/airflow/blob/helm-chart/1.22.0/chart/values.yaml),
[release notes](https://airflow.apache.org/docs/helm-chart/stable/release_notes.html),
[Airflow simple auth](https://airflow.apache.org/docs/apache-airflow/3.2.2/core-concepts/auth-manager/simple/index.html).
