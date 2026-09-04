resource "kubernetes_persistent_volume_claim_v1" "workspace" {
  wait_until_bound = false

  metadata {
    name      = "contoso-forge-workspace"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels = {
      "app.kubernetes.io/name"       = "contoso-forge"
      "app.kubernetes.io/component"  = "workspace"
      "app.kubernetes.io/managed-by" = "opentofu"
    }
  }

  spec {
    access_modes       = ["ReadWriteOnce"]
    storage_class_name = "standard"

    resources {
      requests = {
        storage = var.workspace_storage
      }
    }
  }
}

