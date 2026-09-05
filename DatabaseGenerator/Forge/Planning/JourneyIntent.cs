#nullable enable
using DatabaseGenerator.Forge.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DatabaseGenerator.Forge.Planning;

public sealed partial class ProductIntent
{
    public string? Goal { get; set; }
    public string? StopAfter { get; set; }
    public List<string>? SelectedKpis { get; set; }
    public AnalysisIntent? Analysis { get; set; }
    public List<PublishTarget>? PublishTargets { get; set; }
    public WranglingRecipe? Recipe { get; set; }

    public static readonly string[] Goals = { "data-only", "kpi-semantic", "specific-ml", "automl" };
    public static readonly string[] StopStages = { "generate", "bronze", "silver", "gold", "semantic", "analysis", "report", "publish" };
    public static readonly string[] JourneySteps = { "Goal", "Data", "Stop stage", "Transformation", "KPI / ML configuration", "Destination / publish", "Orchestration", "Results" };
    public static readonly string[] Kpis = { "order_count", "gross_sales_amount", "return_rate", "on_time_delivery_rate", "average_review_rating" };

    private void ValidateJourney(StudioProjectSpec project, ArchitectureSettings settings)
    {
        if (Version != "1.7")
        {
            if (Goal is not null || StopAfter is not null || SelectedKpis is not null || Analysis is not null || PublishTargets is not null || Recipe is not null)
                throw new ArgumentException("Goal, stopAfter, analysis, publishTargets and recipe require product.version=1.7.");
            return;
        }
        if (!Goals.Contains(Goal) || !StopStages.Contains(StopAfter)) throw new ArgumentException("V1.7 requires an explicit valid goal and stopAfter.");
        if (SelectedKpis is null || SelectedKpis.Any(k => !Kpis.Contains(k)) || SelectedKpis.Distinct().Count() != SelectedKpis.Count)
            throw new ArgumentException("selectedKpis must be a unique list of governed KPI IDs (empty for data/ML).");
        if (Goal == "kpi-semantic" && SelectedKpis.Count == 0) throw new ArgumentException("KPI goal requires selectedKpis.");
        if (Goal == "data-only" && (Array.IndexOf(StopStages, StopAfter) > 3 || SelectedKpis.Count > 0))
            throw new ArgumentException("data-only can stop at generate, bronze, silver or gold and publishes no KPIs.");
        if (Analysis is null || PublishTargets is null || PublishTargets.Any(p => p is null)) throw new ArgumentException("V1.7 requires typed analysis and publishTargets.");
        Analysis.Validate();
        if ((Goal is "data-only" or "kpi-semantic") != (Analysis.Kind == "none")) throw new ArgumentException("Analysis kind must match the selected goal.");
        if (Goal == "specific-ml" && Analysis.Kind is not ("sklearn" or "spark-ml")) throw new ArgumentException("specific-ml requires sklearn or spark-ml.");
        if (Goal == "automl" && Analysis.Kind != "mljar") throw new ArgumentException("automl requires bounded MLJAR on Kaggle.");
        if (Goal is "specific-ml" or "automl" && project.BusinessScenario != ScenarioCatalog.MlScenarioId)
            throw new ArgumentException("The generated ML journey requires the governed customer satisfaction ML scenario.");
        if (settings.Warehouse == "none" && Array.IndexOf(StopStages, StopAfter) > 2) throw new ArgumentException("Gold and later stages require a warehouse.");
        if (PublishTargets.Select(p => p.Kind).Distinct().Count() != PublishTargets.Count) throw new ArgumentException("Duplicate publish target.");
        foreach (var target in PublishTargets)
        {
            target.Validate();
            if (target.Kind == "huggingface" && Goal is not ("specific-ml" or "automl")) throw new ArgumentException("Hugging Face consumes measured ML results.");
            if (target.Kind == "motherduck" && (settings.Warehouse != "motherduck" || Goal != "kpi-semantic")) throw new ArgumentException("MotherDuck target requires the KPI journey and MotherDuck warehouse.");
        }
        if (settings.Warehouse == "motherduck" && !PublishTargets.Any(p => p.Kind == "motherduck")) throw new ArgumentException("MotherDuck requires an explicit fresh database target.");
        if (StopAfter == "publish" && !PublishTargets.Any(p => p.Kind == "huggingface" || p.CreateDive)) throw new ArgumentException("publish requires a downstream Hugging Face or Dive target.");
        Recipe?.Validate();
        if (DbtIntegration == "cosmos" && (StopAfter is not ("report" or "publish") || settings.Warehouse != "duckdb"))
            throw new ArgumentException("V1.7 Cosmos requires DuckDB and stopAfter report/publish; shorter journeys use the neutral Airflow or local runner.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AnalysisIntent
{
    public string Kind { get; set; } = "none";
    public string Runtime { get; set; } = "local";
    public string ProblemType { get; set; } = "binary_classification";
    public string Target { get; set; } = "is_dissatisfied_14d";
    public string? Algorithm { get; set; }
    public string Mode { get; set; } = "Explain";
    public int TotalTimeLimitSeconds { get; set; } = 300;
    public int TimeoutSeconds { get; set; } = 1800;
    public bool Execute { get; set; }
    public bool RequireExecution { get; set; }
    public string? DatasetId { get; set; }
    public string? KernelId { get; set; }
    public bool VersionDataset { get; set; }

    internal void Validate()
    {
        if (Kind is not ("none" or "sklearn" or "spark-ml" or "mljar") || Runtime is not ("local" or "colab" or "kaggle" or "databricks-export")) throw new ArgumentException("Unsupported analysis kind/runtime.");
        if (ProblemType != "binary_classification" || Target != "is_dissatisfied_14d") throw new ArgumentException("V1.7 generated ML supports the governed binary dissatisfaction target only; other families have no generated label contract.");
        if (Mode is not ("Explain" or "Perform" or "Compete") || TotalTimeLimitSeconds is < 30 or > 3600 || TimeoutSeconds < TotalTimeLimitSeconds + 60 || TimeoutSeconds > 7200) throw new ArgumentException("AutoML budget must be 30..3600 seconds; external timeout must include 60 seconds overhead and be <=7200.");
        var algorithms = Kind == "spark-ml" ? new[] { "logistic_regression", "random_forest", "gradient_boosting" } : new[] { "dummy", "logistic_regression", "random_forest", "histogram_gradient_boosting" };
        if (Kind is "sklearn" or "spark-ml" && !algorithms.Contains(Algorithm)) throw new ArgumentException("specific-ml requires an explicit supported algorithm.");
        if (Kind == "mljar" && (Runtime != "kaggle" || Algorithm is not null)) throw new ArgumentException("MLJAR requires Kaggle and selects models within the budget.");
        if (Kind == "spark-ml" && Runtime is not ("local" or "colab")) throw new ArgumentException("Spark ML uses local Spark or Colab; Kaggle AutoML and sklearn Databricks exports are separate.");
        if (RequireExecution && !Execute) throw new ArgumentException("requireExecution requires execute=true.");
        if ((Execute || RequireExecution) && Runtime is "colab" or "databricks-export") throw new ArgumentException("Colab and Databricks use explicit notebook execution, not this automated adapter.");
        if (DatasetId is not null) PublishTarget.RepoId(DatasetId);
        if (KernelId is not null) PublishTarget.RepoId(KernelId);
        if (Execute && Runtime == "kaggle" && (DatasetId is null || KernelId is null)) throw new ArgumentException("Kaggle execution requires explicit datasetId and kernelId.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PublishTarget
{
    public string Kind { get; set; } = "huggingface";
    public bool Execute { get; set; }
    public bool RequireExecution { get; set; }
    public string? ModelRepoId { get; set; }
    public string? SpaceRepoId { get; set; }
    public bool Public { get; set; }
    public string? Database { get; set; }
    public bool CreateDive { get; set; }
    internal static void RepoId(string value)
    {
        if (!Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,95}/[A-Za-z0-9][A-Za-z0-9_-]{0,95}\z")) throw new ArgumentException("Repo identity must be owner/slug with safe alphanumeric, dash or underscore characters.");
    }
    internal void Validate()
    {
        if (Kind is not ("huggingface" or "motherduck")) throw new ArgumentException("Unknown publish target.");
        if (RequireExecution && !Execute) throw new ArgumentException("requireExecution requires execute=true.");
        if (ModelRepoId is not null) RepoId(ModelRepoId);
        if (SpaceRepoId is not null) RepoId(SpaceRepoId);
        if (Kind == "huggingface" && Execute && ModelRepoId is null) throw new ArgumentException("HF publication requires an explicit modelRepoId.");
        if (Kind == "motherduck" && (Database is null || !Regex.IsMatch(Database, @"\A[A-Za-z_][A-Za-z0-9_]{0,95}\z"))) throw new ArgumentException("MotherDuck requires a safe fresh database name.");
    }
}
