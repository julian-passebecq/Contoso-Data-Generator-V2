using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Generation;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public class ArchitecturePresetTests
{
    [Fact]
    public void DefaultIsFreeGcpLabAndResolutionIsDeterministic()
    {
        var project = new StudioProjectSpec();
        var first = ArchitecturePresets.Resolve(project);
        var second = ArchitecturePresets.Resolve(project);
        Assert.Equal("free-gcp-lab", first.PresetId);
        Assert.Equal("gcp-sandbox-no-card", first.Settings.CostProfile);
        Assert.Equal("local", first.Settings.Storage);
        Assert.Equal("opentofu", first.Settings.Iac);
        Assert.Equal(ArchitecturePresets.ToJson(first), ArchitecturePresets.ToJson(second));
    }

    [Fact]
    public void SelectionIsEditableWithoutMutatingRegistryOrSource()
    {
        var project = new StudioProjectSpec();
        project.Architecture.Overrides.Storage = "azure-adls";
        project.Architecture.Overrides.Warehouse = "none";
        project.Architecture.Overrides.Runtime = "docker";
        project.Architecture.Overrides.CostProfile = "external";
        var resolved = ArchitecturePresets.Resolve(project);
        Assert.Equal("azure-adls", resolved.Settings.Storage);
        Assert.Equal("docker", resolved.Settings.Runtime);
        Assert.Equal("local", ArchitecturePresets.Get("free-gcp-lab").Defaults.Storage);
        Assert.Equal("1.0.0", project.SourceProject.Version);
        project.SourceProject.Validate();
    }

    [Theory]
    [InlineData("gcs", "bigquery", "gcp-sandbox-no-card", "none")]
    [InlineData("local", "biglake", "gcp-sandbox-no-card", "iceberg")]
    [InlineData("local", "bigquery", "gcp-sandbox-no-card", "delta")]
    [InlineData("gcs", "bigquery", "local", "none")]
    public void RejectsCostAndFormatConflicts(string storage, string warehouse, string cost, string format)
    {
        var project = new StudioProjectSpec();
        project.Architecture.Overrides = new() { Storage = storage, Warehouse = warehouse, CostProfile = cost, TableFormat = format };
        Assert.Throws<ArgumentException>(() => ArchitecturePresets.Resolve(project));
    }

    [Fact]
    public void EveryPresetResolvesAndReturnedDefaultsAreIsolated()
    {
        foreach (var preset in ArchitecturePresets.List())
        {
            var project = new StudioProjectSpec { Architecture = new() { PresetId = preset.PresetId } };
            Assert.Equal(preset.PresetId, ArchitecturePresets.Resolve(project).PresetId);
        }
        ArchitecturePresets.Get("free-gcp-lab").Defaults.Warehouse = "sqlserver";
        Assert.Equal("bigquery", ArchitecturePresets.Get("free-gcp-lab").Defaults.Warehouse);
    }

    [Fact]
    public void VersionDispatchKeepsV1StrictAndAcceptsNewEnvelope()
    {
        var project = new StudioProjectSpec { SourceProject = ForgeTestProject.CreateSmallSpec() };
        var json = JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec);
        var (source, studio) = ProjectSpecReader.Read(json);
        Assert.NotNull(studio);
        Assert.Equal("1.0.0", source.Version);
        var legacy = JsonSerializer.Serialize(project.SourceProject, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Null(ProjectSpecReader.Read(legacy).Studio);
        Assert.Throws<ArgumentException>(() => ProjectSpecReader.Read(legacy.Replace("1.0.0", "1.1.0")));
        Assert.Throws<JsonException>(() => ProjectSpecReader.Read(json.Replace("\"gcp\":", "\"typo\":")));
    }

    [Theory]
    [InlineData("https://user:password@github.com/org/repo.git", "main", "dags")]
    [InlineData("https://github.com/org/repo.git?token=secret", "main", "dags")]
    [InlineData("https://github.com/org/repo.git", "main", "../dags")]
    public void RejectsCredentialsAndGitTraversal(string repository, string branch, string subpath)
    {
        var project = new StudioProjectSpec { Git = new() { Repository = repository, Branch = branch, SubPath = subpath } };
        Assert.Throws<ArgumentException>(() => ArchitecturePresets.Resolve(project));
    }

    [Fact]
    public async Task BusinessDataFingerprintDoesNotDependOnPreset()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-preset-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = ForgeTestProject.CreateSmallSpec();
            var sandbox = new StudioProjectSpec { SourceProject = source };
            var azure = new StudioProjectSpec { SourceProject = source, Architecture = new() { PresetId = "azure-adls-airflow" } };
            ArchitecturePresets.Resolve(sandbox);
            ArchitecturePresets.Resolve(azure);
            var generator = new ForgeProjectGenerator();
            var first = await generator.GenerateAsync(sandbox.SourceProject, Path.Combine(root, "gcp"));
            var second = await generator.GenerateAsync(azure.SourceProject, Path.Combine(root, "azure"));
            Assert.Equal(first.DatasetFingerprint, second.DatasetFingerprint);
            Assert.Equal(File.ReadAllBytes(Path.Combine(root, "gcp", "truth_manifest.json")), File.ReadAllBytes(Path.Combine(root, "azure", "truth_manifest.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
