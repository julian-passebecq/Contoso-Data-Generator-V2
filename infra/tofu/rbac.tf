resource "kubernetes_service_account_v1" "spark" {
  metadata {
    name      = "contoso-forge-spark"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels    = local.common_labels
  }
}

resource "kubernetes_role_v1" "spark" {
  metadata {
    name      = "contoso-forge-spark"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels    = local.common_labels
  }

  rule {
    api_groups = [""]
    resources  = ["pods", "services", "configmaps", "persistentvolumeclaims"]
    verbs      = ["create", "delete", "deletecollection", "get", "list", "patch", "update", "watch"]
  }

  rule {
    api_groups = [""]
    resources  = ["pods/log"]
    verbs      = ["get", "list"]
  }
}

resource "kubernetes_role_binding_v1" "spark" {
  metadata {
    name      = "contoso-forge-spark"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels    = local.common_labels
  }

  role_ref {
    api_group = "rbac.authorization.k8s.io"
    kind      = "Role"
    name      = kubernetes_role_v1.spark.metadata[0].name
  }

  subject {
    kind      = "ServiceAccount"
    name      = kubernetes_service_account_v1.spark.metadata[0].name
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
  }
}
