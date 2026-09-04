using DatabaseGenerator.Forge;
using System.Text.Json.Nodes;

namespace DatabaseGenerator.Tests;

public class ForgeStudioIntegrationTests
{
    [Fact]
    public async Task ResolvedPipelineConflictFailsValidationBeforeReplacingExistingOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-studio-preflight-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = Path.Combine(root, "input");
            var output = Path.Combine(root, "output");
            Assert.Equal(0, await ForgeCommand.RunAsync(new[] { "project", "init", "--output", input }));
            var project = Path.Combine(input, "project.json");
            Assert.Equal(0, await ForgeCommand.RunAsync(new[] { "generate", "--project", project, "--output", output }));
            var pipelineFile = Path.Combine(input, "pipeline.json");
            var pipeline = JsonNode.Parse(File.ReadAllText(pipelineFile))!;
            pipeline["activities"]![0]!["engine"] = "duckdb";
            File.WriteAllText(pipelineFile, pipeline.ToJsonString());
            var sourceFile = Path.Combine(output, "data/source/orders.csv");
            File.WriteAllText(sourceFile, "keep existing bytes on validation failure");
            Assert.Equal(2, await ForgeCommand.RunAsync(new[] { "validate", "--project", project }));
            Assert.Equal(2, await ForgeCommand.RunAsync(new[] { "generate", "--project", project, "--output", output }));
            Assert.Equal("keep existing bytes on validation failure", File.ReadAllText(sourceFile));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InitGenerateAndRecompileKeepsSourceAndRemovesStaleInfrastructure()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-studio-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = Path.Combine(root, "input");
            var output = Path.Combine(root, "output");
            Assert.Equal(0, await ForgeCommand.RunAsync(new[] { "project", "init", "--output", input }));
            var project = Path.Combine(input, "project.json");
            Assert.Equal(0, await ForgeCommand.RunAsync(new[] { "generate", "--project", project, "--output", output }));
            var source = File.ReadAllBytes(Path.Combine(output, "data/source/orders.csv"));
            Assert.True(File.Exists(Path.Combine(output, "infra/gcp/main.tf")));
            Assert.True(File.Exists(Path.Combine(output, "pipeline/pipeline.json"))); // Existing V1 artifact.
            Assert.True(File.Exists(Path.Combine(output, "pipeline.json")));
            Assert.True(File.Exists(Path.Combine(output, "airflow/dags/contoso_forge_customer_satisfaction.py")));
            var edited = JsonNode.Parse(File.ReadAllText(project))!;
            edited["architecture"]!["overrides"] = new JsonObject { ["iac"] = "none" };
            File.WriteAllText(project, edited.ToJsonString());
            Assert.Equal(0, await ForgeCommand.RunAsync(new[] { "pipeline", "compile", "--project", project, "--output", output }));
            Assert.False(File.Exists(Path.Combine(output, "infra/gcp/main.tf")));
            Assert.Equal(source, File.ReadAllBytes(Path.Combine(output, "data/source/orders.csv")));
            Assert.True(File.Exists(Path.Combine(output, "airflow/dags/contoso_forge_customer_satisfaction.py")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InvalidPipelineDoesNotReplaceGeneratedSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-studio-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = Path.Combine(root, "input");
            var output = Path.Combine(root, "output");
            Assert.Equal(0, await ForgeCommand.RunAsync(new[] { "project", "init", "--output", input }));
            File.WriteAllText(Path.Combine(input, "pipeline.json"), "{\"activities\":[{\"id\":\"bad\",\"kind\":\"invented\"}]}");
            Assert.Equal(2, await ForgeCommand.RunAsync(new[] { "generate", "--project", Path.Combine(input, "project.json"), "--output", output }));
            Assert.False(Directory.Exists(output));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CompileRefusesUnownedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-studio-unowned-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = Path.Combine(root, "input");
            var output = Path.Combine(root, "output");
            Assert.Equal(0, await ForgeCommand.RunAsync(new[] { "project", "init", "--output", input }));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "keep.txt"), "user data");
            Assert.Equal(2, await ForgeCommand.RunAsync(new[] { "pipeline", "compile", "--project", Path.Combine(input, "project.json"), "--output", output }));
            Assert.Equal("user data", File.ReadAllText(Path.Combine(output, "keep.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
