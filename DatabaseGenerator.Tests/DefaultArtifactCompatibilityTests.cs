using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Planning;
using System.Security.Cryptography;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public sealed class DefaultArtifactCompatibilityTests
{
    [Fact]
    public async Task AllDefaultArtifactsRemainByteIdenticalToAuditedV13()
    {
        // Captured before this pass from audited HEAD407c1d2; contains public sample paths/hashes only.
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(fixtures, "v13-default-hashes.json")))!;
        var (source, studio) = ProjectSpecReader.Read(File.ReadAllText(Path.Combine(fixtures, "v13-default.project.json")));
        var root = Path.Combine(Path.GetTempPath(), "forge-v13-default-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new ForgeProjectGenerator().GenerateAsync(source, root);
            ForgeStudioCommand.Compile(studio!, root);
            var actual = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            Assert.Equal(152, expected.Count);
            Assert.Equal(expected.Keys.OrderBy(key => key, StringComparer.Ordinal), actual.Keys.OrderBy(key => key, StringComparer.Ordinal));
            foreach (var (path, sha256) in expected) Assert.True(sha256 == actual[path], $"Default artifact changed: {path}. Preserve the audited V1.3 output or introduce an explicit opt-in path.");
            var plan = PlanBuilder.Build(studio!);
            var generation = Assert.Single(plan.Stages, stage => stage.CompilerBoundary == "generation-prerequisite");
            foreach (var artifact in plan.Artifacts.Where(artifact => artifact.StageId == generation.Id && artifact.Path != "plan/resolved_plan.json"))
                Assert.True(actual.ContainsKey(artifact.Path), $"The plan promises a generated bridge file that does not exist: {artifact.Path}.");
            foreach (var output in generation.Outputs)
                Assert.True(File.Exists(Path.Combine(root, output)) || Directory.Exists(Path.Combine(root, output)), $"The source-generation stage promises a missing output: {output}.");
            Assert.Contains("models/semantic_model.json", generation.Outputs);
            Assert.DoesNotContain(plan.Artifacts, artifact => artifact.Path == "models/semantic_intent.json");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
