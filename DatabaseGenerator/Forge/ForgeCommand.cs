#nullable enable

using DatabaseGenerator.Forge.Generation;
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

            if (!string.Equals(args[0], "generate", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown forge command '{args[0]}'.");

            var options = ParseOptions(args[1..]);
            if (!options.TryGetValue("project", out var projectPath))
                throw new ArgumentException("--project is required.");
            if (!options.TryGetValue("output", out var outputPath))
                throw new ArgumentException("--output is required.");
            options.TryGetValue("lake", out var lakePath);

            var json = await File.ReadAllTextAsync(Path.GetFullPath(projectPath));
            var spec = JsonSerializer.Deserialize(json, ForgeJsonContext.Default.ProjectSpec)
                ?? throw new ArgumentException("ProjectSpec JSON is empty.");
            spec.Validate();

            var result = await new ForgeProjectGenerator().GenerateAsync(spec, outputPath, lakePath);
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
        Console.WriteLine("Contoso Forge V1");
        Console.WriteLine("Usage: databasegenerator forge generate --project project.json --output out [--lake lake]");
        Console.WriteLine("The legacy four-argument DatabaseGenerator CLI remains unchanged.");
    }
}
