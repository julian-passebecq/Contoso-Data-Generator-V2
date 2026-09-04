using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DatabaseGenerator.Forge.Specs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProjectSpec
{
    [JsonRequired]
    public string Version { get; set; } = "1.0.0";
    [JsonRequired]
    public string Name { get; set; } = "customer-satisfaction-local";
    [JsonRequired]
    public string Scenario { get; set; } = "retail.customer_satisfaction";
    [JsonRequired]
    public GenerationSpec Generation { get; set; } = new();
    [JsonRequired]
    public ExtensionSpec Extensions { get; set; } = new();
    [JsonRequired]
    public ProblemSpec Problems { get; set; } = new();
    [JsonRequired]
    public OutputSpec Outputs { get; set; } = new();
    [JsonRequired]
    public LabSpec Lab { get; set; } = new();

    public void Validate()
    {
        if (!string.Equals(Version, "1.0.0", StringComparison.Ordinal))
            throw new ArgumentException($"Unsupported ProjectSpec version '{Version}'. Expected '1.0.0'.");
        if (!string.Equals(Scenario, "retail.customer_satisfaction", StringComparison.Ordinal))
            throw new ArgumentException($"Unsupported scenario '{Scenario}'. V1 supports 'retail.customer_satisfaction'.");
        if (string.IsNullOrWhiteSpace(Name) ||
            !Regex.IsMatch(Name, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant))
            throw new ArgumentException(
                "Project name must be 1-128 ASCII letters, digits, dots, underscores, or hyphens, " +
                "and must start with a letter or digit.");
        if (Generation.Seed < 0)
            throw new ArgumentException("generation.seed must be zero or greater.");
        if (Generation.Orders < 12)
            throw new ArgumentException("generation.orders must be at least 12 so every V1 injector has a stable target.");
        if (Generation.TimeSpanDays is < 1 or > 3650)
            throw new ArgumentException("generation.timeSpanDays must be between 1 and 3650 when specified; omission preserves the 60-day horizon.");
        if (Generation.Customers < 8)
            throw new ArgumentException("generation.customers must be at least 8.");
        if (Generation.Products < 4)
            throw new ArgumentException("generation.products must be at least 4.");
        if (Generation.Stores < 2)
            throw new ArgumentException("generation.stores must be at least 2.");
        if (!Regex.IsMatch(
                Generation.StartDate,
                "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]+)?(?:Z|[+-][0-9]{2}:[0-9]{2})$",
                RegexOptions.CultureInvariant) ||
            !DateTimeOffset.TryParse(
                Generation.StartDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
            throw new ArgumentException("generation.startDate must be an RFC 3339 date-time with an explicit UTC offset.");
        if (!Generation.LegacyContosoMode)
            throw new ArgumentException("generation.legacyContosoMode must be true in V1 so the upstream compatibility contract remains explicit.");
        if (Generation.Formats.Count != 1 ||
            !string.Equals(Generation.Formats.SingleOrDefault(), "CSV", StringComparison.Ordinal))
            throw new ArgumentException(
                "generation.formats supports only CSV for the Forge V1 source contract. " +
                "Use the unchanged legacy CLI for its PARQUET and DELTATABLE outputs.");

        if (!Extensions.Shipments || !Extensions.ShipmentEvents || !Extensions.Returns ||
            !Extensions.SupportTickets || !Extensions.Reviews)
            throw new ArgumentException("The V1 customer-satisfaction slice requires every extension entity.");

        if (!Problems.Duplicates || !Problems.Cdc || !Problems.LateArrivals ||
            !Problems.Scd2 || !Problems.QualityIssues)
            throw new ArgumentException("The V1 customer-satisfaction slice requires every deterministic problem injector.");

        if (!Outputs.Sql || !Outputs.Pyspark || !Outputs.Dbt || !Outputs.Airflow ||
            !Outputs.SemanticModel || !Outputs.MlSpec)
            throw new ArgumentException("The V1 customer-satisfaction slice requires every declared output artifact.");

        if (!Lab.Docker || !Lab.Airflow || !Lab.Spark || !Lab.DbtDuckdb ||
            !Lab.KubernetesKind || !Lab.OpenTofu)
            throw new ArgumentException("The V1 reference project requires every declared local lab target.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GenerationSpec
{
    [JsonRequired]
    public bool LegacyContosoMode { get; set; } = true;
    [JsonRequired]
    public int Seed { get; set; } = 20260904;
    [JsonRequired]
    public int Orders { get; set; } = 120;
    [JsonRequired]
    public int Customers { get; set; } = 48;
    [JsonRequired]
    public int Products { get; set; } = 16;
    [JsonRequired]
    public int Stores { get; set; } = 4;
    [JsonRequired]
    public string StartDate { get; set; } = "2024-01-01T00:00:00Z";
    // Omission preserves both the V1 date horizon and its serialized project fingerprint.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeSpanDays { get; set; }
    [JsonRequired]
    public List<string> Formats { get; set; } = new() { "CSV" };
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ExtensionSpec
{
    [JsonRequired]
    public bool Shipments { get; set; } = true;
    [JsonRequired]
    public bool ShipmentEvents { get; set; } = true;
    [JsonRequired]
    public bool Returns { get; set; } = true;
    [JsonRequired]
    public bool SupportTickets { get; set; } = true;
    [JsonRequired]
    public bool Reviews { get; set; } = true;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProblemSpec
{
    [JsonRequired]
    public bool Duplicates { get; set; } = true;
    [JsonRequired]
    public bool Cdc { get; set; } = true;
    [JsonRequired]
    public bool LateArrivals { get; set; } = true;
    [JsonRequired]
    public bool Scd2 { get; set; } = true;
    [JsonRequired]
    public bool QualityIssues { get; set; } = true;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OutputSpec
{
    [JsonRequired]
    public bool Sql { get; set; } = true;
    [JsonRequired]
    public bool Pyspark { get; set; } = true;
    [JsonRequired]
    public bool Dbt { get; set; } = true;
    [JsonRequired]
    public bool Airflow { get; set; } = true;
    [JsonRequired]
    public bool SemanticModel { get; set; } = true;
    [JsonRequired]
    public bool MlSpec { get; set; } = true;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LabSpec
{
    [JsonRequired]
    public bool Docker { get; set; } = true;
    [JsonRequired]
    public bool Airflow { get; set; } = true;
    [JsonRequired]
    public bool Spark { get; set; } = true;
    [JsonRequired]
    public bool DbtDuckdb { get; set; } = true;
    [JsonRequired]
    public bool KubernetesKind { get; set; } = true;
    [JsonRequired]
    public bool OpenTofu { get; set; } = true;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ProjectSpec))]
[JsonSerializable(typeof(TruthManifest))]
internal partial class ForgeJsonContext : JsonSerializerContext
{
}
