using System.Text.Json;
using DatabaseGenerator.Forge.Export;
using DatabaseGenerator.Forge.Generation;

namespace DatabaseGenerator.Tests;

public sealed class BigQueryColabTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "forge-bigquery-tests-" + Guid.NewGuid().ToString("N"));
    private const string Resolved = """
        {"contractVersion":"1.2","presetId":"free-gcp-lab","datasetFingerprint":"unchanged-fingerprint",
         "settings":{"runtime":"google-colab","warehouse":"bigquery","costProfile":"gcp-sandbox-no-card"},
         "gcp":{"projectId":"your-gcp-project","dataset":"contoso_forge","location":"US","maximumBytesBilled":1000000000}}
        """;

    [Fact]
    public async Task ExportPreservesValidatedV1ArtifactsAndTruthBytes()
    {
        await new ForgeProjectGenerator().GenerateAsync(ForgeTestProject.CreateSmallSpec(), root);
        var preserved = new[] { "truth_manifest.json", "pyspark/bronze_silver.py", "gcp/README.md", "dbt/models/gold/fact_sales.sql" }
            .ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(root, path)));
        BigQueryColabExporter.Export(root, Resolved, "{\"version\":\"1.2\"}");
        foreach (var pair in preserved)
            Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(root, pair.Key)));
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "gcp/bigquery_config.json")));
        Assert.Equal("WRITE_EMPTY", config.RootElement.GetProperty("writeDisposition").GetString());
        Assert.False(config.RootElement.GetProperty("cloudExecutionVerified").GetBoolean());
        Assert.Equal(new[] { "csv", "jsonl", "avro", "orc", "parquet" },
            config.RootElement.GetProperty("nativeLoadFormats").EnumerateArray().Select(value => value.GetString()));
        Assert.False(File.Exists(Path.Combine(root, "colab/result_manifest.json")));
    }

    [Fact]
    public void CompileWithoutDatasetEmitsUnstartedDeterministicArtifacts()
    {
        BigQueryColabExporter.Export(root, Resolved, "{}");
        var initial = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        BigQueryColabExporter.Export(root, Resolved, "{}");
        foreach (var pair in initial)
            Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        using var order = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "colab/work_order.template.json")));
        Assert.Equal("unstarted", order.RootElement.GetProperty("status").GetString());
        Assert.False(order.RootElement.TryGetProperty("workOrderId", out _));
        Assert.False(File.Exists(Path.Combine(root, "colab/work_order.json")));
    }

    [Fact]
    public void NotebookIsAnExplicitInteractiveFlowWithNoClaimedExecution()
    {
        BigQueryColabExporter.Export(root, Resolved, "{}");
        using var notebook = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "colab/contoso_free_gcp.ipynb")));
        Assert.Equal("experimental", notebook.RootElement.GetProperty("metadata").GetProperty("contosoForge").GetProperty("artifactStatus").GetString());
        foreach (var cell in notebook.RootElement.GetProperty("cells").EnumerateArray())
        {
            if (cell.GetProperty("cell_type").GetString() != "code")
                continue;
            Assert.Equal(JsonValueKind.Null, cell.GetProperty("execution_count").ValueKind);
            Assert.Empty(cell.GetProperty("outputs").EnumerateArray());
        }
        var text = File.ReadAllText(Path.Combine(root, "colab/contoso_free_gcp.ipynb"));
        Assert.Contains("files.upload()", text);
        Assert.Contains("auth.authenticate_user()", text);
        Assert.Contains("files.download(", text);
        Assert.Contains("run_spark.py", text);
        Assert.Contains("bigquery_runtime.py", text);
    }

    [Fact]
    public void UnrelatedArchitectureDoesNotEmitGcpRuntime()
    {
        BigQueryColabExporter.Export(root, "{\"settings\":{\"warehouse\":\"sqlserver\",\"runtime\":\"docker\"}}", "{}");
        Assert.False(Directory.Exists(root));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
