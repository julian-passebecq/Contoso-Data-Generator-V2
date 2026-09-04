resource "kubernetes_namespace_v1" "forge" {
  metadata {
    name = var.namespace
    labels = {
      "app.kubernetes.io/name"       = "contoso-forge"
      "app.kubernetes.io/managed-by" = "opentofu"
      "contoso-forge/environment"    = "local-v1c"
    }
  }
}

