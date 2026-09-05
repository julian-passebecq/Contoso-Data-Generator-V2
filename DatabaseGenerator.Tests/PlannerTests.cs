using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Pipeline;
using DatabaseGenerator.Forge.Planning;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public sealed class PlannerTests
{
    private static StudioProjectSpec Project(string preset = "free-gcp-lab") => new()
    {
        SourceProject = ForgeTestProject.CreateSmallSpec(), Architecture = new() { PresetId = preset }
    };

    [Fact]
    public void RepeatedPlanPreservesInputAndContainsNoRunIdentityOrMachineState()
    {
        var project = Project();
        var before = JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec);
        var first = PlanBuilder.ToJson(PlanBuilder.Build(project));
        Assert.Equal(first, PlanBuilder.ToJson(PlanBuilder.Build(project)));
        Assert.Equal(before, JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec));
        Assert.DoesNotContain("psychic-sun", first);
        Assert.DoesNotContain("C:\\", first);
        Assert.DoesNotContain("D:\\", first);
        Assert.DoesNotContain("recordedAt", first);
        Assert.DoesNotContain("runId", first);
        Assert.Contains("not-executed", first);
    }

    [Fact]
    public void ExplicitConnectIsDistinctAndHonestAboutComposedReadiness()
    {
        var classic = PlanBuilder.Build(Project());
        var connect = PlanBuilder.Build(Project("free-gcp-connect"));
        Assert.Equal("classic", classic.ResolvedSettings.SparkApiMode);
        Assert.Equal("connect-local", connect.ResolvedSettings.SparkApiMode);
        Assert.Equal("4.0.4", connect.ResolvedSettings.SparkVersion);
        Assert.Equal("bigquery", connect.ResolvedSettings.Warehouse);
        Assert.Equal("generated", connect.OverallReadiness);
        Assert.Equal("not-executed", connect.CurrentExecutionStatus);
        Assert.Equal("reconciled", connect.Stages.Single(s => s.Kind == "transform").ValidationLevel);
        Assert.Equal("generated", connect.Stages.Single(s => s.CompilerOperation == "reconcile-colab").ValidationLevel);
        Assert.Contains(connect.Warnings, w => w.Contains("complete composed Connect"));
        Assert.Contains(connect.ManualCheckpoints, c => c.Reason.Contains("Colab"));
        Assert.All(connect.RequiredCredentials, c => Assert.False(c.RequiredAtPlanTime));
        Assert.Contains(connect.Stages, s => s.Kind == "orchestration" && s.ValidationLevel == "parses");
        Assert.Contains(connect.Stages, s => s.Kind == "infrastructure" && s.ValidationLevel == "parses");
    }

    [Fact]
    public void ScenarioApplicationIsExplicitClonedAndIndependentOfArchitecture()
    {
        var project = Project("local-fast");
        project.Architecture.Overrides.FileFormat = "csv";
        project.SourceProject.Generation.Seed = 77;
        var originalOrders = project.SourceProject.Generation.Orders;
        var ml = ScenarioCatalog.Apply(project, ScenarioCatalog.MlScenarioId);
        Assert.NotSame(project, ml);
        Assert.Equal(originalOrders, project.SourceProject.Generation.Orders);
        Assert.Equal(1200, ml.SourceProject.Generation.Orders);
        Assert.Equal(365, ml.SourceProject.Generation.TimeSpanDays);
        Assert.Equal("local-fast", ml.Architecture.PresetId);
        Assert.Equal("csv", ml.Architecture.Overrides.FileFormat);
        Assert.Equal(project.SourceProject.Name, ml.SourceProject.Name);
        Assert.Equal(77, ml.SourceProject.Generation.Seed);
        ml.SourceProject.Generation.Orders = 1500;
        var same = ScenarioCatalog.Apply(ml, ScenarioCatalog.MlScenarioId);
        Assert.Equal(1500, same.SourceProject.Generation.Orders);
        Assert.Equal(1500, PlanBuilder.Build(same).GenerationProfile.Orders);
        var retail = ScenarioCatalog.Apply(same, ScenarioCatalog.DefaultScenarioId);
        Assert.Null(retail.SourceProject.Generation.TimeSpanDays);
        Assert.Equal(120, retail.SourceProject.Generation.Orders);
    }

    [Fact]
    public void MlIncludesStandaloneFeaturesButNeverClaimsTrainedModel()
    {
        var ml = PlanBuilder.Build(ScenarioCatalog.Apply(Project("free-gcp-connect"), ScenarioCatalog.MlScenarioId));
        Assert.Equal(365, ml.GenerationProfile.TimeSpanDays);
        Assert.Equal(14, ml.GenerationProfile.LabelEmbargoDays);
        var features = ml.Stages.Single(s => s.Kind == "ml-features");
        Assert.Equal("executes", features.ValidationLevel);
        var train = ml.Stages.Single(s => s.Kind == "ml-training");
        Assert.Equal("generated", train.ImplementationStatus);
        Assert.Equal("generated", train.ValidationLevel);
        Assert.Empty(train.Evidence);
        Assert.True(train.Manual);
        Assert.Equal("standalone-after-pipeline", train.CompilerBoundary);
        Assert.Contains("--allow-training-cost", train.Reason);
        Assert.DoesNotContain(PlanBuilder.Build(Project()).Stages, s => s.Kind == "ml-training");
    }

    [Theory]
    [InlineData("local-fast")]
    [InlineData("fabric-lakehouse")]
    [InlineData("databricks-free")]
    [InlineData("open-lakehouse-iceberg")]
    public void ReferenceArchitectureDoesNotClaimExecutableTransform(string preset)
    {
        var project = Project(preset);
        if (preset == "local-fast") project.Git = null;
        var plan = PlanBuilder.Build(project);
        Assert.Equal("declared", plan.OverallReadiness);
        Assert.Equal("reference-only", plan.OverallImplementationStatus);
        Assert.Contains(plan.Stages, s => s.ImplementationStatus == "reference-only" && s.CompilerOperation == "unsupported");
        Assert.DoesNotContain(plan.Stages, s => s.Kind == "warehouse-load");
    }

    [Theory]
    [InlineData("polars")]
    [InlineData("pandas")]
    public void FutureEngineOverridesRemainReferenceOnly(string engine)
    {
        var project = Project("local-fast");
        project.Architecture.Overrides.Engine = engine;
        var plan = PlanBuilder.Build(project);
        Assert.Equal(engine, plan.Stages.Single(s => s.Id == "transform").Engine);
        Assert.Equal("reference-only", plan.Stages.Single(s => s.Id == "transform").ImplementationStatus);
        Assert.DoesNotContain(plan.Stages, s => s.Engine == engine && s.ImplementationStatus == "executed");
    }

    [Fact]
    public void NativeBigQueryDeltaIsRejectedAtArchitectureAndActivityLevels()
    {
        var project = Project();
        project.Architecture.Overrides.TableFormat = "delta";
        Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project));
        project.Architecture.Overrides.TableFormat = null;
        var pipeline = DefaultPipeline(project);
        pipeline.Activities.Single(a => a.Id == "prepare_colab").TableFormat = "delta";
        Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project, PipelineDocument.Write(pipeline)));
    }

    [Fact]
    public void CustomPipelineIdsEdgesOverridesAndUnsupportedBindingsAreRetained()
    {
        var project = Project();
        var pipeline = DefaultPipeline(project);
        var handoff = pipeline.Activities.Single(a => a.Id == "prepare_colab");
        handoff.Name = "Custom handoff";
        handoff.SparkApiMode = "connect-local";
        handoff.Runtime = "google-colab-connect-local";
        var custom = new PipelineActivity { Id = "my_gold", Name = "My Gold", Kind = "dbt", Engine = "duckdb", Runtime = "local-process", DependsOn = new() { "reconcile" } };
        pipeline.Activities.Add(custom);
        var plan = PlanBuilder.Build(project, PipelineDocument.Write(pipeline));
        Assert.Equal("connect-local", plan.Stages.Single(s => s.Id == handoff.Id).SparkApiMode);
        Assert.Equal("Custom handoff", plan.Stages.Single(s => s.Id == handoff.Id).Name);
        Assert.Equal("generated", plan.Stages.Single(s => s.Id == "reconcile").ValidationLevel);
        Assert.DoesNotContain(plan.Stages.Single(s => s.Id == "reconcile").Evidence, e => e.Id == "classic-bigquery-result-adoption");
        Assert.Equal("duckdb", plan.Stages.Single(s => s.Id == "my_gold").Engine);
        Assert.Equal("unsupported", plan.Stages.Single(s => s.Id == "my_gold").CompilerOperation);
        Assert.Contains(plan.Edges, e => e.From == "reconcile" && e.To == "my_gold");
        Assert.Contains(plan.Warnings, w => w.Contains("my_gold"));
    }

    [Fact]
    public void UnsupportedDatasetPathsNeverAcquireExternalSparkOrWarehouseEvidence()
    {
        var project = Project();
        var pipeline = DefaultPipeline(project);
        pipeline.Datasets.Single(d => d.Id == "source_csv").Path = "my-custom-data";
        var plan = PlanBuilder.Build(project, PipelineDocument.Write(pipeline));
        Assert.Equal("unsupported", plan.Stages.Single(s => s.Id == "prepare_colab").CompilerOperation);
        Assert.DoesNotContain(plan.Stages, s => s.Kind is "warehouse-load" or "analytics-transform");
    }

    [Fact]
    public void SparkVersionOutsideActualBootstrapCompatibilityIsRejected()
    {
        var project = Project("free-gcp-connect");
        project.Architecture.Overrides.SparkVersion = "4.1.0";
        var error = Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project));
        Assert.Contains("bootstrap does not support", error.Message);
    }

    [Fact]
    public void EquivalentPipelineOrderProducesSamePlanAndSharesCompilerInspection()
    {
        var project = Project();
        var pipeline = DefaultPipeline(project);
        var first = PlanBuilder.ToJson(PlanBuilder.Build(project, PipelineDocument.Write(pipeline)));
        pipeline.Activities.Reverse();
        pipeline.Datasets.Reverse();
        var second = PlanBuilder.Build(project, PipelineDocument.Write(pipeline));
        Assert.Equal(first, PlanBuilder.ToJson(second));
        var compiler = PipelineCompiler.Inspect(PipelineDocument.Write(pipeline), ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project)));
        Assert.All(compiler.Activities, operation => Assert.Equal(operation.Operation, second.Stages.Single(s => s.Id == operation.Id).CompilerOperation));
        Assert.All(second.Edges, e => { Assert.Contains(second.Stages, s => s.Id == e.From); Assert.Contains(second.Stages, s => s.Id == e.To); });
    }

    [Fact]
    public void SupplementalIdsCannotCollideWithAuthoredActivities()
    {
        var project = Project();
        var pipeline = DefaultPipeline(project);
        pipeline.Activities.Add(new() { Id = "plan_generate", Kind = "sql", Runtime = "local-process", Engine = "duckdb" });
        var plan = PlanBuilder.Build(project, PipelineDocument.Write(pipeline));
        Assert.Equal(plan.Stages.Count, plan.Stages.Select(s => s.Id).Distinct().Count());
        Assert.Contains(plan.Stages, s => s.Id == "plan_generate_");
    }

    [Fact]
    public void UnknownScenarioAndMalformedGraphFailBeforePlanning()
    {
        var project = Project();
        project.BusinessScenario = "invented";
        Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project));
        project.BusinessScenario = null;
        Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project, "{\"activities\":null}"));
    }

    [Fact]
    public void ActivityOnlyWarehouseOverrideDoesNotPromiseUnemittedBigQueryConfiguration()
    {
        var project = Project();
        project.Architecture.Overrides.Warehouse = "none";
        var pipeline = DefaultPipeline(project);
        foreach (var activity in pipeline.Activities) activity.Sink = "bigquery";
        var plan = PlanBuilder.Build(project, PipelineDocument.Write(pipeline));
        Assert.Equal("unsupported", plan.Stages.Single(s => s.Id == "prepare_colab").ImplementationStatus);
        Assert.DoesNotContain(plan.Stages, s => s.Kind == "warehouse-load");
        Assert.Contains(plan.Warnings, w => w.Contains("project-level BigQuery"));
    }

    [Fact]
    public void MultipleWorkOrdersGetSameExplicitExporterBoundary()
    {
        var project = Project();
        var pipeline = DefaultPipeline(project);
        pipeline.Activities.Add(new() { Id = "another_handoff", Kind = "handoff", Implementation = "colab-work-order", DependsOn = new() { "verify_source" } });
        var error = Assert.Throws<ArgumentException>(() => PlanBuilder.Build(project, PipelineDocument.Write(pipeline)));
        Assert.Contains("exactly one work-order", error.Message);
    }

    [Theory]
    [InlineData("fabric-lakehouse", "provider:fabric")]
    [InlineData("databricks-free", "provider:databricks")]
    [InlineData("sqlserver-bi", "provider:sqlserver")]
    public void ReferenceProviderCredentialsAreDescribedWithoutAuthenticating(string preset, string scope)
    {
        var plan = PlanBuilder.Build(Project(preset));
        var credential = Assert.Single(plan.RequiredCredentials, c => c.Scope == scope);
        Assert.False(credential.RequiredAtPlanTime);
        Assert.True(credential.RequiredAtExecutionTime);
        Assert.Contains("reference adapter", credential.Reason);
    }

    private static PipelineDefinition DefaultPipeline(StudioProjectSpec project) => PipelineDocument.Read(
        PipelineCompiler.CreateDefault(ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(project))));
}
