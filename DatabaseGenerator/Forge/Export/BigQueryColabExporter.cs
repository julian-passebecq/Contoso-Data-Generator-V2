#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DatabaseGenerator.Forge.Generation;

namespace DatabaseGenerator.Forge.Export;

/// <summary>Additive native BigQuery and interactive Colab artifacts; never provisions or authenticates.</summary>
public static class BigQueryColabExporter
{
    public static void Export(string outputRoot, string resolvedProjectJson, string pipelineJson)
    {
        var resolved = JsonNode.Parse(resolvedProjectJson)?.AsObject()
            ?? throw new ArgumentException("A resolved project JSON object is required.", nameof(resolvedProjectJson));
        _ = JsonNode.Parse(pipelineJson)?.AsObject()
            ?? throw new ArgumentException("A pipeline JSON object is required.", nameof(pipelineJson));
        var settings = resolved["settings"]?.AsObject();
        if (settings?["warehouse"]?.GetValue<string>() != "bigquery" &&
            settings?["runtime"]?.GetValue<string>() != "google-colab")
            return;

        var root = Path.GetFullPath(outputRoot);
        var templateRoot = Path.Combine(AppContext.BaseDirectory, "Forge", "Templates", "free_gcp");
        foreach (var relative in new[]
                 {
                     "gcp/bigquery_runtime.py", "gcp/reconcile_kpis.sql", "gcp/requirements.txt", "gcp/FREE_GCP_README.md",
                     "colab/work_order.py", "colab/run_spark.py", "colab/contoso_free_gcp.ipynb",
                     "colab/work_order.schema.json", "colab/result_manifest.schema.json"
                 })
            ForgeIo.WriteText(Path.Combine(root, relative), File.ReadAllText(Path.Combine(templateRoot, relative)));

        var gcp = resolved["gcp"]?.DeepClone() ?? new JsonObject
        {
            ["projectId"] = "your-gcp-project", ["dataset"] = "contoso_forge",
            ["location"] = "US", ["maximumBytesBilled"] = 1_000_000_000L
        };
        var config = new JsonObject
        {
            ["contractVersion"] = "1.2",
            ["artifactStatus"] = "generated-reference",
            ["costProfile"] = settings?["costProfile"]?.DeepClone() ?? JsonValue.Create("gcp-sandbox-no-card"),
            ["warehouse"] = settings?["warehouse"]?.DeepClone(),
            ["runtime"] = settings?["runtime"]?.DeepClone(),
            ["gcp"] = gcp,
            ["nativeLoadFormats"] = new JsonArray("csv", "jsonl", "avro", "orc", "parquet"),
            ["writeDisposition"] = "WRITE_EMPTY",
            ["authentication"] = "application-default-credentials",
            ["maximumLocalFileBytes"] = 100_000_000L,
            ["cloudExecutionVerified"] = false
        };
        WriteJson(Path.Combine(root, "gcp", "bigquery_config.json"), config);
        var workOrder = new JsonObject
        {
            ["contractVersion"] = "1.2",
            ["artifactStatus"] = "experimental",
            ["status"] = "unstarted",
            ["datasetFingerprint"] = resolved["datasetFingerprint"]?.DeepClone() ?? JsonValue.Create(""),
            ["instructions"] = "Run python colab/work_order.py package --root . --run-id <unique-run> to issue a work order and uploadable ZIP."
        };
        WriteJson(Path.Combine(root, "colab", "work_order.template.json"), workOrder);
    }

    private static void WriteJson(string path, JsonObject value) =>
        ForgeIo.WriteText(path, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}
