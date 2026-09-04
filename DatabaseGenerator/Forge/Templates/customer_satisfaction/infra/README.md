# Infrastructure handoff

Artifact status: **starter/reference**

Project: `__PROJECT_NAME__`  
Scenario: `__SCENARIO__`

Infrastructure code is deliberately separate from the generated business/data
specification (`project.json`) and logical workflow (`pipeline/pipeline.json`).
OpenTofu is the default V1 IaC CLI, with conservative HCL kept
Terraform-compatible where practical.

The repository-level V1C lab owns the executable kind, kubectl and OpenTofu
configuration for the `contoso-forge` namespace, Forge Job, dbt Job and small
Spark-on-Kubernetes smoke test. Full Airflow-on-Kubernetes, real cloud
resources, secrets and remote state are outside V1.

