using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ContosoForge.PipelineStudio;

public partial class MainWindow
{
    private void ListRuns_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RunHistoryBox.ItemsSource = new RunCatalog(RunCatalog.DefaultPath).List();
            StatusText.Text = "Select a locator, then Inspect. Duplicate IDs at different locations remain separate entries.";
        }
        catch (Exception error) { StatusText.Text = "Catalog unavailable: " + error.Message + ". Explicit folder import still works; preserve the catalog for recovery."; }
    }
    private void InspectRun_Click(object sender, RoutedEventArgs e)
    {
        if (RunHistoryBox.SelectedItem is CatalogEntry entry) Guard(() => SelectRun(new RunSelection(entry.Root, entry.RunId)));
    }
    public void SelectRun(RunSelection run)
    {
        if (factoryRunning || openingReport) throw new InvalidOperationException("Wait for the owned operation before switching results.");
        // Validate the locator before replacing a usable selection or stopping its preview.
        var candidate = run with { Root = Path.GetFullPath(run.Root), EditorIdentity = null, OwnedVersion = null };
        var state = candidate.State;
        ResetFactoryResults();
        selectedRun = candidate;
        RunLocation.Text = state;
        ProductFlowTabs.SelectedIndex = Session.Project.Product?.Version == "1.7" ? 7 : 9;
        if (Session.Project.Product?.Version == "1.7") resultTabs.SelectedIndex = 1;
        ResultsPreview.Text = "Historical run — read-only\nRun: " + run.RunId + "\nLocation: " + run.Root + "\n";
        try
        {
            var evidence = RunEvidence.Inspect(selectedRun);
            var status = RunEvidence.Text(evidence, "status");
            AppendRuntimeOutput("Project SHA-256: " + evidence["identity"]?["projectSha256"] + "\nRecorded outcome: " +
                (status == "running" ? "last recorded running; live execution unconfirmed" : status) + "\n");
            var receipt = RunEvidence.Within(selectedRun.State, "bi/build_evidence.json");
            AppendRuntimeOutput("Report build: " + (File.Exists(receipt) ? RunEvidence.Text(RunEvidence.Read(receipt), "status") : "not recorded") + "\n");
            try
            {
                RunEvidence.ValidatePreview(selectedRun);
                AppendRuntimeOutput("Validated report inputs and index.html. Other built assets are not hash-verified; local receipts are unsigned.\n");
            }
            catch (Exception error) { AppendRuntimeOutput("Preview unverified/unavailable: " + error.Message + "\n"); }
        }
        catch (Exception error) { AppendRuntimeOutput("Unverified evidence: " + error.Message + ". Locate the original/moved root or inspect its logs; no evidence was changed.\n"); }
        AppendRuntimeOutput("Evidence/logs: " + run.State + "\nProcess logs: " + run.Root + "\n");
        RefreshProduct();
    }
    public async Task ImportRun(string folder, string? expectedId = null)
    {
        if (factoryRunning || openingReport) throw new InvalidOperationException("Wait before importing results.");
        folder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var directory = new DirectoryInfo(folder);
        var isState = directory.Parent?.Name == "v15" && directory.Parent.Parent?.Name == ".forge";
        var root = isState ? directory.Parent!.Parent!.Parent!.FullName : folder;
        var states = isState ? new[] { folder } : Directory.GetDirectories(RunEvidence.Within(root, ".forge/v15"));
        if (states.Length == 0) throw new InvalidDataException("No run states in this root. Select a generated V1.7 root or state folder.");
        foreach (var state in states)
        {
            var id = Path.GetFileName(state);
            try { id = RunEvidence.Text(RunEvidence.Read(RunEvidence.Within(root, $".forge/v15/{id}/run_evidence.json")), "runId"); }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidOperationException) { /* Partial evidence remains inspectable by its folder locator. */ }
            if (expectedId is not null && id != expectedId) continue;
            var selection = new RunSelection(root, id);
            if (!string.Equals(selection.State, Path.GetFullPath(state), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Run ID does not match its state directory.");
            // Selection exposes corrupt/partial receipts for diagnosis; preview always validates separately.
            SelectRun(selection);
            try
            {
                var catalog = new RunCatalog(RunCatalog.DefaultPath);
                await catalog.Register(new CatalogEntry(root, id, Path.GetFileName(root), DateTime.UtcNow.ToString("O")));
                var entries = catalog.List(); RunHistoryBox.ItemsSource = entries;
                RunHistoryBox.SelectedItem = entries.FirstOrDefault(e => e.RunId == id && string.Equals(e.Root, root, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception error) { AppendRuntimeOutput("Catalog registration unavailable: " + error.Message + "\n"); }
        }
        if (expectedId is not null && !states.Any(s => Path.GetFileName(s) == RunEvidence.StateDirectory(expectedId))) throw new InvalidDataException("Moved folder does not contain the selected run ID.");
    }
    private async void ImportRun_Click(object sender, RoutedEventArgs e)
    {
        if (factoryRunning || openingReport) return;
        var dialog = new OpenFolderDialog { Title = "Select a V1.7 generated root or .forge/v15/run-id folder (read-only)" };
        if (dialog.ShowDialog(this) == true)
            try { await ImportRun(dialog.FolderName); } catch (Exception error) { StatusText.Text = error.Message; }
    }
    private async void LocateRun_Click(object sender, RoutedEventArgs e)
    {
        if (factoryRunning || openingReport || RunHistoryBox.SelectedItem is not CatalogEntry entry) return;
        var dialog = new OpenFolderDialog { Title = "Locate the moved generated root for " + entry.RunId };
        if (dialog.ShowDialog(this) == true)
            try { await ImportRun(dialog.FolderName, entry.RunId); } catch (Exception error) { StatusText.Text = error.Message; }
    }
}
