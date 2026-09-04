resource "kubernetes_job_v1" "spark_submit" {
  wait_for_completion = true

  metadata {
    name      = "contoso-forge-spark-submit"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels = merge(local.run_labels, {
      "app.kubernetes.io/component" = "spark-submit"
    })
  }

  spec {
    backoff_limit = 0

    template {
      metadata {
        labels = merge(local.run_labels, {
          "app.kubernetes.io/component" = "spark-submit"
        })
      }

      spec {
        restart_policy       = "Never"
        service_account_name = kubernetes_service_account_v1.spark.metadata[0].name

        container {
          name              = "spark-submit"
          image             = var.spark_image
          image_pull_policy = "Never"
          command           = ["/opt/spark/bin/spark-submit"]
          args = [
            "--master",
            "k8s://https://kubernetes.default.svc:443",
            "--deploy-mode",
            "cluster",
            "--name",
            "contoso-forge-spark-${var.run_id}",
            "--conf",
            "spark.kubernetes.namespace=${kubernetes_namespace_v1.forge.metadata[0].name}",
            "--conf",
            "spark.kubernetes.container.image=${var.spark_image}",
            "--conf",
            "spark.kubernetes.container.image.pullPolicy=Never",
            "--conf",
            "spark.kubernetes.authenticate.submission.caCertFile=/var/run/secrets/kubernetes.io/serviceaccount/ca.crt",
            "--conf",
            "spark.kubernetes.authenticate.submission.oauthTokenFile=/var/run/secrets/kubernetes.io/serviceaccount/token",
            "--conf",
            "spark.kubernetes.authenticate.driver.serviceAccountName=${kubernetes_service_account_v1.spark.metadata[0].name}",
            "--conf",
            "spark.kubernetes.driver.podTemplateFile=/etc/contoso-forge/spark/driver.yaml",
            "--conf",
            "spark.kubernetes.executor.podTemplateFile=/etc/contoso-forge/spark/executor.yaml",
            "--conf",
            "spark.kubernetes.executor.deleteOnTermination=false",
            "--conf",
            "spark.kubernetes.driver.label.contoso-forge-managed=spark",
            "--conf",
            "spark.kubernetes.executor.label.contoso-forge-managed=spark",
            "--conf",
            "spark.kubernetes.driver.label.contoso-forge-run=${var.run_id}",
            "--conf",
            "spark.kubernetes.executor.label.contoso-forge-run=${var.run_id}",
            "--conf",
            "spark.executor.instances=1",
            "--conf",
            "spark.executor.cores=1",
            "--conf",
            "spark.executor.memory=512m",
            "--conf",
            "spark.driver.memory=512m",
            "--conf",
            "spark.kubernetes.memoryOverheadFactor=0.20",
            "--conf",
            "spark.dynamicAllocation.enabled=false",
            "--conf",
            "spark.ui.enabled=false",
            "local:///opt/spark/examples/src/main/python/pi.py",
            "10",
          ]

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
            name       = "spark-pod-templates"
            mount_path = "/etc/contoso-forge/spark"
            read_only  = true
          }
        }

        volume {
          name = "spark-pod-templates"
          config_map {
            name = kubernetes_config_map_v1.spark_pod_templates.metadata[0].name
          }
        }
      }
    }
  }

  timeouts {
    create = "15m"
    update = "15m"
  }

  depends_on = [
    kubernetes_job_v1.dbt,
    kubernetes_role_binding_v1.spark,
    kubernetes_config_map_v1.spark_pod_templates,
  ]

  lifecycle {
    replace_triggered_by = [terraform_data.run]
  }
}
