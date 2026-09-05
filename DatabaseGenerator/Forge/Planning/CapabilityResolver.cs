#nullable enable
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Pipeline;
using System;

namespace DatabaseGenerator.Forge.Planning;

/// <summary>Conservative evidence policy. These are versioned adapter observations, not environment probes.</summary>
public static class CapabilityResolver
{
    // Mirrors the explicit generated bootstrap compatibility set, not all syntactically valid Spark releases.
    public static bool SupportsColabVersion(string? mode, string? version) =>
        mode == "classic" && version is "3.5.9" or "4.0.4"
        || mode is "connect-local" or "connect-remote" && version == "4.0.4";

    public static bool HasHostedSparkEvidence(PipelinePlannedActivity activity, ArchitectureSettings settings) =>
        activity.Engine == "spark" && activity.Source == "local" && activity.Sink == "bigquery"
        && activity.Runtime is "google-colab" or "google-colab-connect-local"
        && activity.SparkApiMode is "classic" or "connect-local" && activity.SparkVersion == "4.0.4"
        && settings.FileFormat == "parquet" && settings.TableFormat == "none";

    public static PlanEvidence Historical(string id, string level, string scope) => new()
    {
        Id = id, ValidationLevel = level, Reference = "docs/v1.3-handoff.json",
        Scope = "V1.3 historical adapter observation. " + scope + " This planned project has not been executed."
    };

    public static void Observed(PlanStage stage, string id, string level, string scope)
    {
        stage.ImplementationStatus = "executed";
        stage.ValidationLevel = level;
        stage.Evidence.Add(Historical(id, level, scope));
    }

    public static void ResolveActivity(PlanStage stage, PipelinePlannedActivity mapping, ArchitectureSettings settings)
    {
        if (mapping.Operation == "unsupported")
        {
            stage.ImplementationStatus = IsReference(settings, mapping) ? "reference-only" : "unsupported";
            stage.ValidationLevel = "declared";
            return;
        }
        stage.ImplementationStatus = "runnable";
        stage.ValidationLevel = "generated";
        if (mapping.Operation == "verify-source")
        {
            stage.Engine = "python";
            stage.Runtime = "local-process";
            stage.SparkApiMode = null;
            stage.SparkVersion = null;
            Observed(stage, "source-hash-verification", "tested", "Generated CSV source hashes were verified against the truth manifest.");
        }
        else if (HasHostedSparkEvidence(mapping, settings))
        {
            if (mapping.Operation == "prepare-colab")
                Observed(stage, "colab-work-order-identity", "tested", "Classic and Connect-local work orders bound source hashes and requested Spark mode to returned evidence.");
            else if (mapping.SparkApiMode == "classic")
                Observed(stage, "classic-bigquery-result-adoption", "reconciled", "Classic hosted Colab + native BigQuery result was accepted by local Minikube Airflow using a local Git server. Public GitHub GitSync was not established by this evidence.");
            else
            {
                stage.Evidence.Add(Historical("connect-spark-result-adoption", "tested", "Connect-local Spark-only return was imported successfully; full Connect + BigQuery + Airflow composition remains unexecuted."));
                stage.Reason += " Full Connect + BigQuery result adoption is generated; Spark-only Connect evidence is separate.";
            }
        }
    }

    private static bool IsReference(ArchitectureSettings settings, PipelinePlannedActivity mapping) =>
        mapping.Engine is "duckdb" or "polars" or "pandas"
        || mapping.Runtime is "fabric-spark" or "databricks-spark" or "google-colab-connect-remote"
        || mapping.Source is "azure-adls" or "fabric-onelake" or "r2" or "seaweedfs" or "b2" or "s3"
        || mapping.Sink is "motherduck" or "biglake" or "fabric" or "databricks" or "sqlserver"
        || settings.TableFormat == "iceberg";
}
