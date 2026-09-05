using DatabaseGenerator.Forge.Architecture;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public sealed class ArchitectureGitRequirementsTests
{
    [Theory]
    [InlineData("local-fast")]
    [InlineData("local-spark")]
    public void LocalArchitectureAllowsAbsentAndEmptyGit(string preset)
    {
        var project = new StudioProjectSpec { Architecture = new() { PresetId = preset }, Git = null };
        Assert.Equal("local", ArchitecturePresets.Resolve(project).Settings.DagSource);
        var json = JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec);
        Assert.NotNull(ProjectSpecReader.Read(json).Studio);
        project.Git = new() { Repository = "", Branch = "", SubPath = "", ProjectSubPath = "" };
        Assert.Equal(preset, ArchitecturePresets.Resolve(project).PresetId);
    }

    [Theory]
    [InlineData("free-gcp-lab")]
    [InlineData("free-gcp-connect")]
    public void GitSyncNeedsAnActualSafeRepositoryReference(string preset)
    {
        var project = new StudioProjectSpec { Architecture = new() { PresetId = preset }, Git = null };
        Assert.Contains("git.repository", Assert.Throws<ArgumentException>(() => ArchitecturePresets.Resolve(project)).Message);
        project.Git = new() { Repository = "" };
        Assert.Throws<ArgumentException>(() => ArchitecturePresets.Resolve(project));
        project.Git = new() { Repository = "https://github.com/julian-passebecq/Contoso-Data-Generator-V2.git" };
        Assert.Equal("github-gitsync", ArchitecturePresets.Resolve(project).Settings.DagSource);
    }

    [Theory]
    [InlineData("https://user:password@github.com/org/repo.git", "main", "generated")]
    [InlineData("https://github.com/org/repo.git?token=secret", "main", "generated")]
    [InlineData("https://github.com/org/repo.git", "../main", "generated")]
    [InlineData("https://github.com/org/repo.git", "main", "../generated")]
    public void ProvidedGitRemainsCredentialAndTraversalSafeEvenForLocalPlans(string url, string branch, string projectPath)
    {
        var project = new StudioProjectSpec
        {
            Architecture = new() { PresetId = "local-fast" },
            Git = new() { Repository = url, Branch = branch, ProjectSubPath = projectPath }
        };
        Assert.Throws<ArgumentException>(() => ArchitecturePresets.Resolve(project));
    }

    [Fact]
    public void ExplicitConnectPresetPreservesClassicDefaultsAndNativeBatchFormat()
    {
        var classic = ArchitecturePresets.Resolve(new StudioProjectSpec());
        var connect = ArchitecturePresets.Resolve(new StudioProjectSpec { Architecture = new() { PresetId = "free-gcp-connect" } });
        Assert.Equal("free-gcp-lab", classic.PresetId);
        Assert.Equal("classic", classic.Settings.SparkApiMode);
        Assert.Equal("google-colab", classic.Settings.Runtime);
        Assert.Equal("connect-local", connect.Settings.SparkApiMode);
        Assert.Equal("google-colab-connect-local", connect.Settings.Runtime);
        Assert.Equal("4.0.4", connect.Settings.SparkVersion);
        Assert.Equal("bigquery", connect.Settings.Warehouse);
        Assert.Equal("none", connect.Settings.TableFormat);
        Assert.Equal("parquet", connect.Settings.FileFormat);
        Assert.Equal("airflow-minikube", connect.Settings.Orchestrator);
    }

    [Theory]
    [InlineData("https://github.com/org/{{repo}}.git")]
    [InlineData("https://github.com/org/repo\n.git")]
    [InlineData("https://github.com/org/repo\t.git")]
    public void GitSyncRejectsValuesThatCannotBeSafelyRenderedByHelm(string repository)
    {
        var project = new StudioProjectSpec { Git = new() { Repository = repository } };
        Assert.Throws<ArgumentException>(() => ArchitecturePresets.Resolve(project));
    }
}
