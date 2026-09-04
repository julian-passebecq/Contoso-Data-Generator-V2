#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DatabaseGenerator.Forge.Runtime;

/// <summary>Imports a returned execution result through the same verifier used by the notebook and Airflow.</summary>
public static class ForgeEvidenceCommand
{
    public static async Task<int> ImportAsync(string rootPath, string workOrderPath, string resultPath,
        string outputPath, string python = "python")
    {
        var root = Path.GetFullPath(rootPath);
        var script = Path.Combine(root, "colab", "work_order.py");
        foreach (var path in new[] { script, Path.Combine(root, "truth_manifest.json"), Path.GetFullPath(workOrderPath), Path.GetFullPath(resultPath) })
            if (!File.Exists(path)) throw new ArgumentException($"Required evidence input is missing: {path}");
        var output = Path.GetFullPath(outputPath);
        if (output.Equals(Path.GetFullPath(resultPath), StringComparison.OrdinalIgnoreCase) ||
            output.Equals(Path.GetFullPath(workOrderPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The imported evidence report must have its own output path.");
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root
        };
        foreach (var argument in new[] { script, "import-evidence", "--root", root,
                     "--work-order", Path.GetFullPath(workOrderPath), "--result", Path.GetFullPath(resultPath), "--output", output })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Python evidence verifier.");
        await process.WaitForExitAsync();
        if (process.ExitCode == 0 && !File.Exists(output))
            throw new InvalidOperationException("The evidence verifier did not write its requested report.");
        return process.ExitCode;
    }
}
