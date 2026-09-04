using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DatabaseGenerator.Forge.Pipeline;

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
            else window.Show();
        }
        catch (Exception error)
        {
            if (options.TryGetValue("--smoke-output", out var output))
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
            if (index + 1 == args.Length || args[index] is not ("--project" or "--pipeline" or "--smoke-output"))
                throw new ArgumentException("Options: --project <project.json> --pipeline <pipeline.json> --smoke-output <empty-directory>");
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
    }
}
