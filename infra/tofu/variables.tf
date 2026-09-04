variable "kubeconfig_path" {
  description = "Path to the kubeconfig written by kind."
  type        = string
  default     = "~/.kube/config"
}

variable "kube_context" {
  description = "Existing kind context managed by this configuration."
  type        = string
  default     = "kind-contoso-forge"
}

variable "namespace" {
  description = "Namespace for the isolated V1C workloads."
  type        = string
  default     = "contoso-forge"
}

variable "forge_image" {
  description = "Locally built Forge image loaded into kind."
  type        = string
  default     = "contoso-forge:local"
}

variable "dbt_image" {
  description = "Locally built dbt Core + DuckDB image loaded into kind."
  type        = string
  default     = "contoso-forge-dbt:local"
}

variable "spark_image" {
  description = "Locally built Spark image loaded into kind."
  type        = string
  default     = "contoso-forge-spark:local"
}

variable "workspace_storage" {
  description = "Storage requested for generated artifacts shared by sequential Jobs."
  type        = string
  default     = "2Gi"
}

variable "run_id" {
  description = "DNS-label-safe run identifier used to replace completed Jobs and select Spark evidence."
  type        = string
  default     = "manual"

  validation {
    condition     = can(regex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$", var.run_id)) && length(var.run_id) <= 24
    error_message = "run_id must be a lowercase DNS label no longer than 24 characters."
  }
}

