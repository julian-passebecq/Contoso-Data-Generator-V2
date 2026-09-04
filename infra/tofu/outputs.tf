output "namespace" {
  description = "OpenTofu-managed V1C namespace."
  value       = kubernetes_namespace_v1.forge.metadata[0].name
}

output "run_id" {
  description = "Run identifier attached to the Jobs and Spark-created pods."
  value       = var.run_id
}

output "workspace_claim" {
  description = "PVC shared by the sequential Forge and dbt Jobs."
  value       = kubernetes_persistent_volume_claim_v1.workspace.metadata[0].name
}

output "jobs" {
  description = "OpenTofu-managed V1C Job names."
  value = {
    forge        = kubernetes_job_v1.forge.metadata[0].name
    dbt          = kubernetes_job_v1.dbt.metadata[0].name
    spark_submit = kubernetes_job_v1.spark_submit.metadata[0].name
  }
}
