using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DatabaseGenerator.Forge.Planning;
using DatabaseGenerator.Forge.Architecture;

namespace ContosoForge.PipelineStudio;

public partial class MainWindow
{
    public ComboBox GoalBox { get; } = new() { Width = 240, Height = 32, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = ProductIntent.Goals };
    public ComboBox StopAfterBox { get; } = new() { Width = 220, Height = 32, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = ProductIntent.StopStages };
    public TextBox SelectedKpisBox { get; } = new() { MinWidth = 320 };
    public TextBox AnalysisEditor { get; } = new() { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public TextBox PublishEditor { get; } = new() { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public TextBox RecipeEditor { get; } = new() { AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private TabItem[] legacyTabs = [];
    private TabItem[] journeyTabs = [];
    private bool journeyVisible;
    private TabControl advancedTabs = new();
    private TabControl resultTabs = new();
    private readonly JsonSerializerOptions journeyJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        TypeInfoResolver = ArchitectureJsonContext.Default,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private void InitializeJourneys()
    {
        legacyTabs = ProductFlowTabs.Items.Cast<TabItem>().ToArray();
        TabItem Tab(string title, string description, UIElement editor)
        {
            var panel = new DockPanel { Margin = new Thickness(16, 10, 16, 10) };
            var label = new TextBlock { Text = description, FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(label, Dock.Top); panel.Children.Add(label);
            var button = new Button { Content = "Apply journey settings", HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => Guard(ApplyJourneySettings); DockPanel.SetDock(button, Dock.Bottom); panel.Children.Add(button);
            panel.Children.Add(editor);
            return new TabItem { Header = title, Content = panel };
        }
        var analytics = new DockPanel();
        DockPanel.SetDock(SelectedKpisBox, Dock.Top); analytics.Children.Add(SelectedKpisBox); analytics.Children.Add(AnalysisEditor);
        journeyTabs = [Tab("Goal", "Choose what to accomplish. Engine and runtime options are under Advanced.", GoalBox), legacyTabs[1],
            Tab("Stop stage", "The run completes at this stage. Later transformations, analysis and reports are excluded.", StopAfterBox),
            Tab("Transformation", "Optional safe wrangling recipe over validated Silver. Leave null to use the governed transformations.", RecipeEditor),
            Tab("KPI / ML configuration", "Selected KPI IDs (comma separated), then typed analysis settings. ML requires an explicit algorithm or bounded MLJAR budget.", analytics),
            Tab("Destination / publish", "Optional typed publish targets. Explicit repository/database IDs and execute=true enable credentialed actions.", PublishEditor),
            legacyTabs[4], new TabItem { Header = "Results", Content = resultTabs }];
        GoalBox.SelectionChanged += (_, _) =>
        {
            if (refreshing) return;
            var goal = GoalBox.SelectedItem as string;
            if (goal == "data-only") { StopAfterBox.SelectedItem = "silver"; SelectedKpisBox.Text = ""; }
            else if (goal == "kpi-semantic") { StopAfterBox.SelectedItem = "report"; if (string.IsNullOrWhiteSpace(SelectedKpisBox.Text)) SelectedKpisBox.Text = "order_count, gross_sales_amount"; }
            else { StopAfterBox.SelectedItem = "report"; SelectedKpisBox.Text = ""; }
            AnalysisEditor.Text = JsonSerializer.Serialize(new AnalysisIntent { Kind = goal == "automl" ? "mljar" : goal == "specific-ml" ? "sklearn" : "none",
                Runtime = goal == "automl" ? "kaggle" : "local", Algorithm = goal == "specific-ml" ? "logistic_regression" : null }, journeyJson);
        };
        var enable = new Button { Content = "Start V1.7 goal-driven journey", HorizontalAlignment = HorizontalAlignment.Left };
        enable.Click += (_, _) => Guard(() =>
        {
            Session.ApplyArchitecture("local-fast", "local");
            Session.ApplyProduct(new ProductIntent { Version = "1.7", Goal = "data-only", StopAfter = "silver", SelectedKpis = [], Analysis = new(), PublishTargets = [] });
            Changed("V1.7 journey enabled. Choose a goal and stop stage.");
        });
        ((StackPanel)legacyTabs[0].Content).Children.Add(enable);
    }

    private void RefreshJourney()
    {
        var intent = Session.Project.Product;
        var active = intent?.Version == "1.7";
        if (active != journeyVisible)
        {
            ProductFlowTabs.Items.Clear();
            if (active)
            {
                var run = legacyTabs[8].Content; var results = legacyTabs[9].Content;
                legacyTabs[8].Content = null; legacyTabs[9].Content = null;
                resultTabs.Items.Add(new TabItem { Header = "Run", Content = run });
                resultTabs.Items.Add(new TabItem { Header = "Measured results", Content = results });
                foreach (var tab in journeyTabs) ProductFlowTabs.Items.Add(tab);
                // A nested Advanced tab preserves the existing controls and pending-edit handling.
                foreach (var index in new[] { 3, 5, 6, 7 }) advancedTabs.Items.Add(legacyTabs[index]);
                ProductFlowTabs.Items.Add(new TabItem { Header = "Advanced", Content = advancedTabs });
            }
            else
            {
                advancedTabs.Items.Clear();
                var runTab = (TabItem)resultTabs.Items[0]; var resultsTab = (TabItem)resultTabs.Items[1];
                var run = runTab.Content; var results = resultsTab.Content;
                runTab.Content = null; resultsTab.Content = null; resultTabs.Items.Clear();
                legacyTabs[8].Content = run; legacyTabs[9].Content = results;
                foreach (var tab in legacyTabs) ProductFlowTabs.Items.Add(tab);
            }
            ProductFlowTabs.SelectedIndex = 0;
            journeyVisible = active;
        }
        GoalBox.SelectedItem = intent?.Goal ?? "data-only";
        StopAfterBox.SelectedItem = intent?.StopAfter ?? "silver";
        SelectedKpisBox.Text = string.Join(", ", intent?.SelectedKpis ?? []);
        AnalysisEditor.Text = JsonSerializer.Serialize(intent?.Analysis ?? new AnalysisIntent(), journeyJson);
        PublishEditor.Text = JsonSerializer.Serialize(intent?.PublishTargets ?? [], journeyJson);
        RecipeEditor.Text = JsonSerializer.Serialize(intent?.Recipe, journeyJson);
    }

    public void ApplyJourneySettings()
    {
        var current = Session.Project.Product ?? new();
        if (GoalBox.Text is "specific-ml" or "automl" && Session.Project.BusinessScenario != ScenarioCatalog.MlScenarioId)
            Session.ApplyScenario(ScenarioCatalog.MlScenarioId);
        Session.ApplyProduct(new ProductIntent { Version = "1.7", Goal = GoalBox.Text, StopAfter = StopAfterBox.Text,
            SelectedKpis = Ids(SelectedKpisBox.Text), Analysis = JsonSerializer.Deserialize<AnalysisIntent>(AnalysisEditor.Text, journeyJson),
            PublishTargets = JsonSerializer.Deserialize<List<PublishTarget>>(PublishEditor.Text, journeyJson), Recipe = JsonSerializer.Deserialize<WranglingRecipe>(RecipeEditor.Text, journeyJson),
            PipelineMode = current.PipelineMode, MlTarget = current.MlTarget, BiTarget = current.BiTarget, DbtIntegration = current.DbtIntegration,
            LabelAsOf = current.LabelAsOf, MaterializationLimitMb = current.MaterializationLimitMb });
        Changed("Journey applied. Plan to review the selected stage boundary and runtime support.", "product");
    }
}
