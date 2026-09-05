using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Pipeline;
using DatabaseGenerator.Forge.Planning;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public sealed class FactoryV15Tests
{
    private static StudioProjectSpec Project(bool ml = false) => new()
    {
        SourceProject = ForgeTestProject.CreateSmallSpec(), Architecture = new() { PresetId = "local-fast" },
        Product = new(), Git = null, BusinessScenario = ml ? ScenarioCatalog.MlScenarioId : ScenarioCatalog.DefaultScenarioId
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptInUsesSharedGraphAndFirstClassBiWithHonestCurrentStatus(bool ml)
    {
        var project = Project(ml);
        var plan = PlanBuilder.Build(project);
        Assert.Equal(ProductIntent.Steps, plan.Product!.Steps);
        Assert.Equal("runnable", plan.OverallImplementationStatus);
        Assert.Equal("not-executed", plan.CurrentExecutionStatus);
        Assert.Contains(plan.Stages, s => s.CompilerOperation == "factory-dbt");
        Assert.Single(plan.Stages, s => s.Kind == "bi-validation");
        Assert.Equal(ml, plan.Product.Ml is not null);
        var graph = PipelineDocument.Read(PipelineCompiler.CreateDefault(ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project))));
        Assert.DoesNotContain(graph.Activities, a => a.Kind == "manual-checkpoint");
        Assert.Contains(graph.Activities, a => a.Id == "bi_validation" && a.DependsOn.Contains(ml ? "ml_experiment" : "truth_reconcile"));
    }

    [Fact]
    public void DefaultContractRetainsLegacyReferenceBoundary()
    {
        var project = Project();
        project.Product = null;
        Assert.Equal("reference-only", PlanBuilder.Build(project).OverallImplementationStatus);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec));
        Assert.False(json.RootElement.TryGetProperty("product", out _));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("engine")]
    [InlineData("sink")]
    [InlineData("binding")]
    [InlineData("disabled")]
    public void CustomBindingsNeverBecomeSilentSuccessfulFactoryStages(string mutation)
    {
        var project = Project();
        var resolved = ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project));
        var graph = PipelineDocument.Read(PipelineCompiler.CreateDefault(resolved));
        var node = graph.Activities.Single(a => a.Id == "transform_bronze_silver");
        switch (mutation)
        {
            case "source": node.Source = "gcs"; break;
            case "engine": node.Engine = "pandas"; break;
            case "sink": node.Sink = "motherduck"; break;
            case "binding": node.Parameters["custom"] = JsonSerializer.SerializeToElement(1); break;
            case "disabled": node.Enabled = false; break;
        }
        var compiled = PipelineCompiler.Inspect(PipelineDocument.Write(graph), resolved);
        Assert.Equal("unsupported", compiled.Activities.Single(a => a.Id == node.Id).Operation);
    }

    [Fact]
    public void MlDesignExcludesOutcomesAndUsesMeasuredTemporalReadiness()
    {
        var design = PlanBuilder.Build(Project(true)).Product!.Ml!;
        Assert.Equal("scikit-learn", design.Framework);
        Assert.Equal(14, design.EmbargoDays);
        Assert.Equal(14, design.MinimumLabelDelayDays);
        Assert.Equal("not-executed", design.TrainingStatus);
        Assert.Contains("dummy", design.CandidateAlgorithms);
        Assert.Contains("average_precision", design.Metrics);
        Assert.Contains("confusion_matrix", design.Metrics);
        Assert.Contains("average_review_rating", design.LeakageExclusions);
        Assert.All(design.Features, f => Assert.Contains(f, design.FeatureAvailability.Keys));
        Assert.Empty(design.Features.Intersect(design.LeakageExclusions));
    }

    [Fact]
    public void AirflowHostIsSeparateAndCiIsNotPersistentHost()
    {
        var project = Project();
        project.Architecture.PresetId = "local-airflow";
        var plan = PlanBuilder.Build(project);
        Assert.Equal("airflow", plan.Product!.Orchestrator);
        Assert.Equal("docker-local", plan.Product.AirflowHost);
        project.Architecture.Overrides.AirflowHost = "github-actions";
        Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project));
    }

    [Fact]
    public void InvalidBiAndCosmosSelectionsFailBeforeCompilation()
    {
        var project = Project();
        project.Product!.BiTarget = "evidence-and-dive";
        Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project));
        project.Product.BiTarget = "evidence";
        project.Product.DbtIntegration = "cosmos";
        Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project));
    }

    [Fact]
    public void CosmosCannotBorrowPlainDbtExecutionEvidence()
    {
        var project = Project();
        project.Architecture.PresetId = "local-airflow";
        project.Product!.DbtIntegration = "cosmos";
        var plan = PlanBuilder.Build(project);
        var dbt = Assert.Single(plan.Stages, s => s.CompilerOperation == "factory-dbt");
        Assert.Equal("generated", dbt.ImplementationStatus);
        Assert.Equal("generated", dbt.ValidationLevel);
        Assert.Empty(dbt.Evidence);
        Assert.Equal("not-executed", plan.CurrentExecutionStatus);
        Assert.NotEqual("runnable", plan.OverallImplementationStatus);
    }

    [Theory]
    [InlineData("free-gcp-lab")]
    [InlineData("free-gcp-connect")]
    public void ColabPreservesManualCheckpointAndAddsUniversalBi(string preset)
    {
        var project = Project(true);
        project.Architecture.PresetId = preset;
        project.Git = new();
        var plan = PlanBuilder.Build(project);
        Assert.NotEmpty(plan.ManualCheckpoints);
        Assert.Single(plan.Stages, s => s.Kind == "bi-validation");
        Assert.Equal("scikit-learn", plan.Product!.Ml!.Framework);
        Assert.DoesNotContain(plan.Stages, s => s.Kind == "ml-training" && s.ImplementationStatus == "executed");
    }

    [Fact]
    public async Task CompileEmitsSelfContainedFactoryWithoutChangingTheSourceGenerator()
    {
        var project = Project();
        var root = Path.Combine(Path.GetTempPath(), "forge-v15-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new ForgeProjectGenerator().GenerateAsync(project.SourceProject, root);
            var bytes = File.ReadAllBytes(Path.Combine(root, "data/source/orders.csv"));
            ForgeStudioCommand.Compile(project, root, includePlan: true);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, "data/source/orders.csv")));
            foreach (var path in new[] { "factory/run.py", "factory/bi_report.py", "factory/ml/spec.json", "factory/ml_lab.py", "factory/spark_ml.py", "factory/dive.tsx", "factory/dbt/models/intermediate/int_customer_experience.sql" })
                Assert.True(File.Exists(Path.Combine(root, path)), path);
            Assert.Contains("factory/run.py", File.ReadAllText(Path.Combine(root, "pipeline/forge_pipeline_runtime.py")));
            Assert.DoesNotContain("expectedKpis", File.ReadAllText(Path.Combine(root, "factory/ml_lab.py")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
