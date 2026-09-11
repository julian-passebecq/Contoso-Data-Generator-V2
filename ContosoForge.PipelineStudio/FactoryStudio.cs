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
    private RunSelection? selectedRun;
    private StudioExecutionContext? activeExecution;
    private string? catalogDiagnostic;
    private string? lastFactoryRoot => selectedRun?.Root;
    private string? lastFactoryState => selectedRun?.State;
    private string EditorIdentity => RunEvidence.EditorIdentity(Session.ProjectJson, Session.PipelineJson);
    private bool CurrentSelection => selectedRun?.IsCurrent(EditorIdentity) == true;
    private bool factoryRunning;
    private bool openingReport;
    private Process? reportServer;

    public void ApplyProductSettings()
    {
        var current = Session.Project.Product ?? new ProductIntent();
        Session.ApplyProduct(new ProductIntent
        {
            Version = current.Version,
            Goal = current.Goal, StopAfter = current.StopAfter, SelectedKpis = current.SelectedKpis,
            Analysis = current.Analysis, PublishTargets = current.PublishTargets, Recipe = current.Recipe,
            PipelineMode = PipelineModeBox.Text, MlTarget = MlTargetBox.Text, BiTarget = BiTargetBox.Text,
            DbtIntegration = DbtIntegrationBox.Text, LabelAsOf = current.LabelAsOf, MaterializationLimitMb = current.MaterializationLimitMb
        });
        Changed("Factory product settings applied. Plan to review execution support.", "product");
    }

    private void RefreshProduct()
    {
        if (MlDesignPreview is null) return;
        PythonPathBox.IsEnabled = !factoryRunning && !openingReport;
        var settings = ArchitecturePresets.Resolve(Session.Project).Settings;
        OrchestrationDetails.Text = $"Engine: {settings.Engine} · Warehouse: {settings.Warehouse} · Orchestrator: {settings.Orchestrator} · Host: {settings.AirflowHost ?? settings.Orchestrator} · Executor: {settings.Executor ?? "preset default"}. The selected engine produces Silver; dbt produces Gold.";
        MlDesignPreview.Text = Session.Project.BusinessScenario == ScenarioCatalog.MlScenarioId
            ? JsonSerializer.Serialize(new MlExperimentDesign { RuntimeTarget = Session.Project.Product?.MlTarget ?? "local-sklearn" }, PlanningJsonContext.Default.MlExperimentDesign)
            : "ML is disabled for this business scenario. BI & Validation remains available. Select Retail Customer Satisfaction ML to derive a delivery-time experiment.";
        BuildEvidenceButton.IsEnabled = !factoryRunning && !openingReport && HasReportPackage();
        RunFactoryButton.IsEnabled = !factoryRunning && !openingReport && Session.Project.Product is not null
            && Session.Plan?.OverallImplementationStatus == "runnable"
            && Session.Pipeline.Activities.All(a => a.Implementation?.StartsWith("factory-", StringComparison.Ordinal) == true);
        if (!factoryRunning) RunGuidance.Text = "Python: " + PythonPathBox.Text + ". " + (Session.Plan is null ? "Apply settings, then Plan this revision to see runnable actions."
            : $"Current project: {Session.Plan.CurrentExecutionStatus}. Capability: {Session.Plan.OverallImplementationStatus}. Run creates a fresh output folder and records each measured stage. Reference/export targets use Compile and their explicit manual steps.");
    }

    private void ApplyProduct_Click(object sender, RoutedEventArgs e) => Guard(ApplyProductSettings);
    private void ApplyGeneration_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        Session.ApplyGeneration(GenerationEditor.Text);
        Changed("Generation settings applied and validated by the deterministic C# contract.", "generation");
    });

    private void ResetFactoryResults()
    {
        StopReportServer(this, EventArgs.Empty);
        selectedRun = null;
        catalogDiagnostic = null;
        BuildEvidenceButton.IsEnabled = false;
        RunLocation.Text = "";
        ResultsPreview.Text = "No execution for this project revision.";
    }

    private bool HasReportPackage() => CurrentSelection && lastFactoryState is not null &&
        File.Exists(Path.Combine(lastFactoryState, "bi", "report_contract.json"));

    private void AppendRuntimeOutput(string text)
    {
        ResultsPreview.AppendText(text);
        const int limit = 64000;
        if (ResultsPreview.Text.Length > limit) ResultsPreview.Text = ResultsPreview.Text[^limit..];
        ResultsPreview.ScrollToEnd();
    }

    private async Task ValidateRuntime(bool report)
    {
        StatusText.Text = "Validating runtime setup...";
        var requirements = RuntimeRequirements.Resolve(
            Session.Project.Product?.Version,
            ArchitecturePresets.Resolve(Session.Project).Settings.Engine,
            Session.Project.Product?.Analysis?.Kind, Session.Project.Product?.Analysis?.Runtime,
            Session.Pipeline.Activities.Select(a => a.Implementation ?? ""), report);
        try
        {
            PythonPathBox.Text = await StudioRuntime.ValidatePython(PythonPathBox.Text.Trim(), requirements.Modules, report, requirements.Distributions);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(requirements.Description + ": " + error.Message, error);
        }
        RunGuidance.Text = "Validated Python: " + PythonPathBox.Text;
    }

    private async void BrowsePython_Click(object sender, RoutedEventArgs e)
    {
        if (factoryRunning || openingReport) return;
        var dialog = new OpenFileDialog { Title = "Select Python executable", Filter = "Python executable|*.exe|All files|*.*" };
        if (dialog.ShowDialog(this) == true) PythonPathBox.Text = dialog.FileName;
        await ValidateSelectedPython();
    }

    private async void ValidatePython_Click(object sender, RoutedEventArgs e) => await ValidateSelectedPython();
    private async Task ValidateSelectedPython()
    {
        if (factoryRunning || openingReport) return;
        factoryRunning = true;
        RefreshProduct();
        string status;
        try { await ValidateRuntime(false); status = "Validated Python: " + PythonPathBox.Text; }
        catch (Exception error) { status = error.Message; }
        finally { factoryRunning = false; RefreshProduct(); }
        RunGuidance.Text = status;
    }

    private async void RunFactory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireAppliedEdits("running");
            if (factoryRunning) throw new InvalidOperationException("Execution is already active.");
            var dialog = new OpenFolderDialog { Title = "Choose a parent folder for a fresh generated run" };
            if (dialog.ShowDialog(this) == true) await RunFactoryAsync(dialog.FolderName);
        }
        catch (Exception error) { AppendRuntimeOutput("\nFailed: " + error.Message); }
    }

    // Shared by the real button and asynchronous desktop regression coverage.
    public async Task RunFactoryAsync(string parent)
    {
        RequireAppliedEdits("running");
        RefreshProduct();
        if (!RunFactoryButton.IsEnabled) throw new InvalidOperationException("Plan a runnable local factory pipeline before execution.");
        var projectJson = Session.ProjectJson;
        var project = ProjectSpecReader.Read(projectJson).Studio!;
        var graph = Session.PipelineJson;
        factoryRunning = true;
        ResetFactoryResults();
        RefreshProduct();
        try
        {
            await ValidateRuntime(false);
            var id = "studio-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
            var run = new RunSelection(Path.GetFullPath(Path.Combine(parent, id)), id, RunEvidence.EditorIdentity(projectJson, graph), project.Product?.Version);
            activeExecution = new StudioExecutionContext(run, PythonPathBox.Text.Trim(), projectJson, graph);
            selectedRun = run;
            RunLocation.Text = lastFactoryState;
            ResultsPreview.Text = "Generating deterministic C# sources...\n";
            StatusText.Text = "Generating deterministic C# sources...";
            ProductFlowTabs.SelectedIndex = project.Product?.Version == "1.7" ? 7 : 9;
            if (project.Product?.Version == "1.7") resultTabs.SelectedIndex = 1;
            await Task.Run(async () =>
            {
                await new ForgeProjectGenerator().GenerateAsync(project.SourceProject, run.Root);
                ForgeStudioCommand.Compile(project, run.Root, graph, includePlan: true);
            });
            if (project.Product?.Version == "1.7")
            {
                try { await new RunCatalog(RunCatalog.DefaultPath).Register(new CatalogEntry(lastFactoryRoot!, id, project.SourceProject.Name, DateTime.UtcNow.ToString("O"))); }
                catch (Exception error) { catalogDiagnostic = "Catalog registration failed; import this folder later: " + error.Message; AppendRuntimeOutput(catalogDiagnostic); }
            }
            if (BeforeExecutionForTest is not null) await BeforeExecutionForTest(run);
            await RunPython(activeExecution, Path.Combine(run.Root, "pipeline", "run_local.py"), "--root", run.Root, "--run-id", id);
            RefreshFactoryResults();
        }
        catch (Exception error) { StatusText.Text = "Run failed: " + error.Message; AppendRuntimeOutput("\n" + StatusText.Text); throw; }
        finally { activeExecution = null; factoryRunning = false; RefreshProduct(); }
    }

    private async Task RunPython(StudioExecutionContext context, string script, params string[] arguments)
    {
        var logPath = Path.Combine(context.Run.Root, Path.GetFileNameWithoutExtension(script) + ".studio.log");
        var elapsed = Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => StatusText.Text = RunGuidance.Text = $"{Path.GetFileName(script)} · run {context.Run.RunId} · elapsed {elapsed.Elapsed:hh\\:mm\\:ss} · Python: {context.Python} · Full log: {logPath}";
        timer.Start();
        try { await StudioRuntime.Execute(context.Python, new[] { script }.Concat(arguments), AppendRuntimeOutput, logPath); }
        finally { timer.Stop(); }
    }

    public async Task BuildEvidenceAsync()
    {
        if (factoryRunning || openingReport) throw new InvalidOperationException("Wait for the current execution before building a report.");
        if (!HasReportPackage()) throw new InvalidOperationException("This run produced no report package. Choose a report stop stage and run again.");
        factoryRunning = true;
        StopReportServer(this, EventArgs.Empty);
        RefreshProduct();
        try
        {
            await ValidateRuntime(true);
            activeExecution = new StudioExecutionContext(selectedRun!, PythonPathBox.Text.Trim(), Session.ProjectJson, Session.PipelineJson);
            await RunPython(activeExecution, Path.Combine(activeExecution.Run.Root, "factory", "build_evidence.py"), "--state", activeExecution.Run.State);
            RefreshFactoryResults();
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(lastFactoryState!, "bi", "build_evidence.json"), new System.Text.Json.Nodes.JsonObject {
                ["status"] = "failed", ["runId"] = selectedRun!.RunId, ["errorType"] = error.GetType().Name,
                ["completedAt"] = DateTime.UtcNow.ToString("O") }.ToJsonString());
            StatusText.Text = "Evidence build failed: " + error.Message; AppendRuntimeOutput("\n" + StatusText.Text); throw;
        }
        finally { activeExecution = null; factoryRunning = false; RefreshProduct(); }
    }

    private async void BuildEvidence_Click(object sender, RoutedEventArgs e)
    {
        try { await BuildEvidenceAsync(); }
        catch (Exception error) { StatusText.Text = error.Message; }
    }
    private void RefreshFactoryResults()
    {
        if (lastFactoryState is null) return;
        if (!factoryRunning && !CurrentSelection) { SelectRun(selectedRun!); return; }
        RunLocation.Text = lastFactoryState;
        var path = Path.Combine(lastFactoryState, "run_evidence.json");
        ResultsPreview.Text = (CurrentSelection ? "Current applied revision" : "Historical run — read-only; editor revision differs or run was reopened") + "\n";
        if (File.Exists(path))
        {
            using var evidence = JsonDocument.Parse(File.ReadAllText(path));
            var root = evidence.RootElement;
            StatusText.Text = $"Run {root.GetProperty("runId").GetString()}: {root.GetProperty("status").GetString()}";
            AppendRuntimeOutput($"Run: {root.GetProperty("runId").GetString()}\nOutcome: {root.GetProperty("status").GetString()}\n");
            foreach (var stage in root.GetProperty("stages").EnumerateObject())
                AppendRuntimeOutput($"{stage.Name}: {stage.Value.GetProperty("status").GetString()} · {(stage.Value.TryGetProperty("result", out var result) && result.TryGetProperty("status", out var status) ? status.GetString() : "no result")}\n");
        }
        else AppendRuntimeOutput("Execution has not produced evidence yet.");
        var render = Path.Combine(lastFactoryState, "bi", "build_evidence.json");
        if (File.Exists(render))
        {
            using var build = JsonDocument.Parse(File.ReadAllText(render));
            StatusText.Text += " · Report: " + build.RootElement.GetProperty("status").GetString();
            AppendRuntimeOutput("\n\nEvidence render result:\n" + File.ReadAllText(render));
        }
        AppendRuntimeOutput($"\nFull evidence: {path}\nFull process logs: {lastFactoryRoot}\\*.studio.log\nReport build logs: {lastFactoryState}\\bi\\*.log\n");
        if (catalogDiagnostic is not null) AppendRuntimeOutput(catalogDiagnostic + "\n");
    }
    private void RefreshResults_Click(object sender, RoutedEventArgs e) { if (!factoryRunning) Guard(RefreshFactoryResults); }
    private async void OpenEvidence_Click(object sender, RoutedEventArgs e)
    {
        try { await OpenEvidenceAsync(); }
        catch (Exception error) { AppendRuntimeOutput("\nReport preview failed: " + error.Message); }
    }

    public async Task<string> OpenEvidenceAsync(bool launchBrowser = true)
    {
        if (openingReport) throw new InvalidOperationException("Report preview is already starting.");
        openingReport = true;
        RefreshProduct();
        try
        {
        if (factoryRunning) throw new InvalidOperationException("Wait for the active execution before opening a report.");
        if (selectedRun is null) throw new InvalidOperationException("Select a local run first.");
        string path;
        try { path = RunEvidence.ValidatePreview(selectedRun); }
        catch (Exception error) { throw new InvalidOperationException("Report verification failed: " + error.Message, error); }
        StopReportServer(this, EventArgs.Empty);
        var previewPython = await StudioRuntime.ValidatePython(PythonPathBox.Text.Trim(), []);
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var start = new ProcessStartInfo(previewPython) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-m", "http.server", port.ToString(), "--bind", "127.0.0.1", "--directory", Path.GetDirectoryName(path)! }) start.ArgumentList.Add(argument);
        reportServer = Process.Start(start) ?? throw new InvalidOperationException("Local report server did not start.");
        if (PreviewStartedForTest is not null) await PreviewStartedForTest();
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
        var url = $"http://127.0.0.1:{port}/";
        if (launchBrowser) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        StatusText.Text = $"Previewing {(CurrentSelection ? "current" : "historical")} run {selectedRun.RunId} · report inputs and index hash validated";
        return url;
        }
        catch { StopReportServer(this, EventArgs.Empty); throw; }
        finally { openingReport = false; RefreshProduct(); }
    }

    private void StopReportServer(object? sender, EventArgs e)
    {
        if (reportServer is { HasExited: false }) { reportServer.Kill(entireProcessTree: true); reportServer.WaitForExit(5000); }
        reportServer?.Dispose();
        reportServer = null;
    }
    internal Func<Task>? PreviewStartedForTest { get; set; }
    internal Func<RunSelection, Task>? BeforeExecutionForTest { get; set; }
    internal bool HasOwnedPreviewForTest => reportServer is { HasExited: false };
}
