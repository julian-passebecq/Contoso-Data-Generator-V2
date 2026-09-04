resource "google_bigquery_dataset" "forge" {
  count                       = var.create_bigquery_dataset ? 1 : 0
  project                     = var.project_id
  dataset_id                  = var.dataset_id
  location                    = var.location
  description                 = "Contoso Forge learning dataset; managed infrastructure only"
  delete_contents_on_destroy  = false
  deletion_policy             = "PREVENT"
  default_table_expiration_ms = var.table_expiration_days * 86400000
  labels                      = var.labels
}

resource "google_bigquery_table" "native" {
  for_each            = var.create_bigquery_dataset ? var.tables : {}
  project             = var.project_id
  dataset_id          = google_bigquery_dataset.forge[0].dataset_id
  table_id            = each.key
  schema              = jsonencode(each.value.schema)
  deletion_protection = true
  labels              = var.labels
}

resource "google_bigquery_dataset_iam_member" "writers" {
  for_each   = var.create_bigquery_dataset ? var.dataset_iam_members : toset([])
  project    = var.project_id
  dataset_id = google_bigquery_dataset.forge[0].dataset_id
  role       = "roles/bigquery.dataEditor"
  member     = each.value
}

resource "google_storage_bucket" "handoff" {
  count                       = var.create_gcs_bucket ? 1 : 0
  project                     = var.project_id
  name                        = var.bucket_name != "" ? var.bucket_name : "${var.project_id}-contoso-forge"
  location                    = var.location
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = false
  labels                      = var.labels
  lifecycle_rule {
    condition {
      age = var.object_expiration_days
    }
    action {
      type = "Delete"
    }
  }
  lifecycle {
    prevent_destroy = true
    precondition {
      condition     = var.cost_profile == "gcp-free-tier-billing-enabled"
      error_message = "GCS requires gcp-free-tier-billing-enabled. It is unavailable in the no-card sandbox preset."
    }
  }
}
