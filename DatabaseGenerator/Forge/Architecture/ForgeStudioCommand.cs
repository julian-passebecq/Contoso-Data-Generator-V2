#nullable enable
using DatabaseGenerator.Forge.Export;
using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Pipeline;
using DatabaseGenerator.Forge.Planning;
using DatabaseGenerator.Forge.Specs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DatabaseGenerator.Forge.Architecture;

public static class ForgeStudioCommand
{
    public static void Initialize(string outputPath, string presetId)
    {
        var root = Path.GetFullPath(outputPath);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new ArgumentException("Project init requires an empty output directory.");
        var project = new StudioProjectSpec { Architecture = new() { PresetId = presetId } };
        var resolved = ArchitecturePresets.Resolve(project);
        var pipeline = PipelineCompiler.CreateDefault(ArchitecturePresets.ToJson(resolved));
        Directory.CreateDirectory(root);
        ForgeIo.WriteText(Path.Combine(root, "project.json"), JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec));
        ForgeIo.WriteText(Path.Combine(root, "pipeline.json"), pipeline);
    }

    public static void Compile(StudioProjectSpec project, string outputPath, string? pipelineJson = null, bool includePlan = false)
    {
        var root = Path.GetFullPath(outputPath);
        EnsureOutput(root);
        var fingerprint = "";
        var truthPath = Path.Combine(root, "truth_manifest.json");
        if (File.Exists(truthPath))
        {
            using var truth = JsonDocument.Parse(File.ReadAllText(truthPath));
            fingerprint = truth.RootElement.GetProperty("datasetFingerprint").GetString()!;
            var sourceJson = JsonSerializer.Serialize(project.SourceProject, ForgeJsonContext.Default.ProjectSpec);
            var sourceFingerprint = ForgeIo.Sha256Text(sourceJson.Replace("\r\n", "\n").TrimEnd() + "\n");
            if (truth.RootElement.GetProperty("projectFingerprint").GetString() != sourceFingerprint)
                throw new ArgumentException("The source project differs from the existing truth manifest. Run forge generate for the changed business configuration.");
        }
        var resolution = ArchitecturePresets.Resolve(project, fingerprint);
        var resolved = ArchitecturePresets.ToJson(resolution);
        pipelineJson ??= PipelineCompiler.CreateDefault(resolved);
        var diagnostics = PipelineCompiler.Validate(pipelineJson);
        if (diagnostics.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, diagnostics));
        var resolvedPlan = includePlan ? PlanBuilder.Build(project, pipelineJson) : null;

        // Stage all compiler outputs before replacing the previous compilation.
        var stage = Path.Combine(Path.GetTempPath(), "forge-compile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            if (File.Exists(truthPath)) File.Copy(truthPath, Path.Combine(stage, "truth_manifest.json"));
            ForgeIo.WriteText(Path.Combine(stage, "project.json"), JsonSerializer.Serialize(project, ArchitectureJsonContext.Default.StudioProjectSpec));
            ForgeIo.WriteText(Path.Combine(stage, "resolved_project.json"), resolved);
            ForgeIo.WriteText(Path.Combine(stage, ".gitignore"),
                ".forge/\nruns/\nlake/\n__pycache__/\n*.pyc\n*.zip\n*.tfstate\n*.tfstate.*\n*.tfplan\n.terraform/\n.env\ncolab/work_order.json\ncolab/result_manifest.json\n");
            var compilation = PipelineCompiler.Compile(pipelineJson, resolved, stage);
            ForgeIo.WriteText(Path.Combine(stage, "ARCHITECTURE.md"),
                $"# {resolution.Name}\n\nPreset: `{resolution.PresetId}`. Compilation status: `{compilation.Plan.ArtifactStatus}`.\n\n" +
                "| Choice | Value |\n| --- | --- |\n" +
                $"| Processing | {resolution.Settings.Engine} |\n| Runtime | {resolution.Settings.Runtime} |\n" +
                $"| Orchestrator | {resolution.Settings.Orchestrator} |\n| DAG distribution | {resolution.Settings.DagSource} |\n" +
                $"| Storage | {resolution.Settings.Storage} |\n| File format | {resolution.Settings.FileFormat} |\n" +
                $"| Table format | {resolution.Settings.TableFormat} |\n| Warehouse | {resolution.Settings.Warehouse} |\n" +
                $"| IaC | {resolution.Settings.Iac} |\n| Cost profile | {resolution.Settings.CostProfile} |\n\n" +
                string.Join("\n\n", resolution.Notes) +
                "\n\nEdit project.json and pipeline.json, then recompile. See local_plan.json for each activity's implementation/status, pipeline/graph.mmd for dependencies, and infra/gcp for the infrastructure preview when selected.\n");
            var canonical = File.ReadAllText(Path.Combine(stage, "pipeline.json"));
            BigQueryColabExporter.Export(stage, resolved, canonical);
            BigQueryAnalyticsExporter.Export(stage, resolved);
            FreeGcpInfrastructureExporter.Export(stage, resolved, canonical);
            FactoryExporter.Export(stage, project);
            if (resolvedPlan is not null)
                ForgeIo.WriteText(Path.Combine(stage, "plan", "resolved_plan.json"), PlanBuilder.ToJson(resolvedPlan));
            var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(stage, p).Replace('\\', '/'))
                .Where(p => p != "truth_manifest.json").OrderBy(p => p, StringComparer.Ordinal).ToList();
            var hashes = new JsonObject();
            foreach (var relative in files) hashes[relative] = ForgeIo.Sha256File(Path.Combine(stage, relative));
            var manifest = new JsonObject
            {
                ["contractVersion"] = "1.2", ["presetId"] = project.Architecture.PresetId,
                ["datasetFingerprint"] = fingerprint, ["artifactStatus"] = "generated-reference",
                ["generationDeterministic"] = true,
                ["executionEvidence"] = "No cloud deployment or hosted Colab execution is implied by compilation.",
                ["files"] = hashes
            };
            Publish(stage, root, files);
            ForgeIo.WriteText(Path.Combine(root, "run_manifest.json"), manifest.ToJsonString());
        }
        finally { Directory.Delete(stage, recursive: true); }
    }

    private static void EnsureOutput(string root)
    {
        if (!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any()) return;
        var forgeMarker = Path.Combine(root, ".contoso-forge-output");
        var compilerMarker = Path.Combine(root, ".contoso-forge-compiler");
        if ((File.Exists(forgeMarker) && File.ReadAllText(forgeMarker).Trim() == "contoso-forge-output-v1") ||
            (File.Exists(compilerMarker) && File.ReadAllText(compilerMarker).Trim() == "contoso-forge-compiler-v1.2")) return;
        throw new ArgumentException("Compilation requires an empty output directory or a Forge ownership marker.");
    }

    private static void Publish(string stage, string root, List<string> files)
    {
        var oldManifest = Path.Combine(root, "run_manifest.json");
        if (File.Exists(oldManifest))
        {
            using var old = JsonDocument.Parse(File.ReadAllText(oldManifest));
            if (old.RootElement.TryGetProperty("files", out var owned))
                foreach (var entry in owned.EnumerateObject())
                {
                    if (files.Contains(entry.Name, StringComparer.Ordinal)) continue;
                    var oldPath = SafeChild(root, entry.Name);
                    if (File.Exists(oldPath)) File.Delete(oldPath);
                }
        }
        Directory.CreateDirectory(root);
        foreach (var relative in files)
        {
            var destination = SafeChild(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(stage, relative), destination, overwrite: true);
        }
        ForgeIo.WriteText(Path.Combine(root, ".contoso-forge-compiler"), "contoso-forge-compiler-v1.2");
    }

    private static string SafeChild(string root, string relative)
    {
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Compiler manifest contains a path outside its output directory.");
        return target;
    }
}
