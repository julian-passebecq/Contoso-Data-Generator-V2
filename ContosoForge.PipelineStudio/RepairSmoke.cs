using System.IO;
using System.Text.Json.Nodes;
using System.Windows;

namespace ContosoForge.PipelineStudio;

public partial class App
{
    private static async Task RunRepairSmoke(MainWindow window, string state, string output)
    {
        Directory.CreateDirectory(output);
        await window.RunRepairChecks(state, Path.GetFullPath(output));
        window.Close();
        File.WriteAllText(Path.Combine(output, "repair-smoke.json"), new JsonObject {
            ["status"] = "passed", ["invalidLocatorHandler"] = true, ["previousPreviewPreserved"] = true,
            ["corruptCatalogRecovery"] = true, ["realMissingPySpark"] = true,
            ["colabAndExcludedAnalysis"] = true, ["pendingEditsPreserved"] = true }.ToJsonString());
    }
}

public partial class MainWindow
{
    internal async Task RunRepairChecks(string state, string output)
    {
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        await ImportRun(state);
        var previous = selectedRun!;
        var location = RunLocation.Text;
        var evidenceHash = RunEvidence.Hash(Path.Combine(state, "run_evidence.json"));
        var url = await OpenEvidenceAsync(false);
        using var http = new System.Net.Http.HttpClient();
        var link = Path.Combine(output, "linked-root");
        await StudioRuntime.Execute("cmd.exe", ["/c", "mklink", "/J", link, previous.Root], _ => { }, Path.Combine(output, "junction.log"));
        try
        {
            foreach (var bad in new[] {
                new CatalogEntry(previous.Root, " ", "whitespace", "now"),
                new CatalogEntry(previous.Root, new string('x', 251), "overlength", "now"),
                new CatalogEntry("bad\0root", previous.RunId, "invalid path", "now"),
                new CatalogEntry(link, previous.RunId, "linked path", "now") })
            {
                File.WriteAllText(RunCatalog.DefaultPath, new JsonObject { ["version"] = 1,
                    ["entries"] = new JsonArray(new JsonObject { ["root"] = bad.Root, ["runId"] = bad.RunId, ["label"] = bad.Label, ["recordedAt"] = bad.RecordedAt }) }.ToJsonString());
                var catalogHash = RunEvidence.Hash(RunCatalog.DefaultPath);
                ListRuns_Click(this, new RoutedEventArgs());
                RunHistoryBox.SelectedIndex = 0;
                InspectRun_Click(this, new RoutedEventArgs()); // The actual click handler must contain the exception.
                Check(StatusText.Text.StartsWith("Action failed:"), "Missing visible locator diagnostic");
                Check(selectedRun == previous && RunLocation.Text == location && HasOwnedPreviewForTest, "Invalid locator replaced selection or stopped preview");
                Check((await http.GetStringAsync(url)).Contains("Governed results"), "Prior preview became unusable");
                Check(catalogHash == RunEvidence.Hash(RunCatalog.DefaultPath), "Malformed catalog was rewritten");
            }
        }
        finally { Directory.Delete(link); } // Delete only the junction itself, never its target.
        File.WriteAllText(RunCatalog.DefaultPath, "{broken json");
        var corruptHash = RunEvidence.Hash(RunCatalog.DefaultPath);
        ListRuns_Click(this, new RoutedEventArgs());
        Check(StatusText.Text.StartsWith("Catalog unavailable:"), "Corrupt JSON escaped catalog recovery");
        await ImportRun(state);
        Check(selectedRun?.RunId == previous.RunId && corruptHash == RunEvidence.Hash(RunCatalog.DefaultPath), "Explicit import did not recover without rewriting corrupt catalog");
        // Restore this smoke's own catalog, then exercise the real refresh and inspect handlers.
        File.Delete(RunCatalog.DefaultPath);
        await ImportRun(state);
        ListRuns_Click(this, new RoutedEventArgs()); RunHistoryBox.SelectedIndex = 0;
        InspectRun_Click(this, new RoutedEventArgs());
        RefreshResults_Click(this, new RoutedEventArgs());
        Check(selectedRun?.RunId == previous.RunId, "Good selection/refresh failed after invalid entries");
        Check(evidenceHash == RunEvidence.Hash(Path.Combine(state, "run_evidence.json")), "Evidence changed during recovery");

        var python = PythonPathBox.Text;
        var missing = false;
        try { await StudioRuntime.ValidatePython(python, ["pyspark"]); }
        catch (InvalidOperationException) { missing = true; }
        Check(missing, "Repair negative test requires a real interpreter without PySpark");
        var project = JsonNode.Parse(File.ReadAllText("examples/v17-spark-ml.project.json"))!;
        project["product"]!["analysis"]!["runtime"] = "local";
        var projectPath = Path.Combine(output, "local-spark.project.json");
        File.WriteAllText(projectPath, project.ToJsonString());
        LoadProject(projectPath); PlanCurrent();
        var beforeProject = Session.ProjectJson; var beforePipeline = Session.PipelineJson;
        var beforeCatalog = RunEvidence.Hash(RunCatalog.DefaultPath);
        var parent = Path.Combine(output, "must-not-generate");
        missing = false;
        try { await RunFactoryAsync(parent); }
        catch (InvalidOperationException error) { missing = error.Message.Contains("local Spark ML") && error.Message.Contains("pyspark"); }
        Check(missing && !Directory.Exists(parent), "Local Spark ML did not fail before generation");
        Check(beforeProject == Session.ProjectJson && beforePipeline == Session.PipelineJson && beforeCatalog == RunEvidence.Hash(RunCatalog.DefaultPath), "Preflight mutated project, pipeline or catalog");
        GenerationEditor.Text += " ";
        var draft = GenerationEditor.Text;
        missing = false;
        try { await ValidateRuntime(false); } catch (InvalidOperationException) { missing = true; }
        Check(missing && GenerationEditor.Text == draft && beforeProject == Session.ProjectJson && beforePipeline == Session.PipelineJson, "Preflight discarded pending edits");
        DiscardPendingEdits();
        project["product"]!["analysis"]!["runtime"] = "colab";
        File.WriteAllText(projectPath, project.ToJsonString()); LoadProject(projectPath); PlanCurrent(); await ValidateRuntime(false);
        project["product"]!["analysis"]!["runtime"] = "local";
        project["product"]!["stopAfter"] = "bronze";
        File.WriteAllText(projectPath, project.ToJsonString()); LoadProject(projectPath); PlanCurrent(); await ValidateRuntime(false);
        await RunFactoryAsync(Path.Combine(output, "recovered-run"));
        Check(selectedRun is not null && ResultsPreview.Text.Contains("succeeded", StringComparison.OrdinalIgnoreCase), "New run failed after history/preflight recovery");
    }
}
