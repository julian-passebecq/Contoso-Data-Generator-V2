variable "project_id" {
  description = "Existing Google Cloud project. Auth uses ADC; credentials never belong in tfvars."
  type        = string
  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{4,28}[a-z0-9]$", var.project_id))
    error_message = "Use a valid Google Cloud project ID."
  }
}

variable "dataset_id" {
  type    = string
  default = "contoso_forge"
  validation {
    condition     = can(regex("^[A-Za-z0-9_]+$", var.dataset_id)) && length(var.dataset_id) <= 1024
    error_message = "BigQuery dataset IDs use letters, numbers, and underscores."
  }
}

variable "location" {
  description = "Explicit BigQuery and optional GCS location. Keep loading/query job locations aligned."
  type        = string
  default     = "US"
  validation {
    condition     = length(trimspace(var.location)) > 0
    error_message = "Specify a location."
  }
}

variable "cost_profile" {
  type    = string
  default = "gcp-sandbox-no-card"
  validation {
    condition     = contains(["gcp-sandbox-no-card", "gcp-free-tier-billing-enabled"], var.cost_profile)
    error_message = "Choose the no-card sandbox or the billing-enabled free-usage profile."
  }
}

variable "create_bigquery_dataset" {
  type    = bool
  default = true
}

variable "create_gcs_bucket" {
  type    = bool
  default = false
}

variable "bucket_name" {
  type    = string
  default = ""
}

variable "dataset_iam_members" {
  description = "Optional principals granted dataset dataEditor only. Job execution permissions are administered separately."
  type        = set(string)
  default     = []
  validation {
    condition     = alltrue([for member in var.dataset_iam_members : can(regex("^(user|group|serviceAccount):[^[:space:]]+@[^[:space:]]+$", member))])
    error_message = "Use explicit user:, group:, or serviceAccount: principals; public IAM grants are not supported."
  }
}

variable "tables" {
  description = "Optional native table schemas. Empty by default so the explicit-schema batch loader owns table creation."
  type = map(object({
    schema = list(object({
      name = string
      type = string
      mode = optional(string, "NULLABLE")
    }))
  }))
  default = {}
  validation {
    condition     = alltrue([for name, definition in var.tables : can(regex("^[A-Za-z_][A-Za-z0-9_]*$", name)) && length(name) <= 1024 && length(definition.schema) > 0])
    error_message = "Provide a safe table ID and at least one field for every native table."
  }
}

variable "table_expiration_days" {
  description = "Small teaching-lab retention; BigQuery Sandbox enforces its own 60-day lifetime."
  type        = number
  default     = 60
  validation {
    condition     = var.table_expiration_days >= 1 && var.table_expiration_days <= 60
    error_message = "Keep lab table retention between 1 and 60 days."
  }
}

variable "object_expiration_days" {
  type    = number
  default = 7
  validation {
    condition     = var.object_expiration_days >= 1 && var.object_expiration_days <= 30
    error_message = "Keep handoff objects between 1 and 30 days."
  }
}

variable "labels" {
  type = map(string)
  default = {
    application = "contoso-forge"
    environment = "learning"
  }
}
