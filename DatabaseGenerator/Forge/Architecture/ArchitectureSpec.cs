#nullable enable
using DatabaseGenerator.Forge.Specs;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatabaseGenerator.Forge.Architecture;

// Deployment choices are an envelope around the unchanged business-generation contract.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StudioProjectSpec
{
    [JsonRequired] public string Version { get; set; } = "1.2.0";
    [JsonRequired] public ProjectSpec SourceProject { get; set; } = new();
    // Optional planner scenario; the source-system entity graph remains the V1 contract.
    public string? BusinessScenario { get; set; }
    public Planning.ProductIntent? Product { get; set; }
    public ArchitectureSelection Architecture { get; set; } = new();
    public GcpOptions Gcp { get; set; } = new();
    public GitOptions? Git { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ArchitectureSelection
{
    public string PresetId { get; set; } = "free-gcp-lab";
    public ArchitectureSettings Overrides { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ArchitectureSettings
{
    public string? Engine { get; set; }
    public string? Runtime { get; set; }
    public string? Orchestrator { get; set; }
    public string? DagSource { get; set; }
    public string? Storage { get; set; }
    public string? FileFormat { get; set; }
    public string? TableFormat { get; set; }
    public string? Warehouse { get; set; }
    public string? Iac { get; set; }
    public string? CostProfile { get; set; }
    public string? SparkApiMode { get; set; }
    public string? SparkVersionPolicy { get; set; }
    public string? SparkVersion { get; set; }
    public string? SparkRemote { get; set; }
    public string? AirflowHost { get; set; }
    public string? Executor { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GcpOptions
{
    public string ProjectId { get; set; } = "your-gcp-project";
    public string Dataset { get; set; } = "contoso_forge";
    public string Location { get; set; } = "US";
    public long MaximumBytesBilled { get; set; } = 1_000_000_000;
    public string? BucketName { get; set; }
    public List<string> IamMembers { get; set; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class GitOptions
{
    public string Repository { get; set; } = "https://github.com/your-account/your-repository.git";
    public string Branch { get; set; } = "main";
    public string SubPath { get; set; } = "generated/airflow/dags";
    public string ProjectSubPath { get; set; } = "generated";
}

public sealed class ArchitecturePreset
{
    public string ContractVersion { get; set; } = "1.0";
    public string PresetId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ArtifactStatus { get; set; } = "generated-reference";
    public ArchitectureSettings Defaults { get; set; } = new();
    public List<string> CapabilityRequirements { get; set; } = new();
}

public sealed class ResolvedProject
{
    public Planning.ProductIntent? Product { get; set; }
    public string? BusinessScenario { get; set; }
    public string ContractVersion { get; set; } = "1.2";
    public string PresetId { get; set; } = "";
    public string Name { get; set; } = "";
    public ArchitectureSettings Settings { get; set; } = new();
    public GcpOptions Gcp { get; set; } = new();
    public GitOptions? Git { get; set; } = new();
    public string DatasetFingerprint { get; set; } = "";
    public string ArtifactStatus { get; set; } = "generated-reference";
    public List<string> Notes { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StudioProjectSpec))]
[JsonSerializable(typeof(ArchitecturePreset))]
[JsonSerializable(typeof(List<ArchitecturePreset>))]
[JsonSerializable(typeof(ResolvedProject))]
public partial class ArchitectureJsonContext : JsonSerializerContext { }

public static class ProjectSpecReader
{
    public static (ProjectSpec Source, StudioProjectSpec? Studio) Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        var version = document.RootElement.GetProperty("version").GetString();
        if (version == "1.0.0")
        {
            var source = JsonSerializer.Deserialize(json, ForgeJsonContext.Default.ProjectSpec)!;
            source.Validate();
            return (source, null);
        }
        if (version == "1.2.0")
        {
            var studio = JsonSerializer.Deserialize(json, ArchitectureJsonContext.Default.StudioProjectSpec)!;
            if (studio.SourceProject is null || studio.Architecture is null || studio.Gcp is null)
                throw new ArgumentException("Studio project objects cannot be null.");
            studio.SourceProject.Validate();
            if (studio.BusinessScenario is not null)
                _ = Planning.ScenarioCatalog.Get(studio.BusinessScenario);
            ArchitecturePresets.Resolve(studio);
            return (studio.SourceProject, studio);
        }
        throw new ArgumentException($"Unsupported ProjectSpec version '{version}'. Supported versions: 1.0.0, 1.2.0.");
    }
}
