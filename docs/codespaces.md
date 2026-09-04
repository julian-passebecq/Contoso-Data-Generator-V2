# GitHub Codespaces reference lab

The Codespace is an ephemeral development environment for the same Contoso Forge V1 reference lab used on a local machine. It uses the repository's root `compose.yaml`, `scripts/lab.py`, container images, generated artifacts, tests, and `lake/raw|bronze|silver|gold` directories without an override file or a second architecture.

The devcontainer supplies .NET 8, Debian's supported Python 3, Java 17, Docker Compose v2, and an isolated Docker-in-Docker daemon. Its post-create check runs the existing `python3 scripts/lab.py validate` command. It validates the shared Compose file and prepares the same bind-backed `contoso-forge-workspace` volume used by every job.

## Run the lab

The standard 2-core, 8-GB Codespace with 32 GB of storage is the supported baseline. A 4-core machine is an optional faster choice for repeated image builds and Spark runs. Wait for the post-create command to finish, then run the complete validation from the repository root:

```sh
dotnet test ContosoDGV2.sln --configuration Release
python3 scripts/lab.py smoke
```

The shell wrapper reaches the identical command surface:

```sh
sh ./lab.sh smoke
```

For incremental work, use the same commands as the local runbook:

```sh
python3 scripts/lab.py build
python3 scripts/lab.py generate
python3 scripts/lab.py run-spark --stage all
python3 scripts/lab.py run-dbt
python3 scripts/lab.py run-pipeline
```

`run-pipeline` starts Airflow and executes the customer-satisfaction DAG. Port 8080 is declared in the devcontainer and appears as **Contoso Forge Airflow** in the Codespaces **Ports** panel. Keep its visibility private. If authentication is requested, the reference-lab credentials are `admin` / `admin`.

Stop the services when finished:

```sh
python3 scripts/lab.py down
```

Generated data remains in the repository's `out/` and `lake/` paths for the lifetime of the Codespace. Download or commit any intentional, non-generated changes before deleting the Codespace.

## Scope, quota, and security

- This is a development and demonstration environment, not an always-on server or a production deployment. Airflow is available only while the Codespace and its Compose services are running.
- Image builds, Spark, dbt, and Airflow consume Codespaces compute, storage, and included-hours quota. Stop idle Codespaces, remove ones no longer needed, and monitor account or organization quota. The first build is the most storage- and network-intensive.
- Keep forwarded port 8080 private. The checked-in `admin` / `admin` login and local JWT secret are intentionally limited to this reference lab and are not suitable for an internet-facing service.
- Docker-in-Docker runs a privileged daemon inside the devcontainer. Airflow mounts that daemon's socket so its DAG can launch the same Forge, Spark, and dbt images. Do not expose the Docker API, and only run trusted repository changes in this environment.
- V1 requires no cloud credentials. Do not add long-lived cloud secrets to the repository, generated files, image layers, or Compose environment. If optional experimentation needs a secret, use Codespaces secrets and remove it when finished.
- A stopped Codespace is not a durable service, and a deleted Codespace loses uncommitted files and generated lake data. Use the Kubernetes/OpenTofu V1C path only for the separate local orchestration smoke tests; it does not turn Codespaces into production hosting.
