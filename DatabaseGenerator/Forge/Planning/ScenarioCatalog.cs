#nullable enable
using DatabaseGenerator.Forge.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DatabaseGenerator.Forge.Planning;

public static class ScenarioCatalog
{
    public const string DefaultScenarioId = "retail.customer_satisfaction";
    public const string MlScenarioId = "retail.customer_satisfaction_ml";

    // Fresh values prevent an editor from changing global catalog defaults.
    public static List<ScenarioDefinition> List() => new()
    {
        new()
        {
            ScenarioId = DefaultScenarioId, DisplayName = "Retail Customer Satisfaction", ProfileId = "default-60d",
            Description = "Orders, shipments, returns, support and reviews with deterministic duplicates, CDC, late arrivals, SCD2 and quality problems.",
            GenerationProfile = new() { Orders = 120, Customers = 48, Products = 16, Stores = 4, TimeSpanDays = 60, Seed = 20260904, StartDate = "2024-01-01T00:00:00Z" },
            MlTask = "classification-ready semantic intent",
            CompatibleArchitecturePresets = ArchitecturePresets.List().Select(p => p.PresetId).ToList()
        },
        new()
        {
            ScenarioId = MlScenarioId, DisplayName = "Retail Customer Satisfaction ML", ProfileId = "learning-365d",
            Description = "The same entity graph with a longer temporal profile and a 14-day label embargo; training is an explicit cost-authorized step.",
            GenerationProfile = new() { Orders = 1200, Customers = 240, Products = 48, Stores = 4, TimeSpanDays = 365, TimeSpanExplicit = true, Seed = 20260904, StartDate = "2024-01-01T00:00:00Z" },
            MlEnabled = true, MlTask = "dissatisfaction classification before review or survey outcomes",
            CompatibleArchitecturePresets = ArchitecturePresets.List().Select(p => p.PresetId).ToList()
        }
    };

    public static ScenarioDefinition Get(string id) => List().SingleOrDefault(s => s.ScenarioId == id)
        ?? throw new ArgumentException($"Unknown business scenario '{id}'. Use 'forge scenarios list'.");

    public static string ToJson() => JsonSerializer.Serialize(List(), PlanningJsonContext.Default.ListScenarioDefinition).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    /// <summary>Explicit selection clones the document and changes only profile quantities/horizon and scenario intent.
    /// Re-selecting the current scenario preserves custom quantities. Planning itself never applies a profile.</summary>
    public static StudioProjectSpec Apply(StudioProjectSpec project, string scenarioId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var scenario = Get(scenarioId);
        var clone = JsonSerializer.Deserialize(JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec),
            ArchitectureJsonContext.Default.StudioProjectSpec)!;
        if ((project.BusinessScenario ?? DefaultScenarioId) == scenarioId) return clone;
        clone.BusinessScenario = scenarioId;
        var generation = clone.SourceProject.Generation;
        generation.Orders = scenario.GenerationProfile.Orders;
        generation.Customers = scenario.GenerationProfile.Customers;
        generation.Products = scenario.GenerationProfile.Products;
        generation.TimeSpanDays = scenario.MlEnabled ? scenario.GenerationProfile.TimeSpanDays : null;
        return clone;
    }
}
