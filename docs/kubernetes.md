# Local Kubernetes V1C

V1C is a deliberately small local proof built on the same three job images as
the Docker Compose lab. OpenTofu creates an isolated namespace, shared storage,
a Forge generator Job, a generated-dbt-project validation Job, and a native
Spark-on-Kubernetes smoke submission. Airflow remains in Docker Compose; it is
not moved to Kubernetes in V1.

## Pinned validation stack

- kind `v0.31.0`
- kind node / Kubernetes `v1.35.0` (image pinned by digest in
  `infra/kind/cluster.yaml`)
- kubectl `v1.35.0`
- OpenTofu `v1.12.6`
- HashiCorp Kubernetes provider `3.2.1`

Install those tools on `PATH`, or place their executables in the repository's
ignored `.tools` directory. Docker Desktop must be running. The Python driver
resolves `.tools` before `PATH`, builds any missing Compose job images, and
does not need cloud credentials.

## Run the proof

From the repository root:

```powershell
python scripts/v1c.py versions
python scripts/v1c.py up
```

The `up` command performs these operations in order:

1. Creates the one-node `contoso-forge` kind cluster.
2. Loads `contoso-forge:local`, `contoso-forge-dbt:local`, and
   `contoso-forge-spark:local` directly into kind.
3. Initializes and validates `infra/tofu`, then applies it with a fresh run ID.
4. Waits for all three Jobs and verifies their logs and Spark-created pods.

The Forge Job generates `/workspace/out/truth_manifest.json` and `lake/raw` on
the PVC. The dbt Job runs `dbt parse` against the generated project, recomputes
every raw-file SHA-256 from the truth manifest, and verifies the parsed model
and test counts. This is intentionally a generated-project contract check;
the full dbt Gold build remains part of the canonical Compose pipeline because
V1C does not duplicate the Docker Spark Bronze/Silver architecture.

The Spark submit Job uses `--master k8s://...` and cluster deploy mode. Spark
then creates a separate driver pod and at least one executor pod. The verifier
requires both roles, a successful driver phase, and the Pi result in driver
logs, so a `local[*]` process inside a Kubernetes Job cannot satisfy the check.

Inspect or rerun validation without changing the cluster:

```powershell
python scripts/v1c.py verify
kubectl --context kind-contoso-forge get jobs,pods -n contoso-forge -o wide
kubectl --context kind-contoso-forge logs -n contoso-forge job/contoso-forge-dbt-validate
```

On bash-compatible systems, `scripts/v1c.sh` exposes the same commands. On
PowerShell, `scripts/v1c.ps1` is an equivalent thin wrapper.

## Teardown

```powershell
python scripts/v1c.py down
```

Teardown deletes Spark-created evidence pods, runs `tofu destroy`, and then
deletes the kind cluster. OpenTofu state and downloaded providers remain local
under `infra/tofu` and are git-ignored. No credentials or secrets are stored in
configuration or state.

This stack is a laptop/Codespaces development proof, not an always-on service
or production Kubernetes design. It deliberately excludes Airflow, ingress,
Kafka, cloud identities, external storage, high availability, and secret
management.
