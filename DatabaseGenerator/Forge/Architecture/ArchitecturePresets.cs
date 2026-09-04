#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DatabaseGenerator.Forge.Architecture;

public static class ArchitecturePresets
{
    public const string DefaultPresetId = "free-gcp-lab";

    // Return fresh objects: editing a selected preset must never mutate the registry.
    public static List<ArchitecturePreset> List() => new()
    {
        Preset("free-gcp-lab", "Free GCP Lab", "spark", "google-colab", "airflow-minikube", "local", "none", "bigquery", "gcp-sandbox-no-card"),
        Preset("free-gcp-full", "GCP billing-enabled free-usage lab", "spark", "google-colab", "airflow-minikube", "gcs", "none", "bigquery", "gcp-free-tier-billing-enabled"),
        Preset("local-spark", "Existing V1 Spark / Docker reference", "spark", "docker", "airflow-docker", "local", "delta", "duckdb", "local"),
        Preset("local-fast", "Local DuckDB reference contract", "duckdb", "local-process", "local-sequential", "local", "none", "duckdb", "local"),
        Preset("azure-adls-airflow", "Azure Data Lake / Airflow reference", "spark", "google-colab", "airflow-minikube", "azure-adls", "none", "none", "external"),
        Preset("fabric-lakehouse", "Fabric Lakehouse reference", "spark", "fabric-spark", "fabric", "fabric-onelake", "delta", "fabric", "external"),
        Preset("databricks-free", "Databricks reference", "spark", "databricks-spark", "databricks-jobs", "local", "delta", "databricks", "external"),
        Preset("sqlserver-bi", "SQL Server reference", "spark", "docker", "local-sequential", "local", "none", "sqlserver", "external"),
        Preset("open-lakehouse-iceberg", "Open Iceberg lakehouse reference", "spark", "docker", "local-sequential", "seaweedfs", "iceberg", "none", "local")
    };

    public static ArchitecturePreset Get(string id) => List().SingleOrDefault(p => p.PresetId == id)
        ?? throw new ArgumentException($"Unknown architecture preset '{id}'. Use 'forge presets list'.");

    public static ResolvedProject Resolve(StudioProjectSpec project, string fingerprint = "")
    {
        if (project.Version != "1.2.0") throw new ArgumentException("Studio project version must be 1.2.0.");
        if (project.Architecture?.Overrides is null) throw new ArgumentException("architecture.overrides cannot be null.");
        var preset = Get(project.Architecture.PresetId);
        var d = preset.Defaults;
        var o = project.Architecture.Overrides;
        var settings = new ArchitectureSettings
        {
            Engine = o.Engine ?? d.Engine, Runtime = o.Runtime ?? d.Runtime,
            Orchestrator = o.Orchestrator ?? d.Orchestrator, DagSource = o.DagSource ?? d.DagSource,
            Storage = o.Storage ?? d.Storage, FileFormat = o.FileFormat ?? d.FileFormat,
            TableFormat = o.TableFormat ?? d.TableFormat, Warehouse = o.Warehouse ?? d.Warehouse,
            Iac = o.Iac ?? d.Iac, CostProfile = o.CostProfile ?? d.CostProfile
        };
        Validate(settings, project.Gcp, project.Git);
        var result = new ResolvedProject
        {
            PresetId = preset.PresetId, Name = project.SourceProject.Name, Settings = settings,
            Gcp = project.Gcp, Git = project.Git, DatasetFingerprint = fingerprint
        };
        if (settings.Runtime!.StartsWith("google-colab", StringComparison.Ordinal))
            result.Notes.Add("Colab is an interactive, ephemeral Spark runtime; a work order and verified manual result are required.");
        if (settings.CostProfile == "gcp-sandbox-no-card")
            result.Notes.Add("BigQuery Sandbox: native batch files and manual handoff; no GCS or billing-dependent resources. IaC apply is optional and not promised.");
        if (settings.CostProfile == "gcp-free-tier-billing-enabled")
            result.Notes.Add("Billing is enabled. Free allowances are limits, not a guarantee against charges. Queries are capped by maximumBytesBilled.");
        result.Notes.Add("Preset settings are editable deployment intent. Business generation and the V1 reference remain unchanged.");
        return result;
    }

    public static string ToJson(ResolvedProject project) => JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.ResolvedProject);

    private static ArchitecturePreset Preset(string id, string name, string engine, string runtime,
        string orchestration, string storage, string tableFormat, string warehouse, string cost) => new()
    {
        PresetId = id, DisplayName = name,
        Defaults = new ArchitectureSettings
        {
            Engine = engine, Runtime = runtime, Orchestrator = orchestration,
            DagSource = orchestration == "airflow-minikube" ? "github-gitsync" : "local",
            Storage = storage, FileFormat = "parquet", TableFormat = tableFormat,
            Warehouse = warehouse, Iac = "opentofu", CostProfile = cost
        },
        CapabilityRequirements = runtime == "google-colab"
            ? new() { "batch", "manual-external-checkpoint", "truth-reconciliation" }
            : new() { "batch", "truth-reconciliation" }
    };

    private static void Choice(string? value, string name, params string[] choices)
    {
        if (value is null || !choices.Contains(value, StringComparer.Ordinal))
            throw new ArgumentException($"Unsupported {name} '{value}'. Choices: {string.Join(", ", choices)}.");
    }

    private static void Validate(ArchitectureSettings s, GcpOptions gcp, GitOptions git)
    {
        Choice(s.Engine, "engine", "spark", "duckdb", "polars", "pandas");
        Choice(s.Runtime, "runtime", "google-colab", "google-colab-connect-local", "google-colab-connect-remote", "docker", "local-process", "databricks-spark", "fabric-spark", "kubernetes");
        Choice(s.Orchestrator, "orchestrator", "none", "local-sequential", "airflow-docker", "airflow-minikube", "gcp-workflows", "databricks-jobs", "fabric", "adf");
        Choice(s.DagSource, "dagSource", "github-gitsync", "local");
        Choice(s.Storage, "storage", "local", "gcs", "azure-adls", "fabric-onelake", "r2", "seaweedfs", "b2", "s3");
        Choice(s.FileFormat, "fileFormat", "csv", "jsonl", "avro", "orc", "parquet");
        Choice(s.TableFormat, "tableFormat", "none", "delta", "iceberg");
        Choice(s.Warehouse, "warehouse", "none", "bigquery", "biglake", "sqlserver", "duckdb", "motherduck", "fabric", "databricks");
        Choice(s.Iac, "iac", "none", "opentofu", "terraform-community", "dual-validate");
        Choice(s.CostProfile, "costProfile", "local", "external", "gcp-sandbox-no-card", "gcp-free-tier-billing-enabled");
        if (s.Orchestrator == "airflow-minikube" && s.DagSource != "github-gitsync")
            throw new ArgumentException("The implemented airflow-minikube preset requires dagSource=github-gitsync.");
        if (s.Engine != "spark" && (s.Runtime!.StartsWith("google-colab", StringComparison.Ordinal) || s.Runtime is "databricks-spark" or "fabric-spark"))
            throw new ArgumentException("The selected Spark runtime requires engine=spark.");
        if (s.TableFormat != "none" && s.FileFormat != "parquet")
            throw new ArgumentException("Delta and Iceberg are table formats over Parquet, not native batch-file formats.");
        if (s.Warehouse == "bigquery" && s.TableFormat != "none")
            throw new ArgumentException("Native BigQuery loading requires tableFormat=none. Select a separately supported BigLake integration for open table formats.");
        if (s.CostProfile == "gcp-sandbox-no-card" && (s.Storage == "gcs" || s.Warehouse == "biglake" || s.Orchestrator == "gcp-workflows"))
            throw new ArgumentException("gcp-sandbox-no-card cannot require GCS, BigLake, or Workflows. Select the billing-enabled cost profile.");
        if ((s.Storage == "gcs" || s.Warehouse == "biglake" || s.Orchestrator == "gcp-workflows") && s.CostProfile != "gcp-free-tier-billing-enabled")
            throw new ArgumentException("GCP storage/external/workflow resources require gcp-free-tier-billing-enabled.");
        if (s.Warehouse is "bigquery" or "biglake")
        {
            if (s.CostProfile is not ("gcp-sandbox-no-card" or "gcp-free-tier-billing-enabled"))
                throw new ArgumentException("BigQuery requires an explicit GCP cost profile.");
            if (gcp is null || !Regex.IsMatch(gcp.ProjectId ?? "", "^[a-z][a-z0-9-]{4,28}[a-z0-9]$") ||
                !Regex.IsMatch(gcp.Dataset ?? "", "^[A-Za-z_][A-Za-z0-9_]{0,1023}$") ||
                !Regex.IsMatch(gcp.Location ?? "", "^[A-Za-z][A-Za-z0-9-]{0,62}$") || gcp.MaximumBytesBilled <= 0)
                throw new ArgumentException("GCP requires valid projectId/dataset/location and a positive maximumBytesBilled.");
        }
        if (git is null || !Uri.TryCreate(git.Repository, UriKind.Absolute, out var repository) ||
            repository.Scheme != "https" || repository.UserInfo.Length != 0 || repository.Query.Length != 0 || repository.Fragment.Length != 0)
            throw new ArgumentException("git.repository must be a credential-free HTTPS URL.");
        if (!Regex.IsMatch(git.Branch ?? "", "^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$") || git.Branch!.Contains("..", StringComparison.Ordinal) ||
            !Regex.IsMatch(git.SubPath ?? "", "^[A-Za-z0-9][A-Za-z0-9._/-]{0,255}$") || git.SubPath!.Split('/').Contains("..") ||
            !Regex.IsMatch(git.ProjectSubPath ?? "", "^[A-Za-z0-9][A-Za-z0-9._/-]{0,255}$") || git.ProjectSubPath!.Split('/').Contains(".."))
            throw new ArgumentException("Git branch/subPath must be safe relative references without traversal.");
    }
}
