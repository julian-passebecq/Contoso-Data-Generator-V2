using System.IO;
using System.Text.Json.Nodes;

namespace ContosoForge.PipelineStudio;

public partial class App
{
    private static async Task RunHistorySmoke(MainWindow window, string state, string output)
    {
        Directory.CreateDirectory(output);
        var root = Directory.GetParent(state)!.Parent!.Parent!.FullName;
        var protectedFiles = new[] { "run_evidence.json", "bi/report_contract.json", "bi/build_evidence.json", "bi/evidence/build/index.html" }
            .Select(p => Path.Combine(state, p)).ToDictionary(p => p, RunEvidence.Hash);
        await window.ImportRun(state);
        Require(window.ResultsPreview.Text.Contains("Historical run") && !window.BuildEvidenceButton.IsEnabled, "History was not read-only.");
        var failed = false;
        try { await window.BuildEvidenceAsync(); } catch (InvalidOperationException) { failed = true; }
        Require(failed, "Imported run executed a build.");
        window.PreviewStartedForTest = () => {
            window.Close(); Require(window.IsVisible, "Close was allowed during owned preview startup.");
            throw new InvalidOperationException("injected startup failure");
        };
        failed = false;
        try { await window.OpenEvidenceAsync(false); } catch (InvalidOperationException) { failed = true; }
        Require(failed && !window.HasOwnedPreviewForTest, "Failed startup left an owned server.");
        window.PreviewStartedForTest = null;
        var url = await window.OpenEvidenceAsync(false);
        using var http = new System.Net.Http.HttpClient();
        Require((await http.GetStringAsync(url)).Contains("Governed results"), "Reopened report was not served.");
        Render(window, 1500, 1000, Path.Combine(output, "history.png"));
        Require(window.ResultsPreview.ActualHeight > 250, "History results were clipped to the compact editor panel.");
        window.Close();
        failed = false;
        try { await http.GetStringAsync(url); } catch (System.Net.Http.HttpRequestException) { failed = true; }
        Require(failed && !window.HasOwnedPreviewForTest, "Preview survived Studio close.");
        foreach (var pair in protectedFiles) Require(pair.Value == RunEvidence.Hash(pair.Key), "Reopening changed evidence.");
        File.WriteAllText(Path.Combine(output, "history-smoke.json"), new JsonObject {
            ["status"] = "passed", ["state"] = state, ["root"] = root, ["readOnly"] = true,
            ["previewStartupCloseGuard"] = true, ["startupFailureCleanup"] = true, ["activeCloseCleanup"] = true }.ToJsonString());
    }
}
