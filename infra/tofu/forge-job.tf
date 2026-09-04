resource "kubernetes_job_v1" "forge" {
  wait_for_completion = true

  metadata {
    name      = "contoso-forge-generator"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels = merge(local.run_labels, {
      "app.kubernetes.io/component" = "generator"
    })
  }

  spec {
    backoff_limit = 0

    template {
      metadata {
        labels = merge(local.run_labels, {
          "app.kubernetes.io/component" = "generator"
        })
      }

      spec {
        restart_policy = "Never"

        container {
          name              = "forge"
          image             = var.forge_image
          image_pull_policy = "Never"
          args = [
            "forge",
            "generate",
            "--project",
            "/config/project.json",
            "--output",
            "/workspace/out",
            "--lake",
            "/workspace/lake",
          ]

          resources {
            requests = {
              cpu    = "50m"
              memory = "128Mi"
            }
            limits = {
              cpu    = "1"
              memory = "512Mi"
            }
          }

          volume_mount {
            name       = "project"
            mount_path = "/config"
            read_only  = true
          }

          volume_mount {
            name       = "workspace"
            mount_path = "/workspace"
          }
        }

        volume {
          name = "project"
          config_map {
            name = kubernetes_config_map_v1.forge_project.metadata[0].name
          }
        }

        volume {
          name = "workspace"
          persistent_volume_claim {
            claim_name = kubernetes_persistent_volume_claim_v1.workspace.metadata[0].name
          }
        }
      }
    }
  }

  timeouts {
    create = "10m"
    update = "10m"
  }

  lifecycle {
    replace_triggered_by = [terraform_data.run]
  }
}
