resource "kubernetes_job_v1" "dbt" {
  wait_for_completion = true

  metadata {
    name      = "contoso-forge-dbt-validate"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels = merge(local.run_labels, {
      "app.kubernetes.io/component" = "dbt-validation"
    })
  }

  spec {
    backoff_limit = 0

    template {
      metadata {
        labels = merge(local.run_labels, {
          "app.kubernetes.io/component" = "dbt-validation"
        })
      }

      spec {
        restart_policy = "Never"

        container {
          name              = "dbt"
          image             = var.dbt_image
          image_pull_policy = "Never"
          command           = ["/bin/sh", "-ec"]
          args = [<<-SHELL
            dbt parse \
              --project-dir /workspace/out/dbt \
              --profiles-dir /workspace/out/dbt \
              --target local
            python /validation/validate.py
          SHELL
          ]

          env {
            name  = "FORGE_LAKE_ROOT"
            value = "/workspace/lake"
          }

          env {
            name  = "FORGE_TRUTH_MANIFEST"
            value = "/workspace/out/truth_manifest.json"
          }

          env {
            name  = "FORGE_DUCKDB_PATH"
            value = "/workspace/lake/gold/contoso_forge.duckdb"
          }

          resources {
            requests = {
              cpu    = "50m"
              memory = "256Mi"
            }
            limits = {
              cpu    = "1"
              memory = "768Mi"
            }
          }

          volume_mount {
            name       = "workspace"
            mount_path = "/workspace"
          }

          volume_mount {
            name       = "validation"
            mount_path = "/validation"
            read_only  = true
          }
        }

        volume {
          name = "workspace"
          persistent_volume_claim {
            claim_name = kubernetes_persistent_volume_claim_v1.workspace.metadata[0].name
          }
        }

        volume {
          name = "validation"
          config_map {
            name = kubernetes_config_map_v1.dbt_validation.metadata[0].name
          }
        }
      }
    }
  }

  timeouts {
    create = "10m"
    update = "10m"
  }

  depends_on = [kubernetes_job_v1.forge]

  lifecycle {
    replace_triggered_by = [terraform_data.run]
  }
}
