#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using DatabaseGenerator.Forge.Generation;

namespace DatabaseGenerator.Forge.Export;

/// <summary>Optional dbt/ML artifacts alongside the unchanged V1 DuckDB project.</summary>
public static class BigQueryAnalyticsExporter
{
    public static void Export(string outputRoot, string resolvedProjectJson)
    {
        var resolved = JsonNode.Parse(resolvedProjectJson)!;
        if (resolved["settings"]?["warehouse"]?.GetValue<string>() != "bigquery") return;
        var templates = Path.Combine(AppContext.BaseDirectory, "Forge", "Templates", "free_gcp");
        foreach (var directory in new[] { "dbt_bigquery", "bqml" })
        {
            var source = Path.Combine(templates, directory);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                         .Where(p => Path.GetExtension(p) is ".sql" or ".yml" or ".py" or ".md" or ".txt")
                         .Where(p => !Path.GetRelativePath(source, p).Split(Path.DirectorySeparatorChar)
                             .Any(part => part is "target" or "logs" or "dbt_packages" or "__pycache__")))
                ForgeIo.WriteText(Path.Combine(outputRoot, directory, Path.GetRelativePath(source, file)), File.ReadAllText(file));
        }
        var truthPath = Path.Combine(outputRoot, "truth_manifest.json");
        var tests = new StringBuilder("-- Exact generator expectations; populated when source data is generated.\n");
        if (File.Exists(truthPath))
        {
            var truth = JsonNode.Parse(File.ReadAllText(truthPath))!;
            var comparisons = truth["expectedKpis"]!.AsObject().Select(kpi =>
                $"SELECT '{kpi.Key}' AS metric, CAST({kpi.Value!.ToJsonString()} AS NUMERIC) AS expected_value, CAST({kpi.Key} AS NUMERIC) AS actual_value FROM {{{{ ref('kpi_customer_satisfaction') }}}}");
            tests.Append("WITH comparisons AS (\n").AppendJoin("\nUNION ALL\n", comparisons)
                .Append("\n) SELECT * FROM comparisons WHERE actual_value IS NULL OR ABS(actual_value - expected_value) > 0.000001\n");
        }
        else tests.Append("{{ exceptions.raise_compiler_error('Generate Forge source data and truth_manifest.json before building BigQuery Gold.') }}\n");
        ForgeIo.WriteText(Path.Combine(outputRoot, "dbt_bigquery", "tests", "reconcile_truth.sql"), tests.ToString());
    }
}
