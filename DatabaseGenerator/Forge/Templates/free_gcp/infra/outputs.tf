output "project_id" {
  value = var.project_id
}

output "bigquery_dataset" {
  value = var.create_bigquery_dataset ? "${var.project_id}.${google_bigquery_dataset.forge[0].dataset_id}" : null
}

output "gcs_bucket" {
  value = var.create_gcs_bucket ? google_storage_bucket.handoff[0].name : null
}

output "cost_profile" {
  value = var.cost_profile
}
