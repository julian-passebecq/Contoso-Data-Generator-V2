# BigQuery infrastructure

Status: **generated-reference**. One HCL tree serves OpenTofu and Terraform Community.
The Google provider is pinned to **7.45.0**. No exporter or validation command applies resources.

The default `gcp-sandbox-no-card` preset exports an existing-project BigQuery dataset,
optional native table schemas, and optional dataset IAM. GCS defaults to off; no
Composer, Cloud Run, BigLake, billing attachment, API enablement, or service-account
key is required. Sandbox users may create the dataset in the console and use the
loader directly: infrastructure apply is optional and cloud permissions still apply.

`gcp-free-tier-billing-enabled` may opt into a GCS handoff bucket through `storage: gcs`.
Free usage allowances are limits, not a guarantee of zero charges. Query scan limits
belong to the load/query runtime; HCL does not execute transformations or impose a
project-wide billing cap. Dataset/table deletion protection, non-public buckets,
short retention, and explicit locations are configured here. Bucket location and
pricing must be checked for the selected region.

From the repository root, validate a generated project without cloud credentials:

```sh
python scripts/validate_free_gcp_infra.py --project out/my-project --iac dual-validate --require-tools
```

Each engine runs `version`, `fmt -check`, `init -backend=false`, and `validate` in a
separate temporary directory. The JSON evidence distinguishes missing tools,
failures, static success, and untested deployment. It never creates plans or state
in this source tree. `iac: none` omits this directory on a fresh export.

For an intentional cloud deployment, first edit `forge.auto.tfvars.json` with a real
project and dataset, establish Google Application Default Credentials outside the
repository, then inspect a plan manually:

```sh
cd out/my-project/infra/gcp
tofu init
tofu plan
```

Terraform users may substitute `terraform` for `tofu`. Review the plan before any
manual apply; no automatic apply command is provided. Dataset/table creation needs
appropriate permissions on an existing project. This module does not enable paid
services or broaden project IAM. For existing resources, import them before any
apply. Cloud execution has not been validated by static CLI checks.

`tables` accepts a map of native table IDs to `{ "schema": [{ "name": "OrderKey",
"type": "INTEGER", "mode": "REQUIRED" }] }`. Leave it empty to let the batch loader
create native tables. These flat native schemas intentionally do not represent
external Iceberg/Delta metadata. Set `dataset_iam_members` only when explicit
dataset dataEditor grants are needed. BigQuery job permissions are separate.

Provider references: [dataset](https://registry.terraform.io/providers/hashicorp/google/7.45.0/docs/resources/bigquery_dataset),
[table](https://registry.terraform.io/providers/hashicorp/google/7.45.0/docs/resources/bigquery_table),
[bucket](https://registry.terraform.io/providers/hashicorp/google/7.45.0/docs/resources/storage_bucket).
