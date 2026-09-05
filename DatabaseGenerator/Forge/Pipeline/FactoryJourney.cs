#nullable enable
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Planning;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DatabaseGenerator.Forge.Pipeline;

internal static partial class FactoryPipeline
{
    internal static readonly Dictionary<string, (string Kind, string Stage)> JourneyOperations = new()
    {
        ["verify"] = ("source", "generate"), ["bronze"] = ("transform", "bronze"),
        ["silver"] = ("transform", "silver"), ["validate-silver"] = ("validate", "silver"),
        ["wrangle"] = ("transform", "silver"), ["dbt"] = ("dbt", "gold"), ["reconcile"] = ("validate", "gold"),
        ["semantic"] = ("validate", "semantic"), ["analysis"] = ("ml", "analysis"),
        ["bi"] = ("validate", "report"), ["publish"] = ("sink", "publish")
    };
    internal static PipelineDefinition CreateJourney(ResolvedProject project)
    {
        var intent = project.Product!;
        var stop = Array.IndexOf(ProductIntent.StopStages, intent.StopAfter);
        var pipeline = new PipelineDefinition { Id = "contoso_forge_factory", Name = "Contoso Forge V1.7 · " + intent.Goal,
            Annotations = new() { "One goal-driven graph. Generation precedes verify; stopAfter bounds executable operations. Shared source/dbt dependencies may still materialize." } };
        string? previous = null;
        foreach (var (operation, spec) in JourneyOperations)
        {
            if (Array.IndexOf(ProductIntent.StopStages, spec.Stage) > stop || operation == "wrangle" && intent.Recipe is null) continue;
            var id = operation.Replace('-', '_');
            pipeline.Activities.Add(new() { Id = id, Name = spec.Stage + " / " + operation, Kind = spec.Kind,
                Implementation = "factory-" + operation, DependsOn = previous is null ? new() : new() { previous },
                TimeoutSeconds = operation == "analysis" ? intent.Analysis!.TimeoutSeconds + 120 : 3600 });
            previous = id;
        }
        return pipeline;
    }
    internal static bool MapJourney(PipelineActivity activity, PipelinePlannedActivity mapped, Dictionary<string, string> settings)
    {
        if (activity.Implementation?.StartsWith("factory-", StringComparison.Ordinal) != true) return false;
        var op = activity.Implementation[8..];
        if (!JourneyOperations.TryGetValue(op, out var spec) || activity.Kind != spec.Kind || activity.Inputs.Count != 0 || activity.Outputs.Count != 0
            || mapped.Engine != settings.GetValueOrDefault("engine") || mapped.Runtime != settings.GetValueOrDefault("runtime")
            || mapped.Source != settings.GetValueOrDefault("storage") || mapped.Sink != settings.GetValueOrDefault("warehouse")) return true;
        if (Array.IndexOf(ProductIntent.StopStages, spec.Stage) > Array.IndexOf(ProductIntent.StopStages, settings.GetValueOrDefault("stopAfter"))) return true;
        if (op != "verify" && (mapped.Runtime != "local-process" || mapped.Source != "local" || mapped.Sink is not ("none" or "duckdb" or "motherduck")
            || (activity.FileFormat ?? settings.GetValueOrDefault("fileFormat")) != "parquet" || (activity.TableFormat ?? settings.GetValueOrDefault("tableFormat")) != "none")) return true;
        if (op == "wrangle" && mapped.Engine is not ("duckdb" or "polars")) return true;
        if (mapped.Sink == "motherduck" && settings.GetValueOrDefault("dbtIntegration") == "cosmos") return true;
        mapped.Operation = activity.Implementation;
        mapped.Status = "generated-reference";
        mapped.Reason = $"V1.7 {spec.Stage}: execute the compiled {op} adapter with run/input/output identity; generated packages do not establish external execution.";
        return true;
    }
}
