using System.Diagnostics;
using System.IO;
using System.Text;

namespace ContosoForge.PipelineStudio;

/// <summary>Owns child execution and drains both pipes concurrently. Preferences are local, never project data.</summary>
public static class StudioRuntime
{
    private static string PreferencePath => Path.Combine(RunCatalog.UserDataDirectory, "python.txt");

    public static string SuggestedPython()
    {
        if (File.Exists(PreferencePath)) return File.ReadAllText(PreferencePath).Trim();
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".tools", "v15", "Scripts", "python.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return "python";
    }

    public static async Task<string> ValidatePython(string executable, IEnumerable<string> modules, bool report = false, IEnumerable<string>? distributions = null)
    {
        var output = new StringBuilder();
        var distributionList = new System.Text.Json.Nodes.JsonArray((distributions ?? []).Select(d => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(d)).ToArray()).ToJsonString();
        var code = "import sys,importlib,shutil; print(sys.executable, flush=True); " +
            "[importlib.import_module(m) for m in sys.argv[1:]]" +
            "; from importlib import metadata; [metadata.version(d) for d in " + distributionList + "]" +
            (report ? "; assert shutil.which('node') and (shutil.which('npm.cmd') or shutil.which('npm')), 'Install Node.js/npm to build Evidence'" : "");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await Execute(executable, new[] { "-c", code }.Concat(modules), chunk => output.Append(chunk), null, timeout.Token);
        }
        catch (Exception error)
        {
            var detail = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
            throw new InvalidOperationException($"Runtime setup failed for '{executable}'. Select a working Python and install DatabaseGenerator/Forge/Templates/v15/requirements.txt (or generated factory/requirements.txt) into that environment. {(report ? "Report builds also require Node.js/npm. " : "")}{detail ?? error.Message}", error);
        }
        var actual = output.ToString().Split('\n')[0].Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
        File.WriteAllText(PreferencePath, actual);
        return actual;
    }

    public static async Task Execute(string executable, IEnumerable<string> arguments, Action<string> output,
        string? logPath, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["PYTHONUNBUFFERED"] = "1";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var log = logPath is null ? null : new StreamWriter(logPath, append: true) { AutoFlush = true };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Runtime did not start.");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var gate = new object();
        async Task Drain(StreamReader reader)
        {
            var buffer = new char[2048];
            int count;
            while ((count = await reader.ReadAsync(buffer)) != 0)
            {
                var chunk = new string(buffer, 0, count);
                lock (gate) { log?.Write(chunk); output(chunk); }
            }
        }
        await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync());
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Runtime exited {process.ExitCode}. Inspect the full log and measured evidence.");
    }
}
