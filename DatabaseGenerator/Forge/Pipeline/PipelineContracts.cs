#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseGenerator.Forge.Pipeline;

/// <summary>The neutral, editable contract. Runtime exporters consume this model, never generated DAG code.</summary>
public sealed class PipelineDefinition
{
    public string ContractVersion { get; set; } = "1.2";
    public string Id { get; set; } = "contoso_forge";
    public string Name { get; set; } = "Contoso Forge";
    public Dictionary<string, PipelineParameter> Parameters { get; set; } = new();
    public Dictionary<string, JsonElement> Variables { get; set; } = new();
    public List<PipelineConnectionReference> Connections { get; set; } = new();
    public List<PipelineDataset> Datasets { get; set; } = new();
    public List<PipelineActivity> Activities { get; set; } = new();
    // Read compatibility for early Pipeline Studio examples; canonical output always uses activities.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PipelineActivity>? Nodes { get; set; }
    public List<PipelineDependency> Edges { get; set; } = new();
    public List<string> Annotations { get; set; } = new();
}

public sealed class PipelineParameter
{
    public string Type { get; set; } = "string";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Default { get; set; }
    public bool Required { get; set; }
    public string? Description { get; set; }
}

public sealed class PipelineConnectionReference
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? SecretProvider { get; set; }
    public string? SecretRef { get; set; }
    public Dictionary<string, JsonElement> NonSecretProperties { get; set; } = new();
}

public sealed class PipelineDataset
{
    public string Id { get; set; } = "";
    public string? ConnectionRef { get; set; }
    public string? Path { get; set; }
    public string? Table { get; set; }
    public string? Query { get; set; }
    public string? Format { get; set; }
    public string? TableFormat { get; set; }
    public string? SchemaRef { get; set; }
    public List<string> Partitioning { get; set; } = new();
    public Dictionary<string, JsonElement> Options { get; set; } = new();
}

public sealed class PipelineActivity
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Name { get; set; }
    public string? Implementation { get; set; }
    public string? Connector { get; set; }
    public string? ConnectionRef { get; set; }
    public string? Table { get; set; }
    public string? Profile { get; set; }
    public string? Source { get; set; }
    public string? Sink { get; set; }
    public string? Engine { get; set; }
    public string? Runtime { get; set; }
    public string? SparkApiMode { get; set; }
    public string? SparkVersionPolicy { get; set; }
    public string? SparkVersion { get; set; }
    public string? SparkRemote { get; set; }
    public string? FileFormat { get; set; }
    public string? TableFormat { get; set; }
    public List<string> Inputs { get; set; } = new();
    public List<string> Outputs { get; set; } = new();
    public Dictionary<string, JsonElement> Parameters { get; set; } = new();
    public PipelineRetry Retry { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 3600;
    public List<string> DependsOn { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

public sealed class PipelineRetry
{
    public int MaximumAttempts { get; set; } = 1;
    public int BackoffSeconds { get; set; } = 15;
}

public sealed class PipelineDependency
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class PipelineExecutionPlan
{
    public string ContractVersion { get; set; } = "1.2";
    public string PipelineId { get; set; } = "";
    public string PresetId { get; set; } = "";
    public string ArtifactStatus { get; set; } = "generated-reference";
    public string Runner { get; set; } = "pipeline/run_local.py";
    public List<PipelinePlannedActivity> Activities { get; set; } = new();
    public Dictionary<string, string> Exporters { get; set; } = new();
}

public sealed class PipelinePlannedActivity
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Operation { get; set; } = "unsupported";
    public string Status { get; set; } = "unsupported";
    public string Reason { get; set; } = "";
    public string? Engine { get; set; }
    public string? Runtime { get; set; }
    public string? SparkApiMode { get; set; }
    public string? SparkVersionPolicy { get; set; }
    public string? SparkVersion { get; set; }
    public string? SparkRemote { get; set; }
    public string? Source { get; set; }
    public string? Sink { get; set; }
    public List<string> DependsOn { get; set; } = new();
    public int MaximumAttempts { get; set; }
    public int BackoffSeconds { get; set; }
    public int TimeoutSeconds { get; set; }
}

public sealed class PipelineCompilationResult
{
    public required string OutputRoot { get; init; }
    public required IReadOnlyList<string> TopologicalOrder { get; init; }
    public required PipelineExecutionPlan Plan { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PipelineDefinition))]
[JsonSerializable(typeof(PipelineExecutionPlan))]
internal partial class PipelineJsonContext : JsonSerializerContext;
