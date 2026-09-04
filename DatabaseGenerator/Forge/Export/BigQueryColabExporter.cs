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
        var pipeline = JsonNode.Parse(pipelineJson)?.AsObject()
            ?? throw new ArgumentException("A pipeline JSON object is required.", nameof(pipelineJson));
        var settings = resolved["settings"]?.AsObject();
        if (settings?["warehouse"]?.GetValue<string>() != "bigquery" &&
            !(settings?["runtime"]?.GetValue<string>()?.StartsWith("google-colab", StringComparison.Ordinal) ?? false))
            return;

        var root = Path.GetFullPath(outputRoot);
        var templateRoot = Path.Combine(AppContext.BaseDirectory, "Forge", "Templates", "free_gcp");
        foreach (var relative in new[]
                 {
                     "gcp/bigquery_runtime.py", "gcp/reconcile_kpis.sql", "gcp/requirements.txt", "gcp/FREE_GCP_README.md",
                     "colab/work_order.py", "colab/run_spark.py", "colab/contoso_free_gcp.ipynb",
                     "colab/spark_session.py", "colab/storage_adapter.py", "colab/bootstrap_runtime.py",
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
        var runtime = settings?["runtime"]?.GetValue<string>();
        JsonObject? workActivity = null;
        if (pipeline["activities"] is JsonArray activities)
            foreach (var item in activities)
                if (item is JsonObject activity && activity["implementation"]?.GetValue<string>() == "colab-work-order")
                {
                    if (workActivity is not null) throw new ArgumentException("The Colab exporter requires one work-order activity.");
                    workActivity = activity;
                }
        runtime = workActivity?["runtime"]?.GetValue<string>() ?? runtime;
        var sparkConfig = new JsonObject
        {
            ["contractVersion"] = "1.3",
            ["sparkApiMode"] = workActivity?["sparkApiMode"]?.DeepClone() ?? JsonValue.Create(runtime switch
            {
                "google-colab-connect-local" => "connect-local", "google-colab-connect-remote" => "connect-remote",
                _ => settings?["sparkApiMode"]?.GetValue<string>() ?? "classic"
            }),
            ["sparkVersionPolicy"] = workActivity?["sparkVersionPolicy"]?.DeepClone() ?? settings?["sparkVersionPolicy"]?.DeepClone() ?? JsonValue.Create("colab-native"),
            ["sparkVersion"] = workActivity?["sparkVersion"]?.DeepClone() ?? settings?["sparkVersion"]?.DeepClone() ?? JsonValue.Create("4.0.4")
        };
        var sparkRemote = workActivity?["sparkRemote"] ?? settings?["sparkRemote"];
        if (sparkRemote is not null) sparkConfig["sparkRemote"] = sparkRemote.DeepClone();
        WriteJson(Path.Combine(root, "colab", "spark_config.json"), sparkConfig);
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
