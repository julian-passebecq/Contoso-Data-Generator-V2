using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DatabaseGenerator.Forge.Pipeline;
using DatabaseGenerator.Forge;
using DatabaseGenerator.Forge.Planning;
using System.Windows.Controls;
using Polyline = System.Windows.Shapes.Polyline;

namespace ContosoForge.PipelineStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = Parse(e.Args);
        var project = options.GetValueOrDefault("--project") ?? FindExample();
        var window = new MainWindow();
        MainWindow = window;
        try
        {
            if (project is not null) window.LoadProject(project);
            if (options.TryGetValue("--pipeline", out var pipeline)) window.LoadPipeline(pipeline);
            if (options.TryGetValue("--smoke-output", out var output))
            {
                RunSmoke(window, output, project);
                Shutdown(0);
            }
            else if (options.TryGetValue("--factory-smoke-output", out var factoryOutput))
            {
                RunFactorySmoke(window, factoryOutput);
                Shutdown(0);
            }
            else window.Show();
        }
        catch (Exception error)
        {
            if (options.TryGetValue("--smoke-output", out var output) || options.TryGetValue("--factory-smoke-output", out output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
            }
            else MessageBox.Show(error.Message, "Pipeline Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 == args.Length || args[index] is not ("--project" or "--pipeline" or "--smoke-output" or "--factory-smoke-output"))
                throw new ArgumentException("Options: --project <project.json> --pipeline <pipeline.json> --smoke-output <empty-directory> --factory-smoke-output <empty-directory>");
            result.Add(args[index], Path.GetFullPath(args[index + 1]));
        }
        return result;
    }

    private static string? FindExample()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "examples/free-gcp-lab.project.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>Runs the actual WPF editor actions and renders its own visual tree.
    /// The test opens no visible desktop window and starts no runtime/cloud jobs.</summary>
    private static void RunSmoke(MainWindow window, string output, string? project)
    {
        if (project is null) throw new ArgumentException("Smoke requires --project or a repository example.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("Smoke output must be an empty directory.");
        Directory.CreateDirectory(output);
        var initialSource = window.Session.Project.SourceProject.Name;
        var count = window.Session.Pipeline.Activities.Count;
        Require(count > 0, "Project did not load its neutral graph.");
        window.AddActivity("sql");
        Require(window.Session.Pipeline.Activities.Count == count + 1, "Toolbox did not add an activity.");
        window.RemoveSelected();
        Require(window.Session.Pipeline.Activities.Count == count, "Removing a toolbox activity failed.");
        var pendingActivity = window.Session.Pipeline.Activities.First(a => a.Implementation == "colab-work-order");
        window.SelectActivity(pendingActivity.Id);
        window.NameBox.Text = "Pending activity name";
        window.ModeBox.SelectedItem = "connect-local";
        foreach (var action in new Action[]
        {
            () => window.SaveTo(Path.Combine(output, "blocked-save/pipeline.json")),
            () => window.CompileTo(Path.Combine(output, "blocked-compile")),
            () => window.ValidateGraph(),
            () => window.SelectActivity(window.Session.Pipeline.Activities.First().Id)
        })
        {
            var blocked = false;
            try { action(); } catch (ArgumentException error) { blocked = error.Message.Contains("pending", StringComparison.Ordinal); }
            Require(blocked && window.NameBox.Text == "Pending activity name" && window.ModeBox.Text == "connect-local",
                "An unapplied activity edit was lost or silently excluded from an action.");
        }
        Require(!Directory.Exists(Path.Combine(output, "blocked-save")) && !Directory.Exists(Path.Combine(output, "blocked-compile")),
            "A blocked pending-edit action changed output files.");
        window.BigQueryDatasetBox.Text = "pending_dataset";
        window.ParametersEditor.Text = "{\"sample\":{\"type\":\"int\",\"default\":1,\"required\":false}}";
        window.ApplyParameters();
        Require(window.NameBox.Text == "Pending activity name" && window.BigQueryDatasetBox.Text == "pending_dataset",
            "Applying parameters discarded pending fields in another panel.");
        window.ApplyDestination();
        Require(window.NameBox.Text == "Pending activity name" && window.ModeBox.Text == "connect-local",
            "Applying destination discarded pending activity fields.");
        window.ApplyNode();
        Require(window.Session.Pipeline.Parameters.ContainsKey("sample") && window.Session.Project.Gcp.Dataset == "pending_dataset",
            "Simultaneous panel changes did not apply independently.");
        var datasetSelection = window.DatasetBox.SelectedItem;
        window.DatasetPathBox.Text = "runtime://pending-navigation";
        window.DatasetBox.SelectedItem = window.Session.Pipeline.Datasets.First(d => d.Id != datasetSelection as string).Id;
        Require(Equals(window.DatasetBox.SelectedItem, datasetSelection) && window.DatasetPathBox.Text == "runtime://pending-navigation",
            "Dataset navigation discarded unapplied dataset text.");
        window.DiscardPendingEdits();
        var collisionRoot = Path.Combine(output, "unrelated-companion");
        Directory.CreateDirectory(collisionRoot);
        var sentinel = "{\"unrelated\":true}\n";
        File.WriteAllText(Path.Combine(collisionRoot, "project.json"), sentinel);
        var collisionRejected = false;
        try { window.Session.Save(Path.Combine(collisionRoot, "new-pipeline.json")); }
        catch (ArgumentException error) { collisionRejected = error.Message.Contains("unrelated", StringComparison.Ordinal); }
        Require(collisionRejected && !File.Exists(Path.Combine(collisionRoot, "new-pipeline.json")) &&
            File.ReadAllText(Path.Combine(collisionRoot, "project.json")) == sentinel, "Save partially wrote or overwrote an unrelated companion project.");
        var validPipeline = window.Session.PipelineJson;
        foreach (var field in new[] { "activities", "datasets", "retry", "null-activity", "null-kind" })
        {
            var malformed = JsonNode.Parse(validPipeline)!;
            if (field == "retry") malformed["activities"]![0]!["retry"] = null;
            else if (field == "null-kind") malformed["activities"]![0]!["kind"] = null;
            else if (field == "null-activity") malformed["activities"]![0] = null;
            else malformed[field] = null;
            var malformedPath = Path.Combine(output, "malformed-" + field + ".json");
            File.WriteAllText(malformedPath, malformed.ToJsonString());
            var malformedRejected = false;
            try { window.LoadPipeline(malformedPath); }
            catch (ArgumentException error) { malformedRejected = error.Message.Contains("null", StringComparison.Ordinal); }
            Require(malformedRejected && window.Session.PipelineJson == validPipeline, "Malformed structural JSON replaced or crashed the existing editor graph.");
        }
        window.ProjectIdBox.Text = "example-project";
        window.BigQueryDatasetBox.Text = "studio_smoke";
        window.ApplyDestination();
        var spark = window.Session.Pipeline.Activities.First(a => a.Implementation == "colab-work-order");
        window.SelectActivity(spark.Id);
        window.NameBox.Text = "Run Forge in Colab";
        window.RuntimeBox.Text = "google-colab";
        window.ModeBox.SelectedItem = "connect-local";
        window.VersionPolicyBox.SelectedItem = "colab-native";
        window.VersionBox.Text = "4.0.4";
        window.RetryBox.Text = "2";
        window.TimeoutBox.Text = "7200";
        window.ApplyNode();
        var diagnostics = window.ValidateGraph();
        Require(diagnostics.Count == 0, "Edited Connect graph did not pass the existing compiler: " + string.Join(" ", diagnostics));
        var path = Path.Combine(output, "edited/pipeline.json");
        window.SaveTo(path);
        window.LoadProject(Path.Combine(output, "edited/project.json"));
        window.LoadPipeline(path);
        var loaded = window.Session.Pipeline.Activities.Single(a => a.Id == spark.Id);
        Require(loaded.SparkApiMode == "connect-local" && loaded.Retry.MaximumAttempts == 2 && loaded.TimeoutSeconds == 7200,
            "Saved neutral contract lost edited runtime/retry/timeout fields.");
        Require(window.Session.Project.Gcp.Dataset == "studio_smoke" && window.Session.Project.SourceProject.Name == initialSource,
            "Destination edit did not round-trip or changed the business project.");
        window.SelectActivity(spark.Id);
        // A cycle must be reported, then clearing it must restore the unchanged graph.
        var source = window.Session.Pipeline.Activities.First(a => a.Kind == "source");
        source.DependsOn.Add(spark.Id);
        Require(window.Session.Validate().Any(e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase)), "Compiler did not reject an edited cycle.");
        source.DependsOn.Remove(spark.Id);
        window.NodeParametersBox.Text = "{\"token\":\"guard-fixture\"}";
        var rejected = false;
        try { window.ApplyNode(); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Editor did not enforce the shared raw-credential guard.");
        window.DiscardPendingEdits();
        window.SelectActivity(spark.Id);
        window.PlanCurrent();
        window.CompileTo(Path.Combine(output, "compiled"));
        Require(window.AirflowPreview.Text.Contains("DAG", StringComparison.Ordinal), "Generated DAG preview is absent.");
        Require(window.IacPreview.Text.Contains("google_bigquery_dataset", StringComparison.Ordinal), "Generated BigQuery HCL preview is absent.");
        Require(window.ManifestPreview.Text.Contains("generated-reference", StringComparison.Ordinal), "Manifest preview lost its generated-only status.");
        window.PreviewTabs.SelectedIndex = 3;
        window.RootGrid.Measure(new Size(1500, 1000));
        window.RootGrid.Arrange(new Rect(0, 0, 1500, 1000));
        window.RootGrid.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1500, 1000, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window.RootGrid);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, "pipeline-studio.png"))) encoder.Save(stream);
        var report = new JsonObject
        {
            ["status"] = "passed", ["runtime"] = ".NET WPF on Windows", ["uiRendered"] = true,
            ["projectLoaded"] = Path.GetFullPath(project), ["savedPipeline"] = path,
            ["pipelineContract"] = window.Session.Pipeline.ContractVersion,
            ["actualSparkApiMode"] = "not-executed", ["editedRequestedMode"] = loaded.SparkApiMode,
            ["toolboxAddRemove"] = true, ["destinationRoundTrip"] = true, ["cycleRejected"] = true,
            ["pendingEditsProtected"] = true, ["simultaneousPanelEditsPreserved"] = true,
            ["unrelatedCompanionProtected"] = true, ["malformedContractsRejected"] = true,
            ["rawCredentialRejected"] = true, ["compiledWithExistingCompiler"] = true,
            ["airflowPreview"] = true, ["bigQueryIacPreview"] = true,
            ["cloudExecutionVerified"] = false, ["screenshot"] = "pipeline-studio.png"
        };
        File.WriteAllText(Path.Combine(output, "smoke-report.json"), report.ToJsonString(new() { WriteIndented = true }) + "\n");
        RunPlannerSmoke(project, Path.Combine(output, "planner"));
    }

    private static void RunPlannerSmoke(string project, string output)
    {
        Directory.CreateDirectory(output);
        var window = new MainWindow();
        window.LoadProject(project);
        var unplannedBlocked = false;
        try { window.CompileTo(Path.Combine(output, "unplanned-compile")); }
        catch (ArgumentException error) { unplannedBlocked = error.Message.Contains("Plan this revision", StringComparison.Ordinal); }
        Require(unplannedBlocked && !Directory.Exists(Path.Combine(output, "unplanned-compile")), "An unreviewed initial plan compiled or wrote output.");
        var classic = window.PlanCurrent();
        Require(classic.ResolvedSettings.SparkApiMode == "classic", "The existing free-gcp-lab default changed to Connect.");
        Require(window.Session.PlanJson == PlanBuilder.ToJson(PlanBuilder.Build(window.Session.Project, window.Session.PipelineJson)), "WPF diverged from the shared C# planner.");
        Require(classic.CurrentExecutionStatus == "not-executed" && classic.OverallReadiness != "reconciled", "A new plan claimed this project had executed.");
        var classicSource = JsonNode.Parse(window.Session.ProjectJson)!["sourceProject"]!.ToJsonString();
        window.PresetBox.SelectedItem = "free-gcp-connect";
        Require(window.Session.Plan is null, "A pending preset selection left a current plan.");
        var pendingBlocked = false;
        try { window.PlanCurrent(); } catch (ArgumentException error) { pendingBlocked = error.Message.Contains("pending", StringComparison.Ordinal); }
        Require(pendingBlocked, "Plan silently excluded unapplied preset settings.");
        window.ApplySelection();
        var connect = window.PlanCurrent();
        Require(connect.ResolvedSettings.SparkApiMode == "connect-local" && connect.ResolvedSettings.SparkVersion == "4.0.4", "The explicit Connect preset did not resolve Connect-local 4.0.4.");
        Require(JsonNode.Parse(window.Session.ProjectJson)!["sourceProject"]!.ToJsonString() == classicSource, "Switching architecture changed business source data.");
        Require(connect.Stages.Any(s => s.SparkApiMode == "connect-local") && connect.ManualCheckpoints.Count > 0, "The plan concealed Connect or its manual handoff.");
        Require(window.Graph.Children.OfType<Border>().Count(b => b.Tag is PlanStage) == connect.Stages.Count, "The WPF canvas did not render actual resolved stages.");
        Require(window.Graph.Children.OfType<Polyline>().Count(line => Equals(line.Tag, "plan-edge")) == connect.Edges.Count, "The WPF canvas lost resolved edges.");
        var renderedText = DescendantText(window.Graph);
        Require(renderedText.Contains("MANUAL", StringComparison.Ordinal) && renderedText.Contains("RECONCILED", StringComparison.Ordinal), "Resolved graph badges are absent.");
        var summary = DescendantText(window.ArchitectureSummary);
        Require(summary.Contains("CREDENTIALS AT EXECUTION", StringComparison.Ordinal) && summary.Contains("COSTS & QUOTAS", StringComparison.Ordinal) && summary.Contains("not-executed", StringComparison.Ordinal), "The architecture summary omitted credentials, costs or execution status.");
        window.ScenarioBox.SelectedValue = ScenarioCatalog.MlScenarioId;
        window.ApplySelection();
        var ml = window.PlanCurrent();
        Require(ml.BusinessScenario.MlEnabled && ml.GenerationProfile.Orders == 1200 && ml.GenerationProfile.TimeSpanDays == 365 && ml.ResolvedSettings.SparkApiMode == "connect-local", "ML scenario selection did not retain architecture and apply the learning profile.");
        var mlArchitecture = ml.ArchitecturePreset;
        window.ScenarioBox.SelectedValue = ScenarioCatalog.DefaultScenarioId;
        window.ApplySelection();
        Require(window.PlanCurrent().ArchitecturePreset == mlArchitecture, "Switching scenario changed the runtime architecture.");
        window.OverridesEditor.Text = "{\"tableFormat\":\"delta\"}";
        var invalidProject = window.Session.ProjectJson;
        var invalidRejected = false;
        try { window.ApplyOverrides(); } catch (ArgumentException error) { invalidRejected = error.Message.Contains("BigQuery", StringComparison.Ordinal); }
        Require(invalidRejected && window.Session.ProjectJson == invalidProject, "An invalid native BigQuery/Delta override was applied or lacked a shared diagnostic.");
        window.DiscardPendingEdits();
        foreach (var preset in new[] { "local-fast", "open-lakehouse-iceberg" })
        {
            window.PresetBox.SelectedItem = preset;
            window.ApplySelection();
            var reference = window.PlanCurrent();
            Require(reference.Stages.Any(s => s.ImplementationStatus is "reference-only" or "unsupported"), "An unimplemented architecture was falsely promoted.");
            Require(DescendantText(window.Graph).Contains("REFERENCE", StringComparison.Ordinal) || DescendantText(window.Graph).Contains("UNSUPPORTED", StringComparison.Ordinal), "Reference capability was not displayed honestly.");
            if (preset == "open-lakehouse-iceberg") Render(window, 1500, 1000, Path.Combine(output, "pipeline-studio-reference.png"));
        }
        window.PresetBox.SelectedItem = "free-gcp-connect";
        window.ApplySelection();
        window.ScenarioBox.SelectedValue = ScenarioCatalog.MlScenarioId;
        window.ApplySelection();
        window.PlanCurrent();
        window.SaveTo(Path.Combine(output, "project-bundle", "pipeline.json"));
        foreach (var sourcePath in new[] { window.Session.ProjectPath!, window.Session.PipelinePath! })
        {
            var sourceBytes = File.ReadAllBytes(sourcePath);
            var sourceProtected = false;
            try { window.Session.SavePlan(sourcePath); } catch (ArgumentException error) { sourceProtected = error.Message.Contains("source file", StringComparison.Ordinal); }
            Require(sourceProtected && sourceBytes.AsSpan().SequenceEqual(File.ReadAllBytes(sourcePath)), "Saving a plan overwrote an open source document.");
        }
        window.Session.SavePlan(Path.Combine(output, "wpf-plan.json"));
        var cliPath = Path.Combine(output, "cli-plan.json");
        var cliArguments = new[] { "plan", "--project", window.Session.ProjectPath!, "--pipeline", window.Session.PipelinePath!, "--output", cliPath };
        var result = System.Threading.Tasks.Task.Run(() => ForgeCommand.RunAsync(cliArguments)).GetAwaiter().GetResult();
        Require(result == 0 && File.ReadAllText(cliPath) == window.Session.PlanJson, "Actual CLI and WPF plan JSON differ for the same project/pipeline.");
        window.ParametersEditor.Text = "{\"new_input\":{\"type\":\"int\",\"default\":2,\"required\":false}}";
        Require(window.Session.Plan is null, "Parameter editor changes did not invalidate the plan.");
        window.ApplyParameters();
        var staleBlocked = false;
        try { window.CompileTo(Path.Combine(output, "stale-compile")); }
        catch (ArgumentException error) { staleBlocked = error.Message.Contains("Plan this revision", StringComparison.Ordinal); }
        Require(staleBlocked && !Directory.Exists(Path.Combine(output, "stale-compile")), "Compile accepted stale planning or wrote output before review.");
        window.PlanCurrent();
        window.CompileTo(Path.Combine(output, "compiled-current"));
        var compiledPlan = Path.Combine(output, "compiled-current", "plan", "resolved_plan.json");
        Require(window.Session.PlanJson == File.ReadAllText(compiledPlan), "Compile did not resolve and serialize the current edited revision.");
        Require(window.Session.Pipeline.Parameters.ContainsKey("new_input"), "Compile dropped the applied parameter revision.");
        var edited = window.Session.Pipeline.Activities.First();
        window.SelectActivity(edited.Id);
        window.NameBox.Text = "Preserve this authored activity";
        window.ApplyNode();
        window.PresetBox.SelectedItem = "free-gcp-lab";
        window.ApplySelection();
        Require(window.Session.Pipeline.Activities.First(a => a.Id == edited.Id).Name == "Preserve this authored activity", "Changing a preset discarded authored graph edits.");
        window.SelectActivity(edited.Id);
        window.NameBox.Text = "Verify source package";
        window.ApplyNode();
        window.PresetBox.SelectedItem = "free-gcp-connect";
        window.ApplySelection();
        window.PlanCurrent();
        window.PreviewTabs.SelectedItem = window.PlanPreviewTab;
        Render(window, 1500, 1000, Path.Combine(output, "pipeline-studio-plan.png"));
        Render(window, 1150, 900, Path.Combine(output, "pipeline-studio-plan-minimum.png"));
        var buttonBounds = window.PlanButton.TransformToAncestor(window.RootGrid).TransformBounds(new Rect(new Point(), window.PlanButton.RenderSize));
        Require(buttonBounds.Right <= 1150 && buttonBounds.Bottom < 300, "The Plan action overflowed the minimum window layout.");
        var report = new JsonObject
        {
            ["status"] = "passed", ["runtime"] = ".NET WPF on Windows", ["uiRendered"] = true,
            ["coreAndCliPlanIdentical"] = true, ["explicitConnectPreset"] = true, ["classicDefaultPreserved"] = true,
            ["scenarioArchitectureIndependent"] = true, ["resolvedStagesAndEdgesRendered"] = true, ["referenceAndManualBadgesVisible"] = true,
            ["pendingPlanBlocked"] = true, ["editsInvalidatePlan"] = true, ["compileUsesCurrentPlan"] = true,
            ["unplannedAndStaleCompileBlocked"] = true, ["planSaveProtectsSources"] = true,
            ["invalidOverrideRejectedAtomically"] = true, ["authoredGraphPreserved"] = true, ["minimumLayoutFits"] = true,
            ["cloudExecutionVerified"] = false, ["screenshots"] = new JsonArray("pipeline-studio-plan.png", "pipeline-studio-plan-minimum.png", "pipeline-studio-reference.png")
        };
        File.WriteAllText(Path.Combine(output, "planner-smoke-report.json"), report.ToJsonString(new() { WriteIndented = true }) + "\n");
    }

    private static string DescendantText(DependencyObject parent)
    {
        var values = new List<string>();
        if (parent is TextBlock text) values.Add(text.Text);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) values.Add(DescendantText(VisualTreeHelper.GetChild(parent, index)));
        return string.Join("\n", values);
    }

    private static void RunFactorySmoke(MainWindow window, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("Smoke output must be an empty directory.");
        Directory.CreateDirectory(output);
        Require(window.Session.Project.Product is not null, "Load a V1.5 example for the factory smoke.");
        var steps = window.ProductFlowTabs.Items.Cast<TabItem>().Select(t => t.Header.ToString()).ToArray();
        Require(steps.SequenceEqual(new ProductDesign().Steps), "The ten business-first steps are not in product order.");
        Require(window.ProductFlowTabs.SelectedIndex == 0, "The Studio did not start at Business.");
        Require(!window.RunFactoryButton.IsEnabled, "An unplanned project enabled execution.");
        var plan = window.PlanCurrent();
        Require(plan.CurrentExecutionStatus == "not-executed" && window.RunFactoryButton.IsEnabled, "An executable local plan was misrepresented.");
        window.MlTargetBox.SelectedItem = "kaggle-sklearn";
        Require(!window.RunFactoryButton.IsEnabled && window.Session.Plan is null, "A pending product edit retained a runnable plan.");
        window.ApplyProductSettings();
        Require(window.Session.Pipeline.Activities.Any(a => a.Implementation == "factory-export-ml"), "The ML target did not update the untouched default graph.");
        window.MlTargetBox.SelectedItem = "local-sklearn";
        window.ApplyProductSettings();
        window.PlanCurrent();
        Require(window.MlDesignPreview.Text.Contains("14", StringComparison.Ordinal) && window.MlDesignPreview.Text.Contains("leakage", StringComparison.OrdinalIgnoreCase), "The derived ML legality contract is absent.");
        window.GenerationEditor.Text += " ";
        Require(!window.RunFactoryButton.IsEnabled, "Pending data changes retained execution.");
        window.DiscardPendingEdits();
        window.PlanCurrent();
        window.SaveTo(Path.Combine(output, "bundle/pipeline.json"));
        window.LoadProject(Path.Combine(output, "bundle/project.json"));
        window.LoadPipeline(Path.Combine(output, "bundle/pipeline.json"));
        window.PlanCurrent();
        window.CompileTo(Path.Combine(output, "compiled"));
        Require(window.Session.PlanJson == PlanBuilder.ToJson(PlanBuilder.Build(window.Session.Project, window.Session.PipelineJson)), "Factory WPF/core plans differ.");
        Require(File.Exists(Path.Combine(output, "compiled/factory/run.py")), "Factory runtime was not compiled.");
        for (var index = 0; index < steps.Length; index++)
        {
            window.ProductFlowTabs.SelectedIndex = index;
            Render(window, 1500, 1000, Path.Combine(output, $"step-{index + 1:00}.png"));
        }
        Render(window, 1150, 900, Path.Combine(output, "minimum.png"));
        var bounds = window.ProductFlowTabs.TransformToAncestor(window.RootGrid).TransformBounds(new Rect(new Point(), window.ProductFlowTabs.RenderSize));
        Require(bounds.Right <= 1150, "Product steps exceed the minimum window width.");
        File.WriteAllText(Path.Combine(output, "factory-smoke-report.json"), new JsonObject
        {
            ["status"] = "passed", ["uiRendered"] = true, ["tenStepsInOrder"] = true,
            ["businessFirst"] = true, ["pendingDataAndProductEditsBlockRun"] = true,
            ["targetChangesDefaultGraph"] = true, ["saveLoadCompile"] = true,
            ["corePlanIdentical"] = true, ["actualPipelineExecution"] = "tested separately through generated neutral runner"
        }.ToJsonString(new() { WriteIndented = true }) + "\n");
    }

    private static void Render(MainWindow window, int width, int height, string path)
    {
        window.RootGrid.Measure(new Size(width, height));
        window.RootGrid.Arrange(new Rect(0, 0, width, height));
        window.RootGrid.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window.RootGrid);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
