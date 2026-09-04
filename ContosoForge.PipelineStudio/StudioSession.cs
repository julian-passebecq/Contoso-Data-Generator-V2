using System.IO;
using System.Text.Json;
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Pipeline;

namespace ContosoForge.PipelineStudio;

/// <summary>Editor state wraps the existing contracts; no alternative graph is serialized.</summary>
public sealed class StudioSession
{
    public StudioProjectSpec Project { get; private set; } = new();
    public PipelineDefinition Pipeline { get; set; } = new();
    public string? ProjectPath { get; private set; }
    public string? PipelinePath { get; private set; }
    public string? CompilationRoot { get; private set; }
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
        CompilationRoot = null;
    }

    public void LoadPipeline(string path)
    {
        Pipeline = PipelineDocument.Read(File.ReadAllText(path));
        PipelinePath = Path.GetFullPath(path);
        CompilationRoot = null;
    }

    public void InvalidateCompilation() => CompilationRoot = null;

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
        ForgeStudioCommand.Compile(Project, output, PipelineJson);
        CompilationRoot = Path.GetFullPath(output);
    }

    public string Preview(string relative)
    {
        if (CompilationRoot is null) return "Compile this revision to preview generated artifacts. Cloud execution remains pending.";
        var path = Path.Combine(CompilationRoot, relative);
        return File.Exists(path) ? File.ReadAllText(path) : "The selected architecture does not generate this artifact.";
    }
}
