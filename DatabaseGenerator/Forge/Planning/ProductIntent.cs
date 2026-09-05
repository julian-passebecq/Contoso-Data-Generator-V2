#nullable enable
using DatabaseGenerator.Forge.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace DatabaseGenerator.Forge.Planning;

/// <summary>Opt-in V1.5 intent inside the existing project, never a second generation contract.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProductIntent
{
    public string Version { get; set; } = "1.5";
    public string PipelineMode { get; set; } = "full-batch";
    public string MlTarget { get; set; } = "local-sklearn";
    public string BiTarget { get; set; } = "evidence";
    public string DbtIntegration { get; set; } = "plain";
    public string? LabelAsOf { get; set; }
    public int MaterializationLimitMb { get; set; } = 256;

    public static readonly string[] Steps = { "Business", "Data", "Pipeline mode", "Architecture", "Orchestration", "dbt", "ML design", "BI & Validation", "Run", "Monitor / Results" };
    public static readonly string[] Modes = { "full-batch", "incremental-cdc", "scd2", "late-arrival-backfill", "quality-quarantine", "ml-features" };
    public static readonly string[] MlTargets = { "local-sklearn", "colab-sklearn", "colab-spark-ml", "kaggle-sklearn", "bqml-export", "databricks-export" };

    public void Validate(StudioProjectSpec project, ArchitectureSettings settings)
    {
        if (Version is not ("1.5" or "1.6") || !Modes.Contains(PipelineMode) || !MlTargets.Contains(MlTarget)
            || BiTarget is not ("evidence" or "evidence-and-dive") || DbtIntegration is not ("plain" or "cosmos"))
            throw new ArgumentException("Invalid product version (1.5/1.6), pipeline mode, ML, BI or dbt target.");
        if (MaterializationLimitMb is < 16 or > 4096) throw new ArgumentException("materializationLimitMb must be between 16 and 4096.");
        if (LabelAsOf is not null && (!LabelAsOf.EndsWith("Z", StringComparison.Ordinal) || !DateTimeOffset.TryParse(LabelAsOf, out _)))
            throw new ArgumentException("product.labelAsOf must be an explicit UTC timestamp ending in Z.");
        if (BiTarget == "evidence-and-dive" && settings.Warehouse != "motherduck")
            throw new ArgumentException("MotherDuck Dive is available only with warehouse=motherduck.");
        if (PipelineMode == "ml-features" && project.BusinessScenario != ScenarioCatalog.MlScenarioId)
            throw new ArgumentException("ML feature preparation requires the customer satisfaction ML business scenario.");
        if (DbtIntegration == "cosmos" && !settings.Orchestrator!.StartsWith("airflow", StringComparison.Ordinal))
            throw new ArgumentException("Cosmos requires orchestrator=airflow; plain dbt remains the local fallback.");
    }
}

public sealed class ProductDesign
{
    public string Version { get; set; } = "1.5";
    public List<string> Steps { get; set; } = ProductIntent.Steps.ToList();
    public string Industry { get; set; } = "Retail / ecommerce / omnichannel";
    public string BusinessProblem { get; set; } = "Customer satisfaction and fulfillment";
    public string OperationalDecision { get; set; } = "Identify fulfillment problems and prioritize customer follow-up at delivery.";
    public string GenerationContract { get; set; } = "sourceProject.generation and sourceProject.problems";
    public string DataBehavior { get; set; } = "Deterministic duplicates, CDC insert/update/delete, SCD2, late arrivals and quarantined rows. Quantity/horizon are configurable. Optional sourceProject.generation.ml profile causal-v1 controls positiveOutcomeRate, signalStrength and noiseLevel for subsequent review/survey outcomes; omission preserves legacy data.";
    public string PipelineMode { get; set; } = "full-batch";
    public string ModeSemantics { get; set; } = "Reproducible batch replay; selected CDC/SCD2/backfill/quality modes focus the existing injected exercises, not a persistent streaming service.";
    public string Orchestrator { get; set; } = "local-sequential";
    public string? AirflowHost { get; set; }
    public string DbtFlow { get; set; } = "Silver -> staging -> intermediate -> marts/Gold -> dbt tests -> truth reconciliation";
    public string BiTarget { get; set; } = "evidence";
    public List<string> BiInputs { get; set; } = new() { "Gold", "models/kpi_catalog.json", "models/semantic_model.json", "truth_manifest.json", "pipeline evidence", "dbt manifest.json", "dbt run_results.json", "measured ML outputs when enabled" };
    public MlExperimentDesign? Ml { get; set; }
}

public sealed class MlExperimentDesign
{
    public string Framework { get; set; } = "scikit-learn";
    public string SecondaryFramework { get; set; } = "spark-ml";
    public string RuntimeTarget { get; set; } = "local-sklearn";
    public string ProblemType { get; set; } = "binary_classification";
    public string PredictionGrain { get; set; } = "one completed order / order_key";
    public string PredictionTimestamp { get; set; } = "delivered_at";
    public string Target { get; set; } = "is_dissatisfied_14d";
    public string LabelTimestamp { get; set; } = "delivered_at + 14 days";
    public int MinimumLabelDelayDays { get; set; } = 14;
    public int EmbargoDays { get; set; } = 14;
    public string SplitStrategy { get; set; } = "chronological 70/15/15 by prediction_time; earlier labels mature strictly before the next partition";
    public string LabelPolicy { get; set; } = "A valid review or closed support score <= 2 within (delivery, delivery + 14 days] is positive; absence is negative only after maturity, not proof of satisfaction.";
    public List<string> Features { get; set; } = new() { "sales_amount", "item_quantity", "store_channel", "country_code", "customer_loyalty_tier_as_of_order", "promised_transit_hours", "actual_transit_hours", "delivery_delay_hours", "is_on_time", "shipment_event_count_at_delivery" };
    public Dictionary<string, string> FeatureAvailability { get; set; } = new()
    {
        ["sales_amount"] = "order_date", ["item_quantity"] = "order_date", ["store_channel"] = "order_date",
        ["country_code"] = "order_date", ["customer_loyalty_tier_as_of_order"] = "order_date; CDC event_time AND ingested_at <= order_date",
        ["promised_transit_hours"] = "shipped_at", ["actual_transit_hours"] = "delivered_at", ["delivery_delay_hours"] = "delivered_at",
        ["is_on_time"] = "delivered_at", ["shipment_event_count_at_delivery"] = "event_time AND ingested_at <= delivered_at"
    };
    public List<string> LeakageExclusions { get; set; } = new() { "review_rating", "review_text", "average_review_rating", "average_support_satisfaction", "satisfaction_outcome", "returned_flag", "refund_amount", "support_closed_at", "late_arrival_event_count", "order_key", "customer_key" };
    public List<string> CandidateAlgorithms { get; set; } = new() { "dummy", "logistic_regression", "random_forest", "histogram_gradient_boosting" };
    public List<string> Metrics { get; set; } = new() { "average_precision", "roc_auc", "f1", "precision", "recall", "confusion_matrix" };
    public string TrainingStatus { get; set; } = "not-executed";
}
