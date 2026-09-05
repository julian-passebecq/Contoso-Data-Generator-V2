using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Pipeline;
using DatabaseGenerator.Forge.Planning;
using Microsoft.Win32;

namespace ContosoForge.PipelineStudio;

public partial class MainWindow : Window
{
    public StudioSession Session { get; } = new();
    private string? selectedId;
    private bool refreshing;
    private string? datasetSelectionId;
    private readonly Dictionary<string, string> baselines = new(StringComparer.Ordinal);
    private static readonly string[] Panels = ["activity", "destination", "parameters", "preset/profile", "overrides"];
    private static readonly string[] Kinds = ["source", "copy", "spark", "sql", "dbt", "validate", "ml", "sink", "manual-checkpoint", "handoff", "transform", "extract", "notebook", "load"];
    private static string? Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static List<string> Ids(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();

    public MainWindow()
    {
        InitializeComponent();
        PresetBox.ItemsSource = ArchitecturePresets.List().Select(p => p.PresetId);
        ScenarioBox.ItemsSource = ScenarioCatalog.List();
        CostBox.ItemsSource = new[] { "gcp-sandbox-no-card", "gcp-free-tier-billing-enabled", "local", "external" };
        KindBox.ItemsSource = Kinds;
        EngineBox.ItemsSource = new[] { "", "spark", "duckdb", "polars", "pandas" };
        RuntimeBox.ItemsSource = new[] { "", "google-colab", "google-colab-connect-local", "google-colab-connect-remote", "docker", "local-process", "databricks-spark", "fabric-spark", "kubernetes" };
        ModeBox.ItemsSource = new[] { "", "classic", "connect-local", "connect-remote" };
        VersionPolicyBox.ItemsSource = new[] { "", "colab-native", "pinned" };
        WarehouseBox.ItemsSource = new[] { "none", "bigquery", "biglake", "duckdb", "sqlserver", "fabric", "databricks", "motherduck" };
        foreach (var kind in Kinds.Take(9))
        {
            var label = new TextBlock { Text = "+  " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(kind.Replace('-', ' ')), TextWrapping = TextWrapping.Wrap };
            var button = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 9), Padding = new Thickness(8, 9, 8, 9) };
            button.Click += (_, _) => Guard(() => AddActivity(kind));
            Toolbox.Children.Add(button);
        }
        PresetBox.SelectionChanged += (_, _) =>
        {
            if (!refreshing && PresetBox.SelectedItem is string id)
                CostBox.SelectedItem = ArchitecturePresets.Get(id).Defaults.CostProfile;
        };
        foreach (var control in Panels.SelectMany(PanelControls))
        {
            if (control is TextBox text) text.TextChanged += (_, _) => EditorChanged();
            else if (control is ComboBox combo)
            {
                combo.SelectionChanged += (_, _) => EditorChanged();
                combo.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => EditorChanged()));
            }
        }
    }

    public void LoadProject(string path)
    {
        RequireAppliedEdits("opening a project");
        Session.LoadProject(path);
        selectedId = Session.Pipeline.Activities.FirstOrDefault()?.Id;
        RefreshAll();
        StatusText.Text = "Loaded " + System.IO.Path.GetFullPath(path);
    }

    public void LoadPipeline(string path)
    {
        RequireAppliedEdits("opening a pipeline");
        Session.LoadPipeline(path);
        selectedId = Session.Pipeline.Activities.FirstOrDefault()?.Id;
        RefreshAll();
        StatusText.Text = "Loaded neutral pipeline · " + System.IO.Path.GetFullPath(path);
    }

    public void SelectActivity(string id)
    {
        RequireAppliedEdits("selecting an activity");
        selectedId = id;
        RefreshNode();
        AuthoringView.IsChecked = true;
        InspectorTabs.SelectedItem = ActivityTab;
        RenderGraph();
    }

    public void AddActivity(string kind)
    {
        RequireAppliedEdits("adding an activity");
        var suffix = 1;
        var id = kind.Replace('-', '_') + "_" + suffix;
        while (Session.Pipeline.Activities.Any(a => a.Id == id)) id = kind.Replace('-', '_') + "_" + ++suffix;
        var node = new PipelineActivity { Id = id, Kind = kind, Name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(kind.Replace('-', ' ')) };
        if (selectedId is not null) node.DependsOn.Add(selectedId);
        Session.Pipeline.Activities.Add(node);
        selectedId = id;
        AuthoringView.IsChecked = true;
        InspectorTabs.SelectedItem = ActivityTab;
        Changed("Added " + id + ". Select an implementation; the compiler reports execution support.");
    }

    public void RemoveSelected()
    {
        RequireAppliedEdits("removing an activity");
        if (selectedId is null) return;
        Session.Pipeline.Activities.RemoveAll(a => a.Id == selectedId);
        foreach (var activity in Session.Pipeline.Activities) activity.DependsOn.RemoveAll(id => id == selectedId);
        Session.Pipeline.Edges.RemoveAll(e => e.From == selectedId || e.To == selectedId);
        selectedId = Session.Pipeline.Activities.FirstOrDefault()?.Id;
        Changed("Removed activity and its dependency edges.");
    }

    public void ApplyNode()
    {
        if (selectedId is null) throw new ArgumentException("Select an activity first.");
        var draft = PipelineDocument.Read(Session.PipelineJson);
        var node = draft.Activities.Single(a => a.Id == selectedId);
        node.Name = Optional(NameBox.Text);
        node.Kind = KindBox.Text;
        node.Implementation = Optional(ImplementationBox.Text);
        node.Source = Optional(SourceBox.Text);
        node.Sink = Optional(SinkBox.Text);
        node.Engine = Optional(EngineBox.Text);
        node.Runtime = Optional(RuntimeBox.Text);
        node.SparkApiMode = Optional(ModeBox.Text);
        node.SparkVersionPolicy = Optional(VersionPolicyBox.Text);
        node.SparkVersion = Optional(VersionBox.Text);
        node.SparkRemote = Optional(RemoteBox.Text);
        node.Table = Optional(TableBox.Text);
        node.ConnectionRef = Optional(ConnectionBox.Text);
        node.Inputs = Ids(InputsBox.Text);
        node.Outputs = Ids(OutputsBox.Text);
        node.DependsOn = Ids(DependsBox.Text);
        // Editing the displayed dependencies replaces both supported edge forms
        // for this child. Unrelated explicit edges are preserved exactly.
        draft.Edges.RemoveAll(edge => edge.To == selectedId);
        node.Retry.MaximumAttempts = Number(RetryBox.Text, "Maximum attempts");
        node.TimeoutSeconds = Number(TimeoutBox.Text, "Timeout");
        using var parameters = JsonDocument.Parse(NodeParametersBox.Text);
        if (parameters.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Activity parameters must be a JSON object.");
        node.Parameters = parameters.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        Session.Pipeline = PipelineDocument.Read(PipelineDocument.Write(draft));
        Changed("Activity changes applied. Validate before compiling.", "activity");
    }

    public void ApplyDestination()
    {
        var draft = JsonSerializer.Deserialize(Session.ProjectJson, ArchitectureJsonContext.Default.StudioProjectSpec)!;
        draft.Architecture.Overrides.Warehouse = WarehouseBox.Text;
        draft.Gcp.ProjectId = ProjectIdBox.Text.Trim();
        draft.Gcp.Dataset = BigQueryDatasetBox.Text.Trim();
        draft.Gcp.Location = LocationBox.Text.Trim();
        if (!long.TryParse(MaxBytesBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) || bytes <= 0)
            throw new ArgumentException("Maximum bytes billed must be a positive integer.");
        draft.Gcp.MaximumBytesBilled = bytes;
        _ = ArchitecturePresets.Resolve(draft);
        var pipeline = PipelineDocument.Read(Session.PipelineJson);
        if (DatasetBox.SelectedItem is string id)
        {
            var dataset = pipeline.Datasets.Single(d => d.Id == id);
            dataset.Path = Optional(DatasetPathBox.Text);
            dataset.Table = Optional(DatasetTableBox.Text);
            dataset.ConnectionRef = Optional(DatasetConnectionBox.Text);
        }
        Session.Pipeline = PipelineDocument.Read(PipelineDocument.Write(pipeline));
        Session.Project.Architecture.Overrides.Warehouse = draft.Architecture.Overrides.Warehouse;
        Session.Project.Gcp = draft.Gcp;
        Changed("Destination and dataset binding applied. Source-generation settings were preserved.", "destination");
    }

    public void ApplyParameters()
    {
        var parameters = JsonNode.Parse(ParametersEditor.Text);
        if (parameters is not JsonObject) throw new ArgumentException("Pipeline parameters must be a JSON object.");
        var node = JsonNode.Parse(Session.PipelineJson)!;
        node["parameters"] = parameters;
        Session.Pipeline = PipelineDocument.Read(node.ToJsonString());
        Changed("Pipeline parameter definitions applied.", "parameters");
    }

    public IReadOnlyList<string> ValidateGraph()
    {
        RequireAppliedEdits("validating");
        var errors = Session.Validate();
        ValidationPreview.Text = errors.Count == 0
            ? "VALID CONTRACT\n\nThe existing PipelineCompiler accepted the graph and resolved architecture.\n\nCompilation previews execution support and manual boundaries. Runtime gates require imported execution evidence."
            : string.Join("\n\n", errors.Select((error, i) => (i + 1) + ". " + error));
        StatusText.Text = errors.Count == 0 ? "Valid neutral contract and resolved architecture." : errors.Count + " validation issue(s). See Validation.";
        PreviewTabs.SelectedIndex = 5;
        return errors;
    }

    public void CompileTo(string output)
    {
        RequireAppliedEdits("compiling");
        Session.Compile(output);
        RefreshPreviews();
        PlanView.IsChecked = true;
        RenderGraph();
        RenderArchitecture();
        ValidationPreview.Text = "COMPILATION COMPLETE\n\n" + Session.Preview("local_plan.json");
        StatusText.Text = "Compiled to " + Session.CompilationRoot + " · Cloud execution remains pending.";
    }

    public ResolvedPlan PlanCurrent()
    {
        RequireAppliedEdits("planning");
        var plan = Session.BuildPlan();
        PlanView.IsChecked = true;
        InspectorTabs.SelectedItem = ArchitectureTab;
        PlanPreview.Text = Session.PlanJson;
        RenderGraph();
        RenderArchitecture();
        PlanState.Text = "Plan current · execution not started";
        StatusText.Text = "Plan resolved offline · " + plan.ArchitecturePreset + " · " + plan.Stages.Count + " stages · reference evidence is shown separately from this project.";
        return plan;
    }

    public void ApplySelection()
    {
        Session.ApplyArchitecture(PresetBox.Text, CostBox.Text, ScenarioBox.SelectedValue as string);
        selectedId = Session.Pipeline.Activities.Any(a => a.Id == selectedId) ? selectedId : Session.Pipeline.Activities.FirstOrDefault()?.Id;
        Changed("Scenario and architecture applied. An untouched default graph follows the preset; authored graph edits are preserved.", "preset/profile");
    }

    public void ApplyOverrides()
    {
        Session.ApplyOverrides(OverridesEditor.Text);
        selectedId = Session.Pipeline.Activities.Any(a => a.Id == selectedId) ? selectedId : Session.Pipeline.Activities.FirstOrDefault()?.Id;
        Changed("Architecture overrides applied through the shared resolver.", "overrides");
    }

    public void SaveTo(string path)
    {
        RequireAppliedEdits("saving");
        Session.Save(path);
        StatusText.Text = "Saved pipeline.json and project.json bundle · " + System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
    }

    private void Changed(string message, string? appliedPanel = null)
    {
        var preserved = PendingPanels().Where(panel => panel != appliedPanel)
            .ToDictionary(panel => panel, panel => (Baseline: baselines[panel], Values: PanelControls(panel).Select(ReadControl).ToArray()));
        Session.InvalidateCompilation();
        RefreshAll();
        refreshing = true;
        try
        {
            foreach (var (panel, snapshot) in preserved)
            {
                var controls = PanelControls(panel);
                for (var index = 0; index < controls.Length; index++) WriteControl(controls[index], snapshot.Values[index]);
                baselines[panel] = snapshot.Baseline;
            }
        }
        finally { refreshing = false; }
        RenderArchitecture();
        ValidationPreview.Text = "This revision has changed. Plan and compile again.";
        StatusText.Text = message + (preserved.Count == 0 ? "" : " Pending " + string.Join(", ", preserved.Keys) + " edits were preserved.");
    }

    private void RefreshAll()
    {
        refreshing = true;
        var resolved = ArchitecturePresets.Resolve(Session.Project);
        ScenarioBox.SelectedValue = Session.Project.BusinessScenario ?? "retail.customer_satisfaction";
        PresetBox.SelectedItem = Session.Project.Architecture.PresetId;
        CostBox.SelectedItem = resolved.Settings.CostProfile;
        WarehouseBox.SelectedItem = resolved.Settings.Warehouse;
        ProjectIdBox.Text = Session.Project.Gcp.ProjectId;
        BigQueryDatasetBox.Text = Session.Project.Gcp.Dataset;
        LocationBox.Text = Session.Project.Gcp.Location;
        MaxBytesBox.Text = Session.Project.Gcp.MaximumBytesBilled.ToString(CultureInfo.InvariantCulture);
        OverridesEditor.Text = JsonNode.Parse(Session.ProjectJson)!["architecture"]!["overrides"]!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var datasetId = DatasetBox.SelectedItem as string;
        DatasetBox.ItemsSource = Session.Pipeline.Datasets.Select(d => d.Id).ToList();
        DatasetBox.SelectedItem = Session.Pipeline.Datasets.Any(d => d.Id == datasetId) ? datasetId : Session.Pipeline.Datasets.FirstOrDefault()?.Id;
        RefreshDataset();
        RefreshNode();
        RefreshPreviews();
        RenderGraph();
        foreach (var panel in Panels) baselines[panel] = Signature(panel);
        RenderArchitecture();
        refreshing = false;
    }

    private void RefreshPreviews()
    {
        var previous = refreshing;
        refreshing = true;
        PipelinePreview.Text = Session.PipelineJson;
        ParametersEditor.Text = JsonNode.Parse(Session.PipelineJson)!["parameters"]!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        ResolvedPreview.Text = Session.ResolvedJson;
        AirflowPreview.Text = Session.Preview("airflow/dags/contoso_forge_pipeline.py");
        IacPreview.Text = Session.Preview("infra/gcp/main.tf");
        ManifestPreview.Text = Session.Preview("run_manifest.json");
        PlanPreview.Text = Session.PlanJson ?? "Plan this revision to inspect the shared ResolvedPlan contract.";
        baselines["parameters"] = Signature("parameters");
        refreshing = previous;
    }

    private void RefreshNode()
    {
        var previous = refreshing;
        refreshing = true;
        var node = Session.Pipeline.Activities.FirstOrDefault(a => a.Id == selectedId);
        NodeId.Text = node?.Id ?? "Select an activity";
        NameBox.Text = node?.Name ?? "";
        KindBox.SelectedItem = node?.Kind;
        ImplementationBox.Text = node?.Implementation ?? "";
        SourceBox.Text = node?.Source ?? "";
        SinkBox.Text = node?.Sink ?? "";
        EngineBox.Text = node?.Engine ?? "";
        RuntimeBox.Text = node?.Runtime ?? "";
        ModeBox.SelectedItem = node?.SparkApiMode ?? "";
        VersionPolicyBox.SelectedItem = node?.SparkVersionPolicy ?? "";
        VersionBox.Text = node?.SparkVersion ?? "";
        RemoteBox.Text = node?.SparkRemote ?? "";
        TableBox.Text = node?.Table ?? "";
        ConnectionBox.Text = node?.ConnectionRef ?? "";
        InputsBox.Text = string.Join(", ", node?.Inputs ?? []);
        OutputsBox.Text = string.Join(", ", node?.Outputs ?? []);
        DependsBox.Text = node is null ? "" : string.Join(", ", node.DependsOn.Concat(Session.Pipeline.Edges.Where(e => e.To == node.Id).Select(e => e.From)).Distinct());
        RetryBox.Text = (node?.Retry.MaximumAttempts ?? 1).ToString(CultureInfo.InvariantCulture);
        TimeoutBox.Text = (node?.TimeoutSeconds ?? 3600).ToString(CultureInfo.InvariantCulture);
        var parameters = new JsonObject();
        if (node is not null) foreach (var (key, value) in node.Parameters) parameters[key] = JsonNode.Parse(value.GetRawText());
        NodeParametersBox.Text = parameters.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        baselines["activity"] = Signature("activity");
        refreshing = previous;
    }

    private void RefreshDataset()
    {
        var previous = refreshing;
        refreshing = true;
        var dataset = Session.Pipeline.Datasets.FirstOrDefault(d => d.Id == DatasetBox.SelectedItem as string);
        DatasetPathBox.Text = dataset?.Path ?? "";
        DatasetTableBox.Text = dataset?.Table ?? "";
        DatasetConnectionBox.Text = dataset?.ConnectionRef ?? "";
        datasetSelectionId = DatasetBox.SelectedItem as string;
        baselines["destination"] = Signature("destination");
        refreshing = previous;
    }

    private void RenderGraph()
    {
        if (Graph is null) return;
        if (PlanView.IsChecked == true) { RenderPlanGraph(); return; }
        Graph.Children.Clear();
        PipelineTitle.Text = Session.Pipeline.Name;
        GraphSubtitle.Text = Session.Pipeline.Activities.Count + " activities · select a node to edit · arrows follow contract dependencies";
        var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
        const double width = 205, height = 83, gap = 41, rowGap = 54;
        for (var index = 0; index < Session.Pipeline.Activities.Count; index++)
            positions[Session.Pipeline.Activities[index].Id] = new Point(25 + index % 3 * (width + gap), 32 + index / 3 * (height + rowGap));
        Graph.Width = 755;
        Graph.Height = Math.Max(300, 60 + Math.Ceiling(Session.Pipeline.Activities.Count / 3d) * (height + rowGap));
        var edges = Session.Pipeline.Activities.SelectMany(a => a.DependsOn.Select(from => (From: from, To: a.Id)))
            .Concat(Session.Pipeline.Edges.Select(e => (e.From, e.To))).Distinct();
        foreach (var (from, to) in edges)
        {
            if (!positions.TryGetValue(from, out var start) || !positions.TryGetValue(to, out var end)) continue;
            var horizontal = start.Y == end.Y;
            var a = horizontal ? new Point(start.X + width, start.Y + height / 2) : new Point(start.X + width / 2, start.Y + height);
            var b = horizontal ? new Point(end.X - 6, end.Y + height / 2) : new Point(end.X + width / 2, end.Y - 6);
            var points = horizontal ? new PointCollection { a, b } : new PointCollection { a, new(a.X, a.Y + rowGap / 2), new(b.X, a.Y + rowGap / 2), b };
            Graph.Children.Add(new Polyline { Points = points, Stroke = new SolidColorBrush(Color.FromRgb(132, 158, 171)), StrokeThickness = 1.6 });
            Graph.Children.Add(new Polygon { Fill = new SolidColorBrush(Color.FromRgb(132, 158, 171)), Points = horizontal ? new PointCollection { b, new(b.X - 7, b.Y - 4), new(b.X - 7, b.Y + 4) } : new PointCollection { b, new(b.X - 4, b.Y - 7), new(b.X + 4, b.Y - 7) } });
        }
        foreach (var node in Session.Pipeline.Activities)
        {
            var selected = selectedId == node.Id;
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = node.Kind.ToUpperInvariant(), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(8, 126, 131)) });
            content.Children.Add(new TextBlock { Text = node.Name ?? node.Id, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 4, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(new TextBlock { Text = node.SparkApiMode ?? node.Implementation ?? "Choose implementation", FontSize = 10, Foreground = Brushes.SlateGray, TextTrimming = TextTrimming.CharacterEllipsis });
            var button = new Button { Content = content, Width = width, Height = height, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 9, 12, 9), Background = selected ? new SolidColorBrush(Color.FromRgb(225, 244, 240)) : Brushes.White,
                BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(8, 126, 131)) : new SolidColorBrush(Color.FromRgb(202, 217, 225)),
                BorderThickness = new Thickness(selected ? 2 : 1), ToolTip = node.Id };
            button.Click += (_, _) => Guard(() => SelectActivity(node.Id));
            Canvas.SetLeft(button, positions[node.Id].X);
            Canvas.SetTop(button, positions[node.Id].Y);
            Graph.Children.Add(button);
        }
    }

    private void EditorChanged()
    {
        if (refreshing || !IsInitialized || PendingPanels().Count == 0) return;
        Session.InvalidateCompilation();
        PlanState.Text = "Plan out of date · apply edits";
        PlanPreview.Text = "Pending editor text has changed. Apply or discard it, then Plan again.";
        AirflowPreview.Text = IacPreview.Text = ManifestPreview.Text = "This revision has changed. Compile again to inspect current artifacts.";
        RenderGraph();
        RenderArchitecture();
    }

    public static string ValidationBadge(PlanStage stage) => stage.ImplementationStatus switch
    {
        "reference-only" => "REFERENCE",
        "unsupported" => "UNSUPPORTED",
        _ => stage.ValidationLevel switch
        {
            "reconciled" => "RECONCILED",
            "tested" => "TESTED",
            "executes" => "EXECUTED",
            "parses" => "PARSES",
            "generated" => "GENERATED ONLY",
            _ => "DECLARED"
        }
    };

    private void RenderPlanGraph()
    {
        Graph.Children.Clear();
        var plan = Session.Plan;
        PipelineTitle.Text = plan is null ? "Plan your data architecture" : plan.BusinessScenario.DisplayName;
        GraphSubtitle.Text = plan is null ? "Choose a scenario and architecture, apply overrides, then select Plan." :
            plan.Stages.Count + " resolved stages · badges describe implementation evidence · this project: " + plan.CurrentExecutionStatus;
        if (plan is null)
        {
            Graph.Width = 760;
            Graph.Height = 280;
            var message = new TextBlock { Text = "Business scenario + architecture + overrides\n↓\nPlan → review capabilities → compile", FontSize = 19, Foreground = Brushes.SlateGray, LineHeight = 33, TextAlignment = TextAlignment.Center, Width = 660 };
            Canvas.SetLeft(message, 45); Canvas.SetTop(message, 52); Graph.Children.Add(message);
            return;
        }
        const double width = 252, height = 150, gap = 32, rowGap = 50;
        var orderedStages = TopologicalStages(plan);
        var positions = orderedStages.Select((stage, index) => (stage.Id, Point: new Point(24 + index % 3 * (width + gap), 27 + index / 3 * (height + rowGap))))
            .ToDictionary(item => item.Id, item => item.Point, StringComparer.Ordinal);
        Graph.Width = 884;
        Graph.Height = Math.Max(280, 54 + Math.Ceiling(plan.Stages.Count / 3d) * (height + rowGap));
        foreach (var edge in plan.Edges)
        {
            if (!positions.TryGetValue(edge.From, out var start) || !positions.TryGetValue(edge.To, out var end)) continue;
            var sameRow = start.Y == end.Y;
            var horizontal = sameRow && end.X - start.X <= width + gap + 1;
            var a = horizontal ? new Point(start.X + width, start.Y + height / 2) : new Point(start.X + width / 2, sameRow ? start.Y : start.Y + height);
            var b = horizontal ? new Point(end.X - 6, end.Y + height / 2) : new Point(end.X + width / 2, end.Y - 6);
            var lane = sameRow ? start.Y - 15 : a.Y + rowGap / 2;
            var points = horizontal ? new PointCollection { a, b } : new PointCollection { a, new(a.X, lane), new(b.X, lane), b };
            Graph.Children.Add(new Polyline { Points = points, Stroke = new SolidColorBrush(Color.FromRgb(132, 158, 171)), StrokeThickness = 1.6, Tag = "plan-edge" });
            Graph.Children.Add(new Polygon { Fill = new SolidColorBrush(Color.FromRgb(132, 158, 171)), Points = horizontal ? new PointCollection { b, new(b.X - 7, b.Y - 4), new(b.X - 7, b.Y + 4) } : new PointCollection { b, new(b.X - 4, b.Y - 7), new(b.X + 4, b.Y - 7) } });
        }
        foreach (var stage in plan.Stages)
        {
            var reference = stage.ImplementationStatus is "reference-only" or "unsupported";
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = stage.Kind.ToUpperInvariant(), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(8, 126, 131)), TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(new TextBlock { Text = stage.Name, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 5, 0, 5), TextWrapping = TextWrapping.Wrap, MaxHeight = 40 });
            content.Children.Add(new TextBlock { Text = stage.Engine + " · " + stage.Runtime, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 33 });
            var badges = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            badges.Children.Add(Badge(ValidationBadge(stage), reference ? "#FFF0D9" : "#E1F1EC"));
            if (stage.Manual) badges.Children.Add(Badge("MANUAL", "#F0EAFB"));
            content.Children.Add(badges);
            var tooltip = stage.Reason + "\n\nImplementation: " + stage.ImplementationStatus + "\nExecution mode: " + stage.ExecutionMode +
                "\nSource / sink: " + stage.Source + " / " + stage.Sink + "\nInputs: " + string.Join(", ", stage.Inputs) + "\nOutputs: " + string.Join(", ", stage.Outputs) +
                "\nCompiler boundary: " + stage.CompilerBoundary + "\nEvidence: " + string.Join("; ", stage.Evidence.Select(e => e.Id + " (" + e.Scope + ")"));
            var border = new Border { Child = content, Width = width, Height = height, Padding = new Thickness(13, 11, 13, 10), CornerRadius = new CornerRadius(6), Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(202, 217, 225)), BorderThickness = new Thickness(1), ToolTip = tooltip, Tag = stage };
            Canvas.SetLeft(border, positions[stage.Id].X); Canvas.SetTop(border, positions[stage.Id].Y); Graph.Children.Add(border);
        }
    }

    private static IReadOnlyList<PlanStage> TopologicalStages(ResolvedPlan plan)
    {
        var remaining = plan.Stages.ToList();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<PlanStage>();
        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(stage => plan.Edges.Where(edge => edge.To == stage.Id).All(edge => emitted.Contains(edge.From)))
                ?? throw new ArgumentException("The resolved plan contains a cycle or an unknown dependency.");
            ordered.Add(next);
            emitted.Add(next.Id);
            remaining.Remove(next);
        }
        return ordered;
    }

    private static Border Badge(string text, string color) => new()
    {
        Background = (Brush)new BrushConverter().ConvertFromString(color)!, CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 6, 0),
        Child = new TextBlock { Text = text, FontSize = 9, FontWeight = FontWeights.SemiBold }
    };

    private void RenderArchitecture()
    {
        ArchitectureSummary.Children.Clear();
        var plan = Session.Plan;
        if (plan is null)
        {
            ArchitectureSummary.Children.Add(new TextBlock { Text = "Plan required", FontSize = 19, FontWeight = FontWeights.SemiBold });
            ArchitectureSummary.Children.Add(new TextBlock { Text = "Apply your scenario, architecture and overrides, then Plan. No credentials or runtime are needed to inspect the architecture.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
            PlanState.Text = PendingPanels().Count == 0 ? "Plan required" : "Plan out of date · apply edits";
            return;
        }
        var settings = plan.ResolvedSettings;
        SummarySection("RESOLVED ARCHITECTURE");
        SummaryValue("Business scenario", plan.BusinessScenario.DisplayName + "\n" + plan.BusinessScenario.Id);
        SummaryValue("Generation profile", plan.BusinessScenario.Profile + " · " + plan.GenerationProfile.Orders + " orders · " + plan.GenerationProfile.TimeSpanDays + " days");
        SummaryValue("Architecture preset", plan.ArchitecturePreset);
        SummaryValue("Engines", string.Join(", ", plan.Stages.Select(s => s.Engine).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()));
        SummaryValue("Runtimes", string.Join(", ", plan.Stages.Select(s => s.Runtime).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()));
        if (plan.Stages.Any(s => s.SparkApiMode is not null)) SummaryValue("Spark API / version", string.Join(", ", plan.Stages.Where(s => s.SparkApiMode is not null).Select(s => s.SparkApiMode + " / " + s.SparkVersion).Distinct()));
        SummaryValue("Storage / file / table format", settings.Storage + " / " + settings.FileFormat + " / " + settings.TableFormat);
        SummaryValue("Warehouse", settings.Warehouse ?? "none");
        SummaryValue("Orchestration / DAG source", settings.Orchestrator + " / " + settings.DagSource);
        SummaryValue("Infrastructure / cost profile", settings.Iac + " / " + settings.CostProfile);
        SummarySection("READINESS & EVIDENCE");
        SummaryValue("This project", plan.CurrentExecutionStatus + " · readiness: " + plan.OverallReadiness + "\nImplementation: " + plan.OverallImplementationStatus);
        SummaryValue("Stage badges", "Reference implementation evidence is not execution proof for this newly planned project. Hover over a stage for its evidence scope.");
        SummarySection("MANUAL CHECKPOINTS");
        if (plan.ManualCheckpoints.Count == 0) SummaryValue("None", "No manual checkpoint is declared by this plan.");
        foreach (var checkpoint in plan.ManualCheckpoints) SummaryValue(checkpoint.AfterStage, checkpoint.Reason);
        SummarySection("CREDENTIALS AT EXECUTION");
        if (plan.RequiredCredentials.Count == 0) SummaryValue("None", "This plan declares no external credentials.");
        foreach (var credential in plan.RequiredCredentials) SummaryValue(credential.Scope, credential.Reason + "\nPlan time: " + (credential.RequiredAtPlanTime ? "required" : "not required") + " · " + credential.Storage);
        SummarySection("COSTS & QUOTAS");
        foreach (var note in plan.CostAndQuotaNotes) SummaryValue("", note);
        if (plan.Warnings.Count > 0) SummarySection("WARNINGS");
        foreach (var warning in plan.Warnings) SummaryValue("", warning);
        PlanState.Text = "Plan current · execution not started";
    }

    private void SummarySection(string title) => ArchitectureSummary.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(8, 126, 131)), Margin = new Thickness(0, ArchitectureSummary.Children.Count == 0 ? 0 : 17, 0, 10) });
    private void SummaryValue(string label, string value)
    {
        if (label.Length > 0) ArchitectureSummary.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 3) });
        ArchitectureSummary.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });
    }

    private static int Number(string value, string label) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
        ? result : throw new ArgumentException(label + " must be an integer.");

    private Control[] PanelControls(string panel) => panel switch
    {
        "activity" => [NameBox, KindBox, ImplementationBox, SourceBox, SinkBox, EngineBox, RuntimeBox, ModeBox,
            VersionPolicyBox, VersionBox, RemoteBox, TableBox, ConnectionBox, InputsBox, OutputsBox, DependsBox,
            RetryBox, TimeoutBox, NodeParametersBox],
        "destination" => [WarehouseBox, ProjectIdBox, BigQueryDatasetBox, LocationBox, MaxBytesBox, DatasetBox,
            DatasetPathBox, DatasetTableBox, DatasetConnectionBox],
        "parameters" => [ParametersEditor],
        "preset/profile" => [ScenarioBox, PresetBox, CostBox],
        "overrides" => [OverridesEditor],
        _ => throw new ArgumentException("Unknown editor panel.")
    };

    private static string ReadControl(Control control) => control switch { TextBox text => text.Text, ComboBox combo when combo.Name == "ScenarioBox" => combo.SelectedValue as string ?? "", ComboBox combo when !combo.IsEditable => combo.SelectedItem as string ?? "", ComboBox combo => combo.Text, _ => "" };
    private static void WriteControl(Control control, string value)
    {
        if (control is TextBox text) text.Text = value;
        else if (control is ComboBox combo && combo.Name == "ScenarioBox") combo.SelectedValue = value;
        else if (control is ComboBox other) other.Text = value;
    }
    private string Signature(string panel) => string.Join("\u001f", PanelControls(panel).Where(c => c != DatasetBox).Select(ReadControl));
    private List<string> PendingPanels() => Panels.Where(panel => baselines.TryGetValue(panel, out var baseline) && baseline != Signature(panel)).ToList();
    private void RequireAppliedEdits(string action)
    {
        var pending = PendingPanels();
        if (pending.Count > 0) throw new ArgumentException("Apply or explicitly discard pending " + string.Join(", ", pending) + " edits before " + action + ". Your entered text has been preserved.");
    }
    public void DiscardPendingEdits()
    {
        RefreshAll();
        StatusText.Text = "Pending editor text discarded; the last applied contract is displayed.";
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            ValidationPreview.Text = error.Message;
            StatusText.Text = "Action failed. See Validation for the compiler/editor diagnostic.";
            PreviewTabs.SelectedIndex = 5;
        }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        RequireAppliedEdits("opening a project");
        var dialog = new OpenFileDialog { Filter = "Forge project JSON|*.json", Title = "Open Forge project" };
        if (dialog.ShowDialog(this) == true) LoadProject(dialog.FileName);
    });
    private void OpenPipeline_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        RequireAppliedEdits("opening a pipeline");
        var dialog = new OpenFileDialog { Filter = "Neutral pipeline JSON|*.json", Title = "Open pipeline.json" };
        if (dialog.ShowDialog(this) == true) LoadPipeline(dialog.FileName);
    });
    private void Save_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        RequireAppliedEdits("saving");
        var dialog = new SaveFileDialog { Filter = "Neutral pipeline JSON|*.json", FileName = "pipeline.json", Title = "Save pipeline and sibling project.json bundle" };
        if (dialog.ShowDialog(this) == true) SaveTo(dialog.FileName);
    });
    private void Compile_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        RequireAppliedEdits("compiling");
        var dialog = new OpenFolderDialog { Title = "Compile into an empty folder or an existing Forge output" };
        if (dialog.ShowDialog(this) == true) CompileTo(dialog.FolderName);
    });
    private void ApplyArchitecture_Click(object sender, RoutedEventArgs e) => Guard(ApplySelection);
    private void ApplyOverrides_Click(object sender, RoutedEventArgs e) => Guard(ApplyOverrides);
    private void Plan_Click(object sender, RoutedEventArgs e) => Guard(() => PlanCurrent());
    private void SavePlan_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        RequireAppliedEdits("saving a plan");
        if (Session.Plan is null) PlanCurrent();
        var dialog = new SaveFileDialog { Filter = "Resolved plan JSON|*.json", FileName = "resolved_plan.json", Title = "Save the current resolved architecture plan" };
        if (dialog.ShowDialog(this) == true) { Session.SavePlan(dialog.FileName); StatusText.Text = "Saved resolved plan · " + dialog.FileName; }
    });
    private void GraphView_Changed(object sender, RoutedEventArgs e) => RenderGraph();
    private void ApplyNode_Click(object sender, RoutedEventArgs e) => Guard(ApplyNode);
    private void ApplyDestination_Click(object sender, RoutedEventArgs e) => Guard(ApplyDestination);
    private void Validate_Click(object sender, RoutedEventArgs e) => Guard(() => ValidateGraph());
    private void Remove_Click(object sender, RoutedEventArgs e) => Guard(RemoveSelected);
    private void Parameters_Click(object sender, RoutedEventArgs e) => Guard(ApplyParameters);
    private void Discard_Click(object sender, RoutedEventArgs e) => Guard(DiscardPendingEdits);
    private void Dataset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing) return;
        if (PendingPanels().Contains("destination"))
        {
            refreshing = true;
            DatasetBox.SelectedItem = datasetSelectionId;
            refreshing = false;
            Guard(() => throw new ArgumentException("Apply or explicitly discard pending destination edits before selecting another dataset. Your entered text has been preserved."));
            return;
        }
        RefreshDataset();
    }
}
