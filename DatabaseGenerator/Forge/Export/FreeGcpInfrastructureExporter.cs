#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using DatabaseGenerator.Forge.Generation;

namespace DatabaseGenerator.Forge.Export;

/// <summary>Exports infrastructure independently of generation, processing, and pipeline execution.</summary>
public static class FreeGcpInfrastructureExporter
{
    public const string AirflowChartVersion = "1.22.0";
    public const string AirflowVersion = "3.2.2";
    public const string GoogleProviderVersion = "7.45.0";

    public static void Export(string outputRoot, string resolvedProjectJson, string pipelineJson)
    {
        using var project = JsonDocument.Parse(resolvedProjectJson);
        using var pipeline = JsonDocument.Parse(pipelineJson);
        var root = project.RootElement;
        var settings = root.GetProperty("settings");
        var iac = Value(settings, "iac", "opentofu");
        if (iac is not ("none" or "opentofu" or "terraform-community" or "dual-validate"))
            throw new ArgumentException($"Unsupported infrastructure engine '{iac}'.");

        var templateRoot = Path.Combine(AppContext.BaseDirectory, "Forge", "Templates", "free_gcp");
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        var costProfile = Value(settings, "costProfile", "gcp-sandbox-no-card");
        var storage = Value(settings, "storage", "local");
        if (storage == "gcs" && costProfile != "gcp-free-tier-billing-enabled")
            throw new ArgumentException("GCS infrastructure requires gcp-free-tier-billing-enabled; the no-card sandbox has no GCS dependency.");

        if (Value(settings, "orchestrator", "none") == "airflow-minikube")
        {
            var git = root.TryGetProperty("git", out var gitNode) ? gitNode : default;
            var repo = Value(git, "repository", "https://github.com/your-account/your-repository.git");
            var branch = Value(git, "branch", "main");
            var subPath = Value(git, "subPath", "generated/airflow/dags");
            var projectSubPath = Value(git, "projectSubPath", "generated");
            // Values are passed through Helm's tpl function. JSON quoting alone cannot escape templates.
            if (!Uri.TryCreate(repo, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("GitSync repository must be an HTTPS URL without embedded credentials, query, or fragment.");
            foreach (var value in new[] { repo, branch, subPath, projectSubPath })
                if (value.Any(char.IsControl) || value.Contains("{{", StringComparison.Ordinal) || value.Contains("}}", StringComparison.Ordinal))
                    throw new ArgumentException("GitSync configuration cannot contain control characters or Helm template expressions.");
            if (string.IsNullOrWhiteSpace(branch) || new[] { subPath, projectSubPath }.Any(path =>
                    Path.IsPathRooted(path) || path.Split('/', '\\').Contains("..") || path.Contains('\\') || path.Contains(':')))
                throw new ArgumentException("GitSync requires a branch and repository-relative paths without traversal.");
            tokens["__GIT_REPOSITORY_JSON__"] = JsonValue.Create(repo)!.ToJsonString();
            tokens["__GIT_BRANCH_JSON__"] = JsonValue.Create(branch)!.ToJsonString();
            tokens["__GIT_SUBPATH_JSON__"] = JsonValue.Create(subPath)!.ToJsonString();
            tokens["__PROJECT_ROOT_JSON__"] = JsonValue.Create("/opt/airflow/dags/repo/" + projectSubPath.Trim('/'))!.ToJsonString();
            ForgeIo.CopyTreeWithTokens(Path.Combine(templateRoot, "minikube"), Path.Combine(outputRoot, "minikube"), tokens);
            WriteStatus(Path.Combine(outputRoot, "minikube", "validation_status.json"), "airflow-minikube", iac);
        }

        if (iac != "none" && (Value(settings, "warehouse", "none") == "bigquery" || storage == "gcs"))
        {
            var gcp = root.TryGetProperty("gcp", out var gcpNode) ? gcpNode : default;
            var infraRoot = Path.Combine(outputRoot, "infra", "gcp");
            ForgeIo.CopyTreeWithTokens(Path.Combine(templateRoot, "infra"), infraRoot, tokens);
            var variableValues = new JsonObject
            {
                ["project_id"] = Value(gcp, "projectId", "your-gcp-project"),
                ["dataset_id"] = Value(gcp, "dataset", "contoso_forge"),
                ["location"] = Value(gcp, "location", "US"),
                ["cost_profile"] = costProfile,
                ["create_bigquery_dataset"] = Value(settings, "warehouse", "none") == "bigquery",
                ["create_gcs_bucket"] = storage == "gcs",
                ["bucket_name"] = Value(gcp, "bucketName", ""),
                ["dataset_iam_members"] = gcp.ValueKind == JsonValueKind.Object && gcp.TryGetProperty("iamMembers", out var members)
                    ? JsonNode.Parse(members.GetRawText()) : new JsonArray(),
                ["tables"] = new JsonObject()
            };
            ForgeIo.WriteText(Path.Combine(infraRoot, "forge.auto.tfvars.json"), variableValues.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            WriteStatus(Path.Combine(infraRoot, "validation_status.json"), "gcp-infrastructure", iac);
        }
    }

    private static string Value(JsonElement node, string name, string fallback) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;

    private static void WriteStatus(string path, string target, string iac)
    {
        var status = new JsonObject
        {
            ["contractVersion"] = "1.2",
            ["target"] = target,
            ["status"] = "generated-reference",
            ["iac"] = iac,
            ["staticValidation"] = "not-run-for-this-export",
            ["runtimeValidation"] = "not-run",
            ["cloudApplied"] = false,
            ["airflowChartVersion"] = AirflowChartVersion,
            ["airflowVersion"] = AirflowVersion,
            ["googleProviderVersion"] = GoogleProviderVersion,
            ["validationCommand"] = "python scripts/validate_free_gcp_infra.py --project <generated-project> --iac " + iac,
            ["note"] = "Run the validation command to record separate CLI evidence; rendering and provider validation do not prove a deployment."
        };
        ForgeIo.WriteText(path, status.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
