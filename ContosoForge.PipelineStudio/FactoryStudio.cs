using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Planning;
using Microsoft.Win32;

namespace ContosoForge.PipelineStudio;

public partial class MainWindow
{
    private string? lastFactoryRoot;
    private string? lastFactoryState;
    private bool factoryRunning;
    private Process? reportServer;

    public void ApplyProductSettings()
    {
        var current = Session.Project.Product ?? new ProductIntent();
        Session.ApplyProduct(new ProductIntent
        {
            Version = current.Version,
            PipelineMode = PipelineModeBox.Text, MlTarget = MlTargetBox.Text, BiTarget = BiTargetBox.Text,
            DbtIntegration = DbtIntegrationBox.Text, LabelAsOf = current.LabelAsOf, MaterializationLimitMb = current.MaterializationLimitMb
        });
        Changed("Factory product settings applied. Plan to review execution support.", "product");
    }

    private void RefreshProduct()
    {
        if (MlDesignPreview is null) return;
        var settings = ArchitecturePresets.Resolve(Session.Project).Settings;
        OrchestrationDetails.Text = $"Engine: {settings.Engine} Â· Warehouse: {settings.Warehouse} Â· Orchestrator: {settings.Orchestrator} Â· Host: {settings.AirflowHost ?? settings.Orchestrator} Â· Executor: {settings.Executor ?? "preset default"}. The selected engine produces Silver; dbt produces Gold.";
        MlDesignPreview.Text = Session.Project.BusinessScenario == ScenarioCatalog.MlScenarioId
            ? JsonSerializer.Serialize(new MlExperimentDesign { RuntimeTarget = Session.Project.Product?.MlTarget ?? "local-sklearn" }, PlanningJsonContext.Default.MlExperimentDesign)
            : "ML is disabled for this business scenario. BI & Validation remains available. Select Retail Customer Satisfaction ML to derive a delivery-time experiment.";
        RunFactoryButton.IsEnabled = !factoryRunning && Session.Project.Product is not null
            && Session.Plan?.OverallImplementationStatus == "runnable"
            && Session.Pipeline.Activities.All(a => a.Implementation?.StartsWith("factory-", StringComparison.Ordinal) == true);
        RunGuidance.Text = Session.Plan is null ? "Apply settings, then Plan this revision to see runnable actions."
            : $"Current project: {Session.Plan.CurrentExecutionStatus}. Capability: {Session.Plan.OverallImplementationStatus}. Run creates a fresh output folder and records each measured stage. Reference/export targets use Compile and their explicit manual steps.";
    }

    private void ApplyProduct_Click(object sender, RoutedEventArgs e) => Guard(ApplyProductSettings);
    private void ApplyGeneration_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Session.ApplyGeneration(GenerationEditor.Text);
        Changed("Generation settings applied and validated by the deterministic C# contract.", "generation");
    });

    private async void RunFactory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireAppliedEdits("running");
            RefreshProduct();
            if (!RunFactoryButton.IsEnabled) throw new InvalidOperationException("Plan a runnable local factory pipeline before execution.");
            var dialog = new OpenFolderDialog { Title = "Choose a parent folder for a fresh generated run" };
            if (dialog.ShowDialog(this) != true) return;
            var project = ProjectSpecReader.Read(Session.ProjectJson).Studio!;
            var graph = Session.PipelineJson;
            var id = "studio-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
            lastFactoryRoot = Path.Combine(dialog.FolderName, id);
            lastFactoryState = Path.Combine(lastFactoryRoot, ".forge", "v15", id);
            factoryRunning = true;
            RunFactoryButton.IsEnabled = false;
            ResultsPreview.Text = "Generating deterministic C# sources...\n";
            ProductFlowTabs.SelectedIndex = 9;
            await new ForgeProjectGenerator().GenerateAsync(project.SourceProject, lastFactoryRoot);
            ForgeStudioCommand.Compile(project, lastFactoryRoot, graph, includePlan: true);
            await RunPython(Path.Combine(lastFactoryRoot, "pipeline", "run_local.py"), "--root", lastFactoryRoot, "--run-id", id);
            BuildEvidenceButton.IsEnabled = true;
            RefreshFactoryResults();
        }
        catch (Exception error) { ResultsPreview.Text += "\nFailed: " + error.Message; }
        finally { factoryRunning = false; RefreshProduct(); }
    }

    private async Task RunPython(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo(PythonPathBox.Text.Trim()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Python did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        ResultsPreview.Text += await stdout + "\n" + await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException("Runtime exited " + process.ExitCode + ". Inspect the measured run evidence and logs.");
    }

    private async void BuildEvidence_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (lastFactoryRoot is null || lastFactoryState is null) throw new InvalidOperationException("Run the local pipeline first.");
            BuildEvidenceButton.IsEnabled = false;
            await RunPython(Path.Combine(lastFactoryRoot, "factory", "build_evidence.py"), "--state", lastFactoryState);
            RefreshFactoryResults();
        }
        catch (Exception error) { ResultsPreview.Text += "\nEvidence build failed: " + error.Message; }
        finally { BuildEvidenceButton.IsEnabled = lastFactoryState is not null; }
    }

    private void RefreshFactoryResults()
    {
        if (lastFactoryState is null) return;
        RunLocation.Text = lastFactoryState;
        var path = Path.Combine(lastFactoryState, "run_evidence.json");
        ResultsPreview.Text = File.Exists(path) ? File.ReadAllText(path) : "Execution has not produced evidence yet.";
        var render = Path.Combine(lastFactoryState, "bi", "build_evidence.json");
        if (File.Exists(render)) ResultsPreview.Text += "\n\nEvidence render result:\n" + File.ReadAllText(render);
    }
    private void RefreshResults_Click(object sender, RoutedEventArgs e) => Guard(RefreshFactoryResults);
    private async void OpenEvidence_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        if (lastFactoryState is null) throw new InvalidOperationException("No local run has produced a report.");
        var path = Path.Combine(lastFactoryState, "bi", "evidence", "build", "index.html");
        if (!File.Exists(path)) throw new InvalidOperationException("Build Evidence first; generated source is not a rendered report.");
        if (reportServer is { HasExited: false }) reportServer.Kill();
        reportServer?.Dispose();
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo(PythonPathBox.Text.Trim()) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-m", "http.server", port.ToString(), "--bind", "127.0.0.1", "--directory", Path.GetDirectoryName(path)! }) start.ArgumentList.Add(argument);
        reportServer = Process.Start(start) ?? throw new InvalidOperationException("Local report server did not start.");
        Closed -= StopReportServer;
        Closed += StopReportServer;
        var ready = false;
        for (var attempt = 0; attempt < 30 && !reportServer.HasExited; attempt++)
        {
            using var client = new System.Net.Sockets.TcpClient();
            try { await client.ConnectAsync(System.Net.IPAddress.Loopback, port); ready = true; break; }
            catch (System.Net.Sockets.SocketException) { await Task.Delay(100); }
        }
        if (!ready) throw new InvalidOperationException("Local report server did not become ready.");
        Process.Start(new ProcessStartInfo($"http://127.0.0.1:{port}/") { UseShellExecute = true });
        }
        catch (Exception error) { ResultsPreview.Text += "\nReport preview failed: " + error.Message; }
    }

    private void StopReportServer(object? sender, EventArgs e)
    {
        if (reportServer is { HasExited: false }) reportServer.Kill();
        reportServer?.Dispose();
        reportServer = null;
    }
}
