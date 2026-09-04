using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Export;
using DatabaseGenerator.Forge.Pipeline;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DatabaseGenerator.Tests;

public sealed class ColabSparkModeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "forge-spark-mode-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ColabUsesNativeClassicWhileDockerKeepsValidatedPin()
    {
        var colab = ArchitecturePresets.Resolve(new StudioProjectSpec()).Settings;
        Assert.Equal("classic", colab.SparkApiMode);
        Assert.Equal("colab-native", colab.SparkVersionPolicy);
        Assert.Equal("4.0.4", colab.SparkVersion);
        var docker = ArchitecturePresets.Resolve(new StudioProjectSpec { Architecture = new() { PresetId = "local-spark" } }).Settings;
        Assert.Equal("classic", docker.SparkApiMode);
        Assert.Equal("pinned", docker.SparkVersionPolicy);
        Assert.Equal("3.5.9", docker.SparkVersion);
    }

    [Fact]
    public void RuntimeReplacementDoesNotCarryColabVersionPolicyIntoDocker()
    {
        var project = new StudioProjectSpec { Architecture = new() { Overrides = new() { Runtime = "docker" } } };
        var settings = ArchitecturePresets.Resolve(project).Settings;
        Assert.Equal("pinned", settings.SparkVersionPolicy);
        Assert.Equal("3.5.9", settings.SparkVersion);
        project.Architecture.Overrides.SparkVersion = "4.0.4";
        Assert.Equal("4.0.4", ArchitecturePresets.Resolve(project).Settings.SparkVersion);
    }

    [Theory]
    [InlineData("google-colab", "connect-local")]
    [InlineData("google-colab-connect-local", null)]
    public void ConnectLocalCompilesIntoSeparatelyIdentifiedWorkOrder(string runtime, string? mode)
    {
        var project = new StudioProjectSpec { Architecture = new() { Overrides = new() { Runtime = runtime, SparkApiMode = mode } } };
        var resolved = ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project));
        var pipeline = PipelineCompiler.CreateDefault(resolved);
        var compiled = PipelineCompiler.Compile(pipeline, resolved, root);
        var preparation = compiled.Plan.Activities.Single(a => a.Operation == "prepare-colab");
        Assert.Equal("connect-local", preparation.SparkApiMode);
        Assert.Equal("4.0.4", preparation.SparkVersion);
        Assert.Equal("manual-checkpoint", compiled.Plan.ArtifactStatus);
        BigQueryColabExporter.Export(root, resolved, pipeline);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "colab/spark_config.json")));
        Assert.Equal("connect-local", config.RootElement.GetProperty("sparkApiMode").GetString());
        Assert.True(File.Exists(Path.Combine(root, "colab/spark_session.py")));
    }

    [Fact]
    public void ActivitySparkOverridesReachExportedPackageConfiguration()
    {
        var resolved = ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(new StudioProjectSpec()));
        var pipeline = JsonNode.Parse(PipelineCompiler.CreateDefault(resolved))!;
        pipeline["activities"]!.AsArray().Single(a => a!["id"]!.GetValue<string>() == "prepare_colab")!["sparkApiMode"] = "connect-local";
        var json = pipeline.ToJsonString();
        var compiled = PipelineCompiler.Compile(json, resolved, root);
        Assert.Equal("connect-local", compiled.Plan.Activities.Single(a => a.Id == "prepare_colab").SparkApiMode);
        BigQueryColabExporter.Export(root, resolved, json);
        Assert.Contains("connect-local", File.ReadAllText(Path.Combine(root, "colab/spark_config.json")));
    }

    [Fact]
    public void RemoteRequiresSharedStorageAndRemainsUnsupportedForLocalPackageTransforms()
    {
        var project = new StudioProjectSpec { Architecture = new() { Overrides = new()
        {
            SparkApiMode = "connect-remote", SparkRemote = "sc://spark.example:15002"
        } } };
        Assert.Throws<ArgumentException>(() => ArchitecturePresets.Resolve(project));
        project.Architecture.Overrides.Storage = "azure-adls";
        var resolved = ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project));
        var compilation = PipelineCompiler.Compile(PipelineCompiler.CreateDefault(resolved), resolved, root);
        Assert.Equal("unsupported", compilation.Plan.ArtifactStatus);
        Assert.DoesNotContain(compilation.Plan.Activities, a => a.Operation == "prepare-colab");
    }

    [Theory]
    [InlineData("connect-local", "3.5.9", null)]
    [InlineData("pretend-connect", "4.0.4", null)]
    [InlineData("classic", "4.0.4", "sc://localhost:15002")]
    public void RejectsInvalidModeVersionOrEndpoint(string mode, string version, string? remote)
    {
        var project = new StudioProjectSpec { Architecture = new() { Overrides = new()
        {
            SparkApiMode = mode, SparkVersion = version, SparkRemote = remote
        } } };
        Assert.Throws<ArgumentException>(() => ArchitecturePresets.Resolve(project));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
