using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Pipeline;
using DatabaseGenerator.Forge.Planning;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public sealed class FactoryV16Tests
{
    private static StudioProjectSpec Project(string engine) => new()
    {
        SourceProject = ForgeTestProject.CreateSmallSpec(),
        Architecture = new() { PresetId = engine == "duckdb" ? "local-fast" : "local-" + engine },
        Product = new() { Version = "1.6" }, Git = null, BusinessScenario = ScenarioCatalog.MlScenarioId
    };

    [Theory]
    [InlineData("duckdb")]
    [InlineData("polars")]
    [InlineData("pandas")]
    public void PresetsShareCompilerGraphAndKeepCurrentExecutionUnverified(string engine)
    {
        var project = Project(engine);
        var plan = PlanBuilder.Build(project);
        Assert.Equal("runnable", plan.OverallImplementationStatus);
        Assert.Equal("not-executed", plan.CurrentExecutionStatus);
        Assert.Equal("1.6", plan.Product!.Version);
        Assert.Equal(engine, plan.Stages.Single(s => s.CompilerOperation == "factory-silver").Engine);
        Assert.Equal("dbt-duckdb", plan.Stages.Single(s => s.CompilerOperation == "factory-dbt").Engine);
        var resolved = ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project));
        var graph = PipelineCompiler.CreateDefault(resolved);
        Assert.Equal(7, PipelineCompiler.Inspect(graph, resolved).Activities.Count);
        Assert.All(PipelineCompiler.Inspect(graph, resolved).Activities, a => Assert.StartsWith("factory-", a.Operation));
        Assert.DoesNotContain(plan.Stages.SelectMany(s => s.Evidence), e => e.Scope.Contains("Spark parity successful"));
    }

    [Theory]
    [InlineData("polars")]
    [InlineData("pandas")]
    public async Task CompilePackagesRealAdaptersAndComparatorWithoutSourceChanges(string engine)
    {
        var project = Project(engine);
        var root = Path.Combine(Path.GetTempPath(), "forge-v16-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new ForgeProjectGenerator().GenerateAsync(project.SourceProject, root);
            var truth = File.ReadAllBytes(Path.Combine(root, "truth_manifest.json"));
            ForgeStudioCommand.Compile(project, root, includePlan: true);
            Assert.Equal(truth, File.ReadAllBytes(Path.Combine(root, "truth_manifest.json")));
            foreach (var name in new[] { "pandas_silver.py", "polars_silver.py", "duckdb_silver.py", "silver_contract.py", "parity.py", "run.py" })
                Assert.True(File.Exists(Path.Combine(root, "factory", name)), name);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "run_manifest.json")));
            Assert.True(manifest.RootElement.GetProperty("files").TryGetProperty("factory/parity.py", out _));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("polars", "runtime")]
    [InlineData("pandas", "runtime")]
    [InlineData("polars", "storage")]
    [InlineData("pandas", "warehouse")]
    [InlineData("polars", "format")]
    [InlineData("pandas", "table")]
    public void UnsupportedCombinationsNeverBecomeRunnable(string engine, string change)
    {
        var project = Project(engine);
        var o = project.Architecture.Overrides;
        switch (change)
        {
            case "runtime": o.Runtime = "docker"; break;
            case "storage": o.Storage = "s3"; break;
            case "warehouse": o.Warehouse = "motherduck"; break;
            case "format": o.FileFormat = "csv"; break;
            case "table": o.TableFormat = "iceberg"; break;
        }
        Assert.NotEqual("runnable", PlanBuilder.Build(project).OverallImplementationStatus);
    }

    [Theory]
    [InlineData("polars")]
    [InlineData("pandas")]
    public void PerActivityEngineOverrideCannotSilentlyRunDifferentCompiledEngine(string engine)
    {
        var project = Project(engine);
        var resolved = ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project));
        var graph = PipelineDocument.Read(PipelineCompiler.CreateDefault(resolved));
        graph.Activities.Single(a => a.Id == "transform_bronze_silver").Engine = "duckdb";
        Assert.Equal("unsupported", PipelineCompiler.Inspect(PipelineDocument.Write(graph), resolved).Activities.Single(a => a.Id == "transform_bronze_silver").Operation);
    }
}
