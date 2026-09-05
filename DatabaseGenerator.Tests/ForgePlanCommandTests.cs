using DatabaseGenerator.Forge;
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Planning;
using DatabaseGenerator.Forge.Pipeline;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public sealed class ForgePlanCommandTests
{
    [Fact]
    public async Task OfflinePlanCommandWritesOnlyRequestedDeterministicContractMatchingCore()
    {
        var root = NewRoot();
        try
        {
            var project = new StudioProjectSpec
            {
                SourceProject = ForgeTestProject.CreateSmallSpec(),
                Architecture = new() { PresetId = "local-fast" }, Git = null
            };
            var source = WriteProject(root, project);
            var first = Path.Combine(root, "first.json");
            var second = Path.Combine(root, "second.json");
            Assert.Equal(0, await ForgeCommand.RunAsync(["plan", "--project", source, "--output", first]));
            Assert.Equal(0, await ForgeCommand.RunAsync(["plan", "--project", source, "--output", second]));
            Assert.Equal(PlanBuilder.ToJson(PlanBuilder.Build(project)), File.ReadAllText(first));
            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
            Assert.Equal(3, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            Assert.Empty(Directory.GetDirectories(root));
            using var plan = JsonDocument.Parse(File.ReadAllText(first));
            Assert.Equal("not-executed", plan.RootElement.GetProperty("currentExecutionStatus").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InvalidArchitectureAndSourceOverwriteFailWithoutChangingFiles()
    {
        var root = NewRoot();
        try
        {
            var project = new StudioProjectSpec { SourceProject = ForgeTestProject.CreateSmallSpec() };
            var source = WriteProject(root, project);
            var before = File.ReadAllBytes(source);
            Assert.Equal(2, await ForgeCommand.RunAsync(["plan", "--project", source, "--output", source]));
            Assert.Equal(before, File.ReadAllBytes(source));
            var sibling = Path.Combine(root, "pipeline.json");
            File.WriteAllText(sibling, PipelineCompiler.CreateDefault(ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project))));
            var pipelineBefore = File.ReadAllBytes(sibling);
            Assert.Equal(2, await ForgeCommand.RunAsync(["plan", "--project", source, "--output", sibling]));
            Assert.Equal(pipelineBefore, File.ReadAllBytes(sibling));
            project.Architecture.Overrides.TableFormat = "delta";
            WriteProject(root, project);
            var result = Path.Combine(root, "bad-plan.json");
            Assert.Equal(2, await ForgeCommand.RunAsync(["plan", "--project", source, "--output", result]));
            Assert.False(File.Exists(result));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task NewCompileAliasIncludesSamePlanAndLegacyCompileRemainsUnchanged()
    {
        var root = NewRoot();
        try
        {
            var project = new StudioProjectSpec { SourceProject = ForgeTestProject.CreateSmallSpec() };
            var source = WriteProject(root, project);
            var compiled = Path.Combine(root, "compiled");
            var legacy = Path.Combine(root, "legacy");
            Assert.Equal(0, await ForgeCommand.RunAsync(["compile", "--project", source, "--output", compiled]));
            Assert.Equal(0, await ForgeCommand.RunAsync(["pipeline", "compile", "--project", source, "--output", legacy]));
            Assert.Equal(PlanBuilder.ToJson(PlanBuilder.Build(project)), File.ReadAllText(Path.Combine(compiled, "plan/resolved_plan.json")));
            Assert.False(Directory.Exists(Path.Combine(legacy, "plan")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(legacy, "pipeline.json")), File.ReadAllBytes(Path.Combine(compiled, "pipeline.json")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(legacy, "airflow/dags/contoso_forge_pipeline.py")), File.ReadAllBytes(Path.Combine(compiled, "airflow/dags/contoso_forge_pipeline.py")));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(compiled, "run_manifest.json")));
            Assert.True(manifest.RootElement.GetProperty("files").TryGetProperty("plan/resolved_plan.json", out _));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ScenarioSelectionChangesProfileWithoutWritingSourceOrRequiringCredentials()
    {
        var root = NewRoot();
        try
        {
            var project = new StudioProjectSpec { SourceProject = ForgeTestProject.CreateSmallSpec() };
            var source = WriteProject(root, project);
            var before = File.ReadAllBytes(source);
            var output = Path.Combine(root, "ml.json");
            Assert.Equal(0, await ForgeCommand.RunAsync(["plan", "--project", source, "--preset", "free-gcp-connect", "--scenario", ScenarioCatalog.MlScenarioId, "--output", output]));
            using var plan = JsonDocument.Parse(File.ReadAllText(output));
            Assert.Equal(1200, plan.RootElement.GetProperty("generationProfile").GetProperty("orders").GetInt32());
            Assert.Equal(365, plan.RootElement.GetProperty("generationProfile").GetProperty("timeSpanDays").GetInt32());
            Assert.Equal("connect-local", plan.RootElement.GetProperty("resolvedSettings").GetProperty("sparkApiMode").GetString());
            Assert.Equal(before, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "forge-plan-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteProject(string root, StudioProjectSpec project)
    {
        var path = Path.Combine(root, "project.json");
        File.WriteAllText(path, JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec));
        return path;
    }
}
