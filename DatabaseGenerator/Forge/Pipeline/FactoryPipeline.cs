#nullable enable
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Planning;
using System;
using System.Collections.Generic;

namespace DatabaseGenerator.Forge.Pipeline;

internal static class FactoryPipeline
{
    internal static bool IsLocal(ArchitectureSettings s) => s.Engine is "duckdb" or "polars" or "pandas" && s.Runtime == "local-process"
        && s.Storage == "local" && s.FileFormat == "parquet" && s.TableFormat == "none" && s.Warehouse == "duckdb"
        && s.Orchestrator is "none" or "local-sequential" or "airflow" or "airflow-docker";

    internal static PipelineDefinition Create(ResolvedProject project)
    {
        var pipeline = new PipelineDefinition { Id = "contoso_forge_factory", Name = $"Contoso Forge V{project.Product!.Version} Data Factory, ML Lab & BI Validation" };
        var specs = new List<(string Id, string Kind, string Operation)>
        {
            ("verify_source", "source", "verify"), ("transform_bronze_silver", "transform", "silver"),
            ("validate_silver", "validate", "validate-silver"), ("dbt_build", "dbt", "dbt"),
            ("truth_reconcile", "validate", "reconcile")
        };
        if (project.BusinessScenario == ScenarioCatalog.MlScenarioId)
            specs.Add(("ml_experiment", "ml", project.Product!.MlTarget == "local-sklearn" ? "ml" : "export-ml"));
        specs.Add(("bi_validation", "validate", "bi"));
        string? previous = null;
        foreach (var spec in specs)
        {
            pipeline.Activities.Add(new() { Id = spec.Id, Name = spec.Id.Replace('_', ' '), Kind = spec.Kind,
                Implementation = "factory-" + spec.Operation, DependsOn = previous is null ? new() : new() { previous } });
            previous = spec.Id;
        }
        pipeline.Annotations.Add("V1.5 opt-in: generation precedes this graph. Each run has isolated state, dbt artifacts and measured BI inputs.");
        return pipeline;
    }

    internal static bool Map(PipelineActivity activity, PipelinePlannedActivity mapped, Dictionary<string, string> settings)
    {
        if (settings.GetValueOrDefault("productVersion") is not ("1.5" or "1.6") || activity.Implementation?.StartsWith("factory-", StringComparison.Ordinal) != true) return false;
        var operation = activity.Implementation[8..];
        var expectedKind = operation switch { "verify" => "source", "silver" => "transform", "dbt" => "dbt", "ml" or "export-ml" => "ml", "validate-silver" or "reconcile" or "bi" => "validate", _ => "" };
        if (activity.Kind != expectedKind || activity.Inputs.Count != 0 || activity.Outputs.Count != 0
            || mapped.Engine is not ("duckdb" or "polars" or "pandas") || mapped.Engine != settings.GetValueOrDefault("engine")
            || mapped.Runtime != "local-process" || mapped.Source != "local" || mapped.Sink != "duckdb"
            || (activity.FileFormat ?? settings.GetValueOrDefault("fileFormat")) != "parquet"
            || (activity.TableFormat ?? settings.GetValueOrDefault("tableFormat")) != "none") return true;
        if (operation == "ml" && settings.GetValueOrDefault("mlTarget") != "local-sklearn") return true;
        mapped.Operation = activity.Implementation;
        mapped.Status = "generated-reference";
        mapped.Reason = operation switch
        {
            "verify" => "Bind source and compiled file SHA-256 hashes to an isolated run.",
            "silver" => $"Execute typed {mapped.Engine} Bronze/Silver with deterministic deduplication, CDC/SCD2, late-arrival flags and quarantine.",
            "validate-silver" => "Read persisted Silver and compare every row count to the independent C# truth manifest.",
            "dbt" => "Execute dbt staging, intermediate and Gold models/tests; retain manifest.json and run_results.json even on failure.",
            "reconcile" => "Read canonical Gold KPIs and independently compare with truth; require all dbt models/tests to pass.",
            "ml" => "Execute bounded scikit-learn candidates after maturity, chronological split, 14-day embargo and class checks.",
            "export-ml" => "Export the selected notebook/SQL experiment without claiming training.",
            "bi" => "Build the Evidence package from Gold, KPI/semantic/truth/run/dbt contracts and measured ML outputs when present.", _ => ""
        };
        return true;
    }
}
