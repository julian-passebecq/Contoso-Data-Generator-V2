using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Pipeline;
using DatabaseGenerator.Forge.Planning;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public sealed class FactoryV17Tests
{
    private static StudioProjectSpec Project(string goal = "data-only", string stop = "silver", string engine = "duckdb") => new()
    {
        SourceProject = ForgeTestProject.CreateSmallSpec(), Git = null,
        Architecture = new() { PresetId = "local-fast", Overrides = new() { Engine = engine } },
        BusinessScenario = ScenarioCatalog.MlScenarioId,
        Product = new() { Version = "1.7", Goal = goal, StopAfter = stop, SelectedKpis = goal == "kpi-semantic" ? new() { "return_rate" } : new(),
            Analysis = new() { Kind = goal == "automl" ? "mljar" : goal == "specific-ml" ? "sklearn" : "none", Runtime = goal == "automl" ? "kaggle" : "local", Algorithm = goal == "specific-ml" ? "logistic_regression" : null }, PublishTargets = new() }
    };

    [Theory]
    [InlineData("generate")]
    [InlineData("bronze")]
    [InlineData("silver")]
    [InlineData("gold")]
    [InlineData("semantic")]
    [InlineData("analysis")]
    [InlineData("report")]
    public void StopsBoundEveryCompiledOperationAndNeverInjectBi(string stop)
    {
        var project = Project("kpi-semantic", stop);
        var plan = PlanBuilder.Build(project);
        Assert.Equal("not-executed", plan.CurrentExecutionStatus);
        Assert.Equal("runnable", plan.OverallImplementationStatus);
        Assert.All(plan.Stages.Where(s => s.LogicalStage != null), s => Assert.True(Array.IndexOf(ProductIntent.StopStages, s.LogicalStage) <= Array.IndexOf(ProductIntent.StopStages, stop)));
        if (stop != "report") Assert.DoesNotContain(plan.Stages, s => s.CompilerOperation == "factory-bi");
        Assert.Equal(stop, plan.Product!.StopAfter);
        Assert.Equal(ProductIntent.JourneySteps, plan.Product.Steps);
    }

    [Theory]
    [InlineData("duckdb")]
    [InlineData("polars")]
    [InlineData("pandas")]
    [InlineData("spark")]
    public void EveryLocalEngineHasIndependentBronzeAndSilver(string engine)
    {
        var plan = PlanBuilder.Build(Project(engine: engine));
        Assert.Equal("runnable", plan.OverallImplementationStatus);
        Assert.Single(plan.Stages, s => s.CompilerOperation == "factory-bronze" && s.Engine == engine);
        Assert.Single(plan.Stages, s => s.CompilerOperation == "factory-silver" && s.Engine == engine);
    }

    [Theory]
    [InlineData("data-only", "report")]
    [InlineData("data-only", "publish")]
    [InlineData("unknown", "silver")]
    [InlineData("data-only", "unknown")]
    public void InvalidGoalBoundariesFail(string goal, string stop) => Assert.Throws<ArgumentException>(() => PlanBuilder.Build(Project(goal, stop)));

    [Theory]
    [InlineData("1.5")]
    [InlineData("1.6")]
    public void LegacyProjectsRejectJourneyFieldsAndOmitNewProperties(string version)
    {
        var project = Project(); project.Product!.Version = version;
        Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project));
        project.Product = new() { Version = version };
        var json = JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec);
        Assert.DoesNotContain("stopAfter", json);
        Assert.DoesNotContain("publishTargets", json);
        Assert.DoesNotContain("logicalStage", PlanBuilder.ToJson(PlanBuilder.Build(project)));
    }

    [Theory]
    [InlineData("order_count")]
    [InlineData("gross_sales_amount")]
    [InlineData("return_rate")]
    [InlineData("on_time_delivery_rate")]
    [InlineData("average_review_rating")]
    public async Task ScopedSemanticsUseCanonicalGoldAndPreserveTruth(string kpi)
    {
        var project = Project("kpi-semantic", "semantic"); project.Product!.SelectedKpis = new() { kpi };
        var root = Path.Combine(Path.GetTempPath(), "forge-v17-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new ForgeProjectGenerator().GenerateAsync(project.SourceProject, root);
            var truth = File.ReadAllBytes(Path.Combine(root, "truth_manifest.json"));
            ForgeStudioCommand.Compile(project, root, includePlan: true);
            Assert.Equal(truth, File.ReadAllBytes(Path.Combine(root, "truth_manifest.json")));
            using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "models/kpi_catalog.json")));
            Assert.Equal(kpi, Assert.Single(catalog.RootElement.GetProperty("kpis").EnumerateArray()).GetProperty("id").GetString());
            using var semantic = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "models/semantic_model.json")));
            var measure = Assert.Single(semantic.RootElement.GetProperty("measures").EnumerateArray());
            Assert.Equal(kpi, measure.GetProperty("column").GetString());
            Assert.Equal("kpi_customer_satisfaction", measure.GetProperty("table").GetString());
            Assert.Equal($"SELECT {kpi} FROM gold.kpi_customer_satisfaction;\n", File.ReadAllText(Path.Combine(root, "models/query_examples.sql")));
            var lineage = File.ReadAllText(Path.Combine(root, "models/lineage.json"));
            ForgeStudioCommand.Compile(project, root, includePlan: true);
            Assert.Equal(lineage, File.ReadAllText(Path.Combine(root, "models/lineage.json")));
            Assert.True(File.Exists(Path.Combine(root, "factory/layers.py")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CustomGraphCannotRunPastStopAfter()
    {
        var project = Project(stop: "bronze");
        var resolved = ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project));
        var graph = PipelineDocument.Read(PipelineCompiler.CreateDefault(resolved));
        graph.Activities.Add(new() { Id = "bad", Kind = "dbt", Implementation = "factory-dbt", DependsOn = new() { "bronze" } });
        Assert.Equal("unsupported", PipelineCompiler.Inspect(PipelineDocument.Write(graph), resolved).Activities.Last().Operation);
    }

    [Theory]
    [InlineData("budget")]
    [InlineData("target")]
    [InlineData("kind")]
    [InlineData("runtime")]
    [InlineData("auth")]
    public void AutoMlRejectsUnboundedOrIncompatibleConfiguration(string change)
    {
        var project = Project("automl", "analysis"); var analysis = project.Product!.Analysis!;
        switch (change) { case "budget": analysis.TotalTimeLimitSeconds = 0; break; case "target": analysis.Target = "review_rating"; break;
            case "kind": analysis.Kind = "spark-ml"; break; case "runtime": analysis.Runtime = "local"; break; case "auth": analysis.RequireExecution = true; break; }
        Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("x;DROP TABLE orders")]
    [InlineData("$(touch pwn)")]
    public void RecipeRejectsIdentifiersThatCouldCarryPathsOrCode(string id)
    {
        var recipe = new WranglingRecipe { Steps = new() { new() { Id = id, Op = "source", Entity = "orders" } } };
        Assert.Throws<ArgumentException>(recipe.Validate);
    }
    [Fact]
    public void RecipeRejectsCycles()
    {
        var recipe = new WranglingRecipe { Steps = new() { new() { Id = "a", Op = "sink", Input = "b" }, new() { Id = "b", Op = "sink", Input = "a" } } };
        Assert.Throws<ArgumentException>(recipe.Validate);
    }
}
