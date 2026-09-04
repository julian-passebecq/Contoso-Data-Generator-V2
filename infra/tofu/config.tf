locals {
  project_spec = file("${path.module}/../../examples/customer-satisfaction.project.json")

  common_labels = {
    "app.kubernetes.io/name"       = "contoso-forge"
    "app.kubernetes.io/managed-by" = "opentofu"
  }

  run_labels = merge(local.common_labels, {
    "contoso-forge/run-id" = var.run_id
  })
}

resource "terraform_data" "run" {
  input = var.run_id
}

resource "kubernetes_config_map_v1" "forge_project" {
  metadata {
    name      = "contoso-forge-project"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels    = local.common_labels
  }

  data = {
    "project.json" = local.project_spec
  }
}

resource "kubernetes_config_map_v1" "dbt_validation" {
  metadata {
    name      = "contoso-forge-dbt-validation"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels    = local.common_labels
  }

  data = {
    "validate.py" = <<-PYTHON
      import hashlib
      import json
      from pathlib import Path

      workspace = Path("/workspace")
      truth_path = workspace / "out" / "truth_manifest.json"
      truth = json.loads(truth_path.read_text(encoding="utf-8"))

      if truth.get("artifactStatus") != "validated":
          raise SystemExit("truth manifest is not validated")

      raw_root = workspace / "lake" / "raw"
      expected_hashes = truth.get("sourceFileSha256", {})
      if len(expected_hashes) < 11:
          raise SystemExit(f"expected at least 11 source hashes, found {len(expected_hashes)}")

      for name, expected in expected_hashes.items():
          source = raw_root / name
          if not source.is_file():
              raise SystemExit(f"missing shared raw file: {source}")
          actual = hashlib.sha256(source.read_bytes()).hexdigest()
          if actual != expected:
              raise SystemExit(f"hash mismatch for {source}: {actual} != {expected}")

      dbt_manifest_path = workspace / "out" / "dbt" / "target" / "manifest.json"
      dbt_manifest = json.loads(dbt_manifest_path.read_text(encoding="utf-8"))
      nodes = dbt_manifest.get("nodes", {}).values()
      model_count = sum(node.get("resource_type") == "model" for node in nodes)
      test_count = sum(node.get("resource_type") == "test" for node in nodes)
      if model_count < 24 or test_count < 5:
          raise SystemExit(f"dbt parse produced too few nodes: models={model_count}, tests={test_count}")

      source_rows = sum(truth.get("sourceRowCounts", {}).values())
      print(
          "DBT_PROJECT_VALIDATED "
          f"raw_files={len(expected_hashes)} source_rows={source_rows} "
          f"models={model_count} tests={test_count}"
      )
    PYTHON
  }
}

resource "kubernetes_config_map_v1" "spark_pod_templates" {
  metadata {
    name      = "contoso-forge-spark-pod-templates"
    namespace = kubernetes_namespace_v1.forge.metadata[0].name
    labels    = local.common_labels
  }

  data = {
    "driver.yaml" = <<-YAML
      apiVersion: v1
      kind: Pod
      metadata:
        labels:
          contoso-forge-role: spark-driver
      spec:
        restartPolicy: Never
        containers:
          - name: spark-kubernetes-driver
            command:
              - /opt/entrypoint.sh
            resources:
              requests:
                cpu: 100m
                memory: 512Mi
              limits:
                cpu: "1"
                memory: 1Gi
            volumeMounts:
              - name: spark-pod-templates
                mountPath: /etc/contoso-forge/spark
                readOnly: true
        volumes:
          - name: spark-pod-templates
            configMap:
              name: contoso-forge-spark-pod-templates
      YAML

    "executor.yaml" = <<-YAML
      apiVersion: v1
      kind: Pod
      metadata:
        labels:
          contoso-forge-role: spark-executor
      spec:
        restartPolicy: Never
        containers:
          - name: spark-kubernetes-executor
            command:
              - /opt/entrypoint.sh
            resources:
              requests:
                cpu: 100m
                memory: 512Mi
              limits:
                cpu: "1"
                memory: 1Gi
      YAML
  }
}
