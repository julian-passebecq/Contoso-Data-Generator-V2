#nullable enable

using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Pipeline;
using DatabaseGenerator.Forge.Specs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DatabaseGenerator.Forge;

public static class ForgeCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                WriteHelp();
                return 0;
            }

            if (args.Length >= 2 && args[0] == "presets" && args[1] == "list")
            {
                Console.WriteLine(JsonSerializer.Serialize(ArchitecturePresets.List(), ArchitectureJsonContext.Default.ListArchitecturePreset));
                return 0;
            }
            if (args.Length >= 2 && args[0] == "project" && args[1] == "init")
            {
                var init = ParseOptions(args[2..]);
                if (!init.TryGetValue("output", out var initOutput)) throw new ArgumentException("--output is required.");
                ForgeStudioCommand.Initialize(initOutput, init.GetValueOrDefault("preset", ArchitecturePresets.DefaultPresetId));
                Console.WriteLine($"Created editable project.json and pipeline.json in {Path.GetFullPath(initOutput)}.");
                return 0;
            }
            var compile = args.Length >= 2 && args[0] == "pipeline" && args[1] == "compile";
            var validate = args[0] == "validate";
            if (!compile && !validate && !string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown forge command '{args[0]}'.");

            var options = ParseOptions(args[(compile ? 2 : 1)..]);
            if (!options.TryGetValue("project", out var projectPath))
                throw new ArgumentException("--project is required.");
            if (!options.TryGetValue("output", out var outputPath) && !validate)
                throw new ArgumentException("--output is required.");
            options.TryGetValue("lake", out var lakePath);

            var json = await File.ReadAllTextAsync(Path.GetFullPath(projectPath));
            var (spec, studio) = ProjectSpecReader.Read(json);
            if (options.TryGetValue("preset", out var preset))
            {
                studio ??= new StudioProjectSpec { SourceProject = spec };
                studio.Architecture.PresetId = preset;
            }
            if (compile || options.ContainsKey("pipeline"))
                studio ??= new StudioProjectSpec { SourceProject = spec };
            string? pipeline = null;
            if (options.TryGetValue("pipeline", out var pipelinePath)) pipeline = await File.ReadAllTextAsync(pipelinePath);
            else if (studio is not null)
            {
                var sibling = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "pipeline.json");
                if (File.Exists(sibling)) pipeline = await File.ReadAllTextAsync(sibling);
            }
            var resolved = studio is null ? null : ArchitecturePresets.ToJson(ArchitecturePresets.Resolve(studio));
            if (pipeline is not null)
            {
                var errors = resolved is null ? PipelineCompiler.Validate(pipeline) : PipelineCompiler.Validate(pipeline, resolved);
                if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
            }
            if (validate)
            {
                Console.WriteLine($"Valid project contract: {spec.Name}");
                return 0;
            }
            if (compile)
            {
                studio ??= new StudioProjectSpec { SourceProject = spec };
                ForgeStudioCommand.Compile(studio, outputPath!, pipeline);
                Console.WriteLine($"Compiled neutral pipeline into {Path.GetFullPath(outputPath!)}.");
                return 0;
            }

            var result = await new ForgeProjectGenerator().GenerateAsync(spec, outputPath!, lakePath);
            if (studio is not null)
            {
                ForgeStudioCommand.Compile(studio, outputPath!, pipeline);
                ForgeIo.WriteText(Path.Combine(result.OutputRoot, "project.json"), JsonSerializer.Serialize(studio, ArchitectureJsonContext.Default.StudioProjectSpec));
            }
            Console.WriteLine($"Contoso Forge generated '{spec.Name}'.");
            Console.WriteLine($"Output: {result.OutputRoot}");
            if (result.LakeRoot is not null)
                Console.WriteLine($"Lake raw: {Path.Combine(result.LakeRoot, "raw")}");
            Console.WriteLine($"Dataset fingerprint: {result.DatasetFingerprint}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"forge: {exception.Message}");
            return 2;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Expected '--name value', found '{args[index]}'.");
            result[args[index][2..]] = args[index + 1];
        }
        return result;
    }

    private static bool IsHelp(string value) =>
        string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) || value is "-h" or "--help";

    private static void WriteHelp()
    {
        Console.WriteLine("Contoso Forge — V1 generation and V1.2 architecture presets");
        Console.WriteLine("Usage: databasegenerator forge generate --project project.json --output out [--lake lake]");
        Console.WriteLine("       databasegenerator forge presets list");
        Console.WriteLine("       databasegenerator forge project init --output project [--preset free-gcp-lab]");
        Console.WriteLine("       databasegenerator forge validate --project project.json [--pipeline pipeline.json]");
        Console.WriteLine("       databasegenerator forge pipeline compile --project project.json --output compiled [--preset free-gcp-lab] [--pipeline pipeline.json]");
        Console.WriteLine("The legacy four-argument DatabaseGenerator CLI remains unchanged.");
    }
}
