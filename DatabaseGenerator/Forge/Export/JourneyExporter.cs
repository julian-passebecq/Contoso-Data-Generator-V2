#nullable enable
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Generation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DatabaseGenerator.Forge.Export;

internal static class JourneyExporter
{
    internal static void Export(string root, StudioProjectSpec project)
    {
        var factory = Path.Combine(root, "factory");
        File.Copy(Path.Combine(factory, "run.py"), Path.Combine(factory, "legacy_run.py"));
        File.Copy(Path.Combine(factory, "motherduck.py"), Path.Combine(factory, "legacy_motherduck.py"));
        var templates = Path.Combine(AppContext.BaseDirectory, "Forge/Templates");
        ForgeIo.CopyTreeWithTokens(Path.Combine(templates, "v17"), factory, new Dictionary<string, string>());
        // Versioned overlays retain every V1.5/V1.6 template byte and reuse their business logic.
        var common = File.ReadAllText(Path.Combine(factory, "common.py"));
        common = common.Replace("if set(expected) != required or not required.issubset(actual):", "if not required.issubset(expected) or not required.issubset(actual):", StringComparison.Ordinal);
        ForgeIo.WriteText(Path.Combine(factory, "common.py"), common);
        var intent = project.Product!;
        var configPath = Path.Combine(factory, "ml/run_config.json");
        var config = JsonNode.Parse(File.ReadAllText(configPath))!;
        config["enabled"] = intent.Goal is "specific-ml" or "automl";
        config["target"] = intent.Analysis!.Runtime == "local" ? "local-sklearn" : intent.Analysis.Runtime + "-" + intent.Analysis.Kind;
        config["algorithm"] = intent.Analysis.Algorithm;
        ForgeIo.WriteText(configPath, config.ToJsonString());
        // The explicit algorithm is filtered within the preserved training function before fitting.
        var ml = Path.Combine(factory, "ml_lab.py");
        ForgeIo.WriteText(ml, File.ReadAllText(ml).Replace("    for name, estimator in algorithms(spec[\"problemType\"], config[\"seed\"]).items():",
            "    candidates = algorithms(spec[\"problemType\"], config[\"seed\"])\n    if config.get(\"algorithm\"):\n        candidates = {config[\"algorithm\"]: candidates[config[\"algorithm\"]]}\n    for name, estimator in candidates.items():", StringComparison.Ordinal));
        var spark = Path.Combine(factory, "spark_ml.py");
        ForgeIo.WriteText(spark, File.ReadAllText(spark).Replace("        metrics, predictions, fitted_models", "        if config.get(\"algorithm\"):\n            estimators = {config[\"algorithm\"]: estimators[config[\"algorithm\"]]}\n        metrics, predictions, fitted_models", StringComparison.Ordinal));
        ScopeSemantics(root, templates, project);
        if (intent.DbtIntegration == "cosmos")
        {
            var orchestration = Path.Combine(factory, "orchestration.py");
            var code = File.ReadAllText(orchestration)
                .Replace("for stage in (\"verify\", \"silver\", \"validate-silver\"):", "for stage in (\"verify\", \"bronze\", \"silver\", \"validate-silver\"):", StringComparison.Ordinal)
                .Replace("require(len(kpis) == 5 and all(k[\"matched\"] for k in kpis.values()),", "require(set(kpis) == {k[\"id\"] for k in read(root / \"models/kpi_catalog.json\")[\"kpis\"]} and all(k[\"matched\"] for k in kpis.values()),", StringComparison.Ordinal);
            var start = code.IndexOf("        config = read(root / \"factory/ml/run_config.json\")", StringComparison.Ordinal);
            var end = code.IndexOf("        from build_evidence import build", start, StringComparison.Ordinal);
            code = code[..start] + "        from run import planned\n        for stage in planned(root):\n            if stage not in (\"verify\", \"bronze\", \"silver\", \"validate-silver\", \"dbt\", \"reconcile\"):\n                execute(root, args.run_id, stage)\n" + code[end..];
            ForgeIo.WriteText(orchestration, code);
        }
    }

    private static void ScopeSemantics(string root, string templates, StudioProjectSpec project)
    {
        var intent = project.Product!;
        string ReadModel(string name) => File.ReadAllText(Path.Combine(templates, "customer_satisfaction/models", name))
            .Replace("__PROJECT_NAME__", project.SourceProject.Name, StringComparison.Ordinal)
            .Replace("__SCENARIO__", "retail.customer_satisfaction", StringComparison.Ordinal)
            .Replace("__EXPECTED_ORDER_COUNT__", project.SourceProject.Generation.Orders.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var catalog = JsonNode.Parse(ReadModel("kpi_catalog.json"))!;
        var selected = intent.SelectedKpis!.OrderBy(k => k, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var kpis = catalog["kpis"]!.AsArray().Where(k => selected.Contains(k!["id"]!.GetValue<string>())).Select(k => k!.DeepClone()).OrderBy(k => k["id"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        catalog["kpis"] = new JsonArray(kpis);
        catalog["version"] = "1.7";
        catalog["artifactStatus"] = "generated-not-executed";
        var models = Directory.GetFiles(Path.Combine(root, "factory/dbt/models"), "*.sql", SearchOption.AllDirectories).ToDictionary(p => Path.GetFileNameWithoutExtension(p)!, File.ReadAllText);
        var closure = new SortedSet<string>(StringComparer.Ordinal);
        var silver = new SortedSet<string>(StringComparer.Ordinal);
        void Visit(string name)
        {
            if (!closure.Add(name)) return;
            if (!models.TryGetValue(name, out var sql)) throw new InvalidOperationException("Missing governed dbt dependency " + name);
            foreach (Match m in Regex.Matches(sql, @"ref\(['""]([^'""]+)['""]\)")) Visit(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(sql, @"source\(['""]silver['""],\s*['""]([^'""]+)['""]\)")) silver.Add(m.Groups[1].Value);
        }
        // The governed KPI row is shared. Traverse actual dbt references, never UI formulas.
        if (selected.Count > 0) Visit("kpi_customer_satisfaction");
        var sourceNames = JsonNode.Parse(ReadModel("source_model.json"))!["entities"]!.AsArray().Select(e => Path.GetFileNameWithoutExtension(e!["file"]!.GetValue<string>())).ToHashSet();
        var source = silver.Where(sourceNames.Contains).ToList();
        if (silver.Contains("customer_scd2")) source.AddRange(new[] { "customers", "customer_cdc" });
        if (silver.Contains("quality_issues")) source.AddRange(new[] { "shipments", "reviews" });
        JsonArray Names(IEnumerable<string> values) => new(values.Distinct().OrderBy(s => s, StringComparer.Ordinal).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());
        var lineage = new JsonObject { ["version"] = "1.7", ["selectedKpis"] = Names(selected), ["requiredSourceEntities"] = Names(source),
            ["requiredBronzeTables"] = Names(source), ["requiredSilverTables"] = Names(silver), ["requiredDbtModels"] = Names(closure),
            ["canonicalGoldModel"] = "gold.kpi_customer_satisfaction", ["physicalPruning"] = false,
            ["scope"] = "Published KPIs are selected; source fixture and shared dbt dependencies are materialized in full." };
        var semantic = JsonNode.Parse(ReadModel("semantic_model.json"))!;
        semantic["version"] = "1.7";
        semantic["artifactStatus"] = "generated-not-executed";
        var tables = semantic["tables"]!.AsArray().Where(t => selected.Count > 0 && (t!["role"]!.GetValue<string>() == "dimension" || closure.Contains(t["name"]!.GetValue<string>()))).Select(t => t!.DeepClone()).ToArray();
        semantic["tables"] = new JsonArray(tables);
        var tableNames = tables.Select(t => t["name"]!.GetValue<string>()).ToHashSet();
        semantic["relationships"] = new JsonArray(semantic["relationships"]!.AsArray().Where(r => tableNames.Contains(r!["from"]!.GetValue<string>().Split('.')[0]) && tableNames.Contains(r["to"]!.GetValue<string>().Split('.')[0])).Select(r => r!.DeepClone()).ToArray());
        semantic["measures"] = new JsonArray(kpis.Select(k => (JsonNode)new JsonObject { ["id"] = k["id"]!.DeepClone(), ["name"] = k["name"]!.DeepClone(),
            ["table"] = "kpi_customer_satisfaction", ["column"] = k["id"]!.DeepClone(), ["format"] = k["format"]!.DeepClone(), ["truthMetric"] = k["id"]!.DeepClone() }).ToArray());
        if (selected.Count > 0) semantic["tables"]!.AsArray().Add(new JsonObject { ["name"] = "kpi_customer_satisfaction", ["role"] = "governed-kpi", ["key"] = "single-row" });
        semantic["source"] = new JsonObject { ["engine"] = ArchitecturePresets.Resolve(project).Settings.Warehouse, ["database"] = "run-bound warehouse; see semantic_execution.json", ["schema"] = "gold" };
        semantic["implementationNotes"] = new JsonArray("Measures select governed Gold columns. No copied formula or separate semantic aggregation.", "See lineage.json for actual shared dbt dependencies; physical pruning is not claimed.");
        ForgeIo.WriteText(Path.Combine(root, "models/kpi_catalog.json"), catalog.ToJsonString());
        ForgeIo.WriteText(Path.Combine(root, "models/semantic_model.json"), semantic.ToJsonString());
        ForgeIo.WriteText(Path.Combine(root, "models/lineage.json"), lineage.ToJsonString());
        ForgeIo.WriteText(Path.Combine(root, "models/query_examples.sql"), string.Join("\n", selected.OrderBy(k => k, StringComparer.Ordinal).Select(k => $"SELECT {k} FROM gold.kpi_customer_satisfaction;")) + "\n");
    }
}
