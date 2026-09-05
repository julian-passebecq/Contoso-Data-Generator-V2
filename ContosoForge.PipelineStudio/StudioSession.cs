using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Pipeline;
using DatabaseGenerator.Forge.Planning;

namespace ContosoForge.PipelineStudio;

/// <summary>Editor state wraps the existing contracts; no alternative graph is serialized.</summary>
public sealed class StudioSession
{
    public StudioProjectSpec Project { get; private set; } = new();
    public PipelineDefinition Pipeline { get; set; } = new();
    public string? ProjectPath { get; private set; }
    public string? PipelinePath { get; private set; }
    public string? CompilationRoot { get; private set; }
    private ResolvedPlan? plan;
    private string? plannedInput;
    public ResolvedPlan? Plan => plannedInput == ProjectJson + PipelineJson ? plan : null;
    public string? PlanJson => Plan is { } current ? PlanBuilder.ToJson(current) : null;
    public string PipelineJson => PipelineDocument.Write(Pipeline);
    public string ResolvedJson => ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(Project));
    public string ProjectJson => JsonSerializer.Serialize(Project, ArchitectureJsonContext.Default.StudioProjectSpec) + "\n";

    public void LoadProject(string path)
    {
        var parsed = ProjectSpecReader.Read(File.ReadAllText(path));
        Project = parsed.Studio ?? new StudioProjectSpec { SourceProject = parsed.Source };
        Pipeline = PipelineDocument.Read(PipelineCompiler.CreateDefault(ResolvedJson));
        ProjectPath = Path.GetFullPath(path);
        PipelinePath = null;
        InvalidateCompilation();
    }

    public void LoadPipeline(string path)
    {
        Pipeline = PipelineDocument.Read(File.ReadAllText(path));
        PipelinePath = Path.GetFullPath(path);
        InvalidateCompilation();
    }

    public void InvalidateCompilation()
    {
        CompilationRoot = null;
        plan = null;
        plannedInput = null;
    }

    public ResolvedPlan BuildPlan()
    {
        var resolved = PlanBuilder.Build(Project, PipelineJson);
        plan = resolved;
        plannedInput = ProjectJson + PipelineJson;
        return resolved;
    }

    public void ApplyScenario(string scenarioId)
    {
        Project = ScenarioCatalog.Apply(Project, scenarioId);
        InvalidateCompilation();
    }

    public void ApplyArchitecture(string presetId, string costProfile, string? scenarioId = null)
    {
        var defaultGraph = PipelineDocument.Write(PipelineDocument.Read(PipelineCompiler.CreateDefault(ResolvedJson)));
        var isDefaultGraph = PipelineJson == defaultGraph;
        var draft = JsonSerializer.Deserialize(ProjectJson, ArchitectureJsonContext.Default.StudioProjectSpec)!;
        if (scenarioId is not null) draft = ScenarioCatalog.Apply(draft, scenarioId);
        draft.Architecture = new ArchitectureSelection
        {
            PresetId = presetId,
            Overrides = presetId == Project.Architecture.PresetId ? draft.Architecture.Overrides : new ArchitectureSettings()
        };
        draft.Architecture.Overrides.CostProfile = costProfile;
        var resolved = ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(draft));
        var replacement = isDefaultGraph ? PipelineDocument.Read(PipelineCompiler.CreateDefault(resolved)) : Pipeline;
        Project = draft;
        Pipeline = replacement;
        InvalidateCompilation();
    }

    public void SavePlan(string path)
    {
        var json = PlanJson ?? throw new ArgumentException("Plan this revision before saving its resolved plan.");
        var destination = Path.GetFullPath(path);
        if (string.Equals(destination, ProjectPath, StringComparison.OrdinalIgnoreCase) || string.Equals(destination, PipelinePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A resolved plan must not overwrite the open project or pipeline source file. Choose a separate plan path.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, json);
    }

    public void ApplyOverrides(string json)
    {
        var overrides = JsonNode.Parse(json);
        if (overrides is not JsonObject) throw new ArgumentException("Architecture overrides must be a JSON object.");
        var document = JsonNode.Parse(ProjectJson)!;
        document["architecture"]!["overrides"] = overrides;
        var draft = ProjectSpecReader.Read(document.ToJsonString()).Studio!;
        var defaultGraph = PipelineDocument.Write(PipelineDocument.Read(PipelineCompiler.CreateDefault(ResolvedJson)));
        var replacement = PipelineJson == defaultGraph ? PipelineDocument.Read(PipelineCompiler.CreateDefault(ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(draft)))) : Pipeline;
        Project = draft;
        Pipeline = replacement;
        InvalidateCompilation();
    }

    public IReadOnlyList<string> Validate()
    {
        try
        {
            Project.SourceProject.Validate();
            return PipelineCompiler.Validate(PipelineJson, ResolvedJson);
        }
        catch (ArgumentException error) { return new[] { error.Message }; }
    }

    public void Save(string pipelinePath)
    {
        // Parse enforces the shared raw-credential guard even for incomplete drafts.
        _ = PipelineDocument.Read(PipelineJson);
        var root = Path.GetDirectoryName(Path.GetFullPath(pipelinePath))!;
        var companion = Path.Combine(root, "project.json");
        if (string.Equals(Path.GetFullPath(pipelinePath), companion, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The pipeline filename must differ from its sibling project.json.");
        var projectJson = ProjectJson;
        // A SaveFileDialog selecting a new pipeline filename does not authorize
        // replacement of another project's companion file. Check before writing
        // either member so a collision leaves the entire destination untouched.
        if (File.Exists(companion) && !string.Equals(ProjectPath, companion, StringComparison.OrdinalIgnoreCase) &&
            !File.ReadAllBytes(companion).AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(projectJson)))
            throw new ArgumentException("The destination already contains an unrelated project.json. Choose a different folder or open that project explicitly before saving its bundle.");
        Directory.CreateDirectory(root);
        File.WriteAllText(pipelinePath, PipelineJson);
        File.WriteAllText(companion, projectJson);
        PipelinePath = Path.GetFullPath(pipelinePath);
        ProjectPath = companion;
    }

    public void Compile(string output)
    {
        var errors = Validate();
        if (errors.Count != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
        if (Plan is null) throw new ArgumentException("Plan this revision before compiling. Review the resolved architecture and readiness first.");
        ForgeStudioCommand.Compile(Project, output, PipelineJson, includePlan: true);
        CompilationRoot = Path.GetFullPath(output);
    }

    public string Preview(string relative)
    {
        if (CompilationRoot is null) return "Compile this revision to preview generated artifacts. Cloud execution remains pending.";
        var path = Path.Combine(CompilationRoot, relative);
        return File.Exists(path) ? File.ReadAllText(path) : "The selected architecture does not generate this artifact.";
    }
}
