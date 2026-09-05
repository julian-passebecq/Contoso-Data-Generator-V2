#nullable enable
using DatabaseGenerator.Forge.Architecture;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseGenerator.Forge.Planning;

/// <summary>Offline deployment intent. Evidence describes adapter history, never a run of this project.</summary>
public sealed class ResolvedPlan
{
    public string ContractVersion { get; set; } = "1.0.0";
    public string ProjectName { get; set; } = "";
    public PlanScenarioSelection BusinessScenario { get; set; } = new();
    public PlanGenerationProfile GenerationProfile { get; set; } = new();
    public string ArchitecturePreset { get; set; } = "";
    public ArchitectureSettings ResolvedSettings { get; set; } = new();
    public List<PlanStage> Stages { get; set; } = new();
    public List<PlanEdge> Edges { get; set; } = new();
    public List<PlanManualCheckpoint> ManualCheckpoints { get; set; } = new();
    public List<PlanCredential> RequiredCredentials { get; set; } = new();
    public List<string> CostAndQuotaNotes { get; set; } = new();
    public List<PlanArtifact> Artifacts { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string OverallReadiness { get; set; } = "declared";
    public string OverallImplementationStatus { get; set; } = "reference-only";
    public string CurrentExecutionStatus { get; set; } = "not-executed";
    public string EvidenceScope { get; set; } = "Historical adapter evidence only; this resolved project has not been executed.";
}

public sealed class PlanScenarioSelection
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Profile { get; set; } = "";
    public bool MlEnabled { get; set; }
}

public sealed class PlanGenerationProfile
{
    public int Orders { get; set; }
    public int Customers { get; set; }
    public int Products { get; set; }
    public int Stores { get; set; }
    public int TimeSpanDays { get; set; }
    public bool TimeSpanExplicit { get; set; }
    public int LabelEmbargoDays { get; set; } = 14;
    public int Seed { get; set; }
    public string StartDate { get; set; } = "";
}

public sealed class PlanStage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Runtime { get; set; } = "";
    public List<string> Inputs { get; set; } = new();
    public List<string> Outputs { get; set; } = new();
    public string ExecutionMode { get; set; } = "unsupported";
    public string ImplementationStatus { get; set; } = "reference-only";
    public string ValidationLevel { get; set; } = "declared";
    public bool Manual { get; set; }
    public List<PlanEvidence> Evidence { get; set; } = new();
    public string Reason { get; set; } = "";
    public string CompilerOperation { get; set; } = "unsupported";
    public string CompilerBoundary { get; set; } = "pipeline-activity";
    public string? Source { get; set; }
    public string? Sink { get; set; }
    public string? FileFormat { get; set; }
    public string? TableFormat { get; set; }
    public string? SparkApiMode { get; set; }
    public string? SparkVersion { get; set; }
}

public sealed class PlanEvidence
{
    public string Id { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Scope { get; set; } = "";
    public string ValidationLevel { get; set; } = "declared";
}

public sealed class PlanEdge
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class PlanManualCheckpoint
{
    public string AfterStage { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class PlanCredential
{
    public string Scope { get; set; } = "";
    public bool RequiredAtPlanTime { get; set; }
    public bool RequiredAtExecutionTime { get; set; } = true;
    public string Storage { get; set; } = "external-credential-provider; never-in-project-json";
    public string Reason { get; set; } = "";
}

public sealed class PlanArtifact
{
    public string Path { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string StageId { get; set; } = "";
}

public sealed class ScenarioDefinition
{
    public string ScenarioId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public PlanGenerationProfile GenerationProfile { get; set; } = new();
    public bool MlEnabled { get; set; }
    public string MlTask { get; set; } = "";
    public List<string> CompatibleArchitecturePresets { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResolvedPlan))]
[JsonSerializable(typeof(List<ScenarioDefinition>))]
public partial class PlanningJsonContext : JsonSerializerContext { }
