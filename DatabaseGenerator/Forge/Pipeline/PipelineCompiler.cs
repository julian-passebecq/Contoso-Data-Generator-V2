#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DatabaseGenerator.Forge.Pipeline;

public static class PipelineCompiler
{
    public static IReadOnlyList<string> Validate(string pipelineJson)
    {
        var errors = new List<string>();
        var pipeline = PipelineValidation.Parse(pipelineJson, errors);
        if (pipeline is not null) PipelineValidation.Validate(pipeline, errors);
        return errors;
    }

    /// <summary>Validate the graph and effective activity settings before any project output is changed.</summary>
    public static IReadOnlyList<string> Validate(string pipelineJson, string resolvedProjectJson)
    {
        var errors = new List<string>();
        var pipeline = PipelineValidation.Parse(pipelineJson, errors);
        if (pipeline is not null) PipelineValidation.Validate(pipeline, errors);
        if (pipeline is null || errors.Count != 0) return errors;
        try
        {
            using var resolved = JsonDocument.Parse(resolvedProjectJson);
            ValidateResolvedCompatibility(pipeline, ReadSettings(resolved.RootElement), errors);
        }
        catch (JsonException ex) { errors.Add("Invalid resolved project JSON: " + ex.Message); }
        catch (ArgumentException ex) { errors.Add(ex.Message); }
        return errors;
    }

    public static string CreateDefault(string resolvedProjectJson)
    {
        using var document = JsonDocument.Parse(resolvedProjectJson);
        var settings = ReadSettings(document.RootElement);
        var colab = Get(settings, "runtime").StartsWith("google-colab", StringComparison.Ordinal);
        var pipeline = new PipelineDefinition
        {
            Id = "contoso_forge_pipeline",
            Name = "Contoso Forge data factory",
            Annotations = new() { "Generated from the resolved architecture preset; edit this neutral contract and recompile.", "Google Colab is an explicit human work-order boundary." },
            Connections = new()
            {
                new() { Id = "source_files", Type = "local", DisplayName = "Generated source files" }
            },
            Datasets = new()
            {
                new() { Id = "source_csv", ConnectionRef = "source_files", Path = "data/source", Format = "csv", TableFormat = "none" },
                new() { Id = "work_order", Path = "runtime://work-order", Format = "json" },
                new() { Id = "result_manifest", Path = "runtime://result-manifest", Format = "json" }
            },
            Activities = new()
            {
                new() { Id = "verify_source", Kind = "source", Implementation = "generated-source", Source = "local", Outputs = new() { "source_csv" } },
                new() { Id = colab ? "prepare_colab" : "transform", Kind = colab ? "handoff" : "transform", Implementation = colab ? "colab-work-order" : "engine-transform",
                    Inputs = new() { "source_csv" }, Outputs = new() { "work_order" }, DependsOn = new() { "verify_source" } },
                new() { Id = "await_result", Kind = "manual-checkpoint", Implementation = colab ? "colab-result" : "external-result",
                    Inputs = new() { "work_order" }, Outputs = new() { "result_manifest" },
                    DependsOn = new() { colab ? "prepare_colab" : "transform" }, TimeoutSeconds = 86400 },
                new() { Id = "reconcile", Kind = "validate", Implementation = "truth-manifest",
                    Inputs = new() { "result_manifest" }, DependsOn = new() { "await_result" } }
            }
        };
        return JsonSerializer.Serialize(pipeline, PipelineJsonContext.Default.PipelineDefinition) + "\n";
    }

    /// <summary>Resolve the exact compiler operations without creating files or contacting runtimes.</summary>
    public static PipelineExecutionPlan Inspect(string pipelineJson, string resolvedProjectJson) =>
        Prepare(pipelineJson, resolvedProjectJson).Plan;

    private static (PipelineDefinition Pipeline, PipelineExecutionPlan Plan, List<string> Order, Dictionary<string, string> Settings)
        Prepare(string pipelineJson, string resolvedProjectJson)
    {
        var errors = new List<string>();
        var pipeline = PipelineValidation.Parse(pipelineJson, errors);
        if (pipeline is not null) PipelineValidation.Validate(pipeline, errors);
        if (errors.Count != 0 || pipeline is null) throw new ArgumentException("Invalid pipeline: " + string.Join(" ", errors), nameof(pipelineJson));
        using var resolved = JsonDocument.Parse(resolvedProjectJson);
        var settings = ReadSettings(resolved.RootElement);
        ValidateResolvedCompatibility(pipeline, settings, errors);
        if (errors.Count != 0) throw new ArgumentException("Invalid resolved pipeline: " + string.Join(" ", errors), nameof(resolvedProjectJson));
        var order = PipelineValidation.TopologicalSort(pipeline);
        // Canonical serialization is independent of input array/dependency order.
        pipeline.ContractVersion = "1.2";
        pipeline.Activities = order.Select(id => pipeline.Activities.Single(a => a.Id == id)).ToList();
        pipeline.Datasets = pipeline.Datasets.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        pipeline.Connections = pipeline.Connections.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        pipeline.Parameters = pipeline.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value);
        pipeline.Variables = pipeline.Variables.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value);
        pipeline.Edges = pipeline.Edges.GroupBy(e => (e.From, e.To)).Select(g => g.First())
            .OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal).ToList();
        foreach (var activity in pipeline.Activities) activity.DependsOn = Dependencies(pipeline, activity);
        var plan = new PipelineExecutionPlan
        {
            PipelineId = pipeline.Id,
            PresetId = resolved.RootElement.TryGetProperty("presetId", out var preset) ? preset.GetString() ?? "custom" : "custom",
            Exporters = new()
            {
                ["airflow"] = "generated-reference",
                ["local"] = "generated-reference",
                ["databricks-jobs"] = "unsupported",
                ["fabric-pipeline"] = "unsupported",
                ["azure-data-factory"] = "unsupported",
                ["google-workflows"] = "unsupported"
            },
            Activities = pipeline.Activities.Select(a => Map(a, settings)).ToList()
        };
        foreach (var activity in pipeline.Activities)
            CheckDatasetBindings(pipeline, activity, plan.Activities.Single(p => p.Id == activity.Id));
        var preparations = plan.Activities.Where(a => a.Operation == "prepare-colab").ToList();
        foreach (var activity in plan.Activities.Where(a => a.Operation is "await-colab" or "reconcile-colab"))
        {
            if (preparations.Count != 1 || !HasAncestor(pipeline, activity.Id, preparations[0].Id))
            {
                activity.Operation = "unsupported";
                activity.Status = "unsupported";
                activity.Reason = "The Colab result mapping requires exactly one upstream Colab work-order activity.";
            }
        }
        if (preparations.Count > 1)
            foreach (var activity in preparations)
            {
                activity.Operation = "unsupported";
                activity.Status = "unsupported";
                activity.Reason = "Multiple independent Colab work orders require an exporter extension; no work order will be silently reused.";
            }
        if (plan.Activities.Any(a => a.Status == "unsupported")) plan.ArtifactStatus = "unsupported";
        else if (plan.Activities.Any(a => a.Status == "manual-checkpoint")) plan.ArtifactStatus = "manual-checkpoint";
        return (pipeline, plan, order, settings);
    }

    public static PipelineCompilationResult Compile(string pipelineJson, string resolvedProjectJson, string outputRoot)
    {
        var (pipeline, plan, order, settings) = Prepare(pipelineJson, resolvedProjectJson);
        var canonical = JsonSerializer.Serialize(pipeline, PipelineJsonContext.Default.PipelineDefinition) + "\n";
        // V1.3's audited Base64 payload used Windows JSON indentation plus a final LF.
        // Pin those bytes on every OS before encoding; Write normalizes standalone JSON to LF.
        var planJson = JsonSerializer.Serialize(plan, PipelineJsonContext.Default.PipelineExecutionPlan)
            .Replace("\r\n", "\n").Replace("\n", "\r\n") + "\n";
        var root = Path.GetFullPath(outputRoot);
        Write(root, "pipeline.json", canonical);
        Write(root, "local_plan.json", planJson);
        Write(root, "pipeline/local_plan.json", planJson);
        Write(root, "pipeline/run_local.py", PipelineRuntimeTemplates.LocalRunner);
        Write(root, "pipeline/forge_pipeline_runtime.py", PipelineRuntimeTemplates.Runtime);
        Write(root, "airflow/dags/forge_pipeline_runtime.py", PipelineRuntimeTemplates.Runtime);
        Write(root, "airflow/dags/contoso_forge_pipeline.py", BuildAirflow(planJson));
        if (Get(settings, "orchestrator") == "airflow-minikube")
            Write(root, "airflow/dags/.airflowignore", "contoso_forge_customer_satisfaction.py\n");
        Write(root, "pipeline/graph.mmd", Graph(pipeline));
        Write(root, "pipeline/COMPILER.md", CompilerReadme);
        return new() { OutputRoot = root, TopologicalOrder = order, Plan = plan };
    }

    private static PipelinePlannedActivity Map(PipelineActivity activity, Dictionary<string, string> settings)
    {
        var mapped = new PipelinePlannedActivity
        {
            Id = activity.Id, Kind = activity.Kind,
            Engine = activity.Engine ?? Get(settings, "engine"), Runtime = activity.Runtime ?? Get(settings, "runtime"),
            SparkApiMode = activity.SparkApiMode ?? SparkMode(activity.Runtime ?? Get(settings, "runtime"), settings),
            SparkVersionPolicy = activity.SparkVersionPolicy ?? settings.GetValueOrDefault("sparkVersionPolicy"),
            SparkVersion = activity.SparkVersion ?? settings.GetValueOrDefault("sparkVersion"),
            SparkRemote = activity.SparkRemote ?? settings.GetValueOrDefault("sparkRemote"),
            Source = activity.Source ?? Get(settings, "storage"), Sink = activity.Sink ?? Get(settings, "warehouse"),
            DependsOn = activity.DependsOn.ToList(), MaximumAttempts = activity.Retry.MaximumAttempts,
            BackoffSeconds = activity.Retry.BackoffSeconds, TimeoutSeconds = activity.TimeoutSeconds,
            Reason = "No executable exporter mapping exists for this activity configuration. Extend an exporter or select a supported implementation."
        };
        if (!activity.Enabled)
        {
            mapped.Reason = "Activity is disabled. Execution stops here; dependent work cannot be declared successful.";
            return mapped;
        }
        if (activity.Parameters.Count > 0 || activity.ConnectionRef is not null || activity.Connector is not null || activity.Table is not null || activity.Profile is not null)
        {
            mapped.Reason = "This activity carries custom bindings or connector properties requiring an exporter mapping; they will not be ignored.";
            return mapped;
        }
        if (activity.Kind == "source" && activity.Implementation == "generated-source" && mapped.Source == "local")
        {
            mapped.Operation = "verify-source";
            mapped.Status = "generated-reference";
            mapped.Reason = "Verify every generated source file against truth_manifest.json SHA-256 values.";
            return mapped;
        }
        var colabBigQuery = mapped.Engine == "spark" && mapped.Runtime is "google-colab" or "google-colab-connect-local"
            && mapped.SparkApiMode is "classic" or "connect-local" && mapped.Sink == "bigquery" && mapped.Source == "local"
            && (activity.FileFormat ?? Get(settings, "fileFormat")) == "parquet" && (activity.TableFormat ?? Get(settings, "tableFormat")) == "none";
        if (!colabBigQuery) return mapped;
        if (activity.Kind is "handoff" or "notebook" && activity.Implementation == "colab-work-order")
        {
            mapped.Operation = "prepare-colab";
            mapped.Status = "generated-reference";
            mapped.Reason = "Create an isolated run work order and ZIP. A human runs Colab Spark and BigQuery load/reconciliation.";
        }
        else if (activity.Kind == "manual-checkpoint" && activity.Implementation == "colab-result")
        {
            mapped.Operation = "await-colab";
            mapped.Status = "manual-checkpoint";
            mapped.Reason = "Wait for this run's returned result; verify identity, freshness, fingerprint and observed reconciliation before success.";
        }
        else if (activity.Kind == "validate" && activity.Implementation == "truth-manifest")
        {
            mapped.Operation = "reconcile-colab";
            mapped.Status = "generated-reference";
            mapped.Reason = "Recheck actual returned BigQuery counts/KPIs and source/Silver counts against the truth manifest.";
        }
        return mapped;
    }

    private static void CheckDatasetBindings(PipelineDefinition pipeline, PipelineActivity activity, PipelinePlannedActivity mapped)
    {
        if (mapped.Operation == "unsupported") return;
        bool Matches(string id, string expectedPath, string expectedFormat)
        {
            var dataset = pipeline.Datasets.Single(d => d.Id == id);
            var connection = pipeline.Connections.SingleOrDefault(c => c.Id == dataset.ConnectionRef);
            return dataset.Path == expectedPath && (dataset.Format is null || dataset.Format == expectedFormat)
                && dataset.Table is null && dataset.Query is null && dataset.SchemaRef is null
                && dataset.TableFormat is null or "none" && dataset.Partitioning.Count == 0 && dataset.Options.Count == 0
                && (connection is null || connection.Type == "local" && connection.SecretRef is null && connection.NonSecretProperties.Count == 0);
        }
        var valid = mapped.Operation switch
        {
            "verify-source" => activity.Inputs.Count == 0 && activity.Outputs.All(id => Matches(id, "data/source", "csv")),
            "prepare-colab" => activity.Inputs.All(id => Matches(id, "data/source", "csv"))
                && activity.Outputs.All(id => Matches(id, "runtime://work-order", "json")),
            "await-colab" => activity.Inputs.All(id => Matches(id, "runtime://work-order", "json"))
                && activity.Outputs.All(id => Matches(id, "runtime://result-manifest", "json")),
            "reconcile-colab" => activity.Inputs.All(id => Matches(id, "runtime://result-manifest", "json")) && activity.Outputs.Count == 0,
            _ => false
        };
        if (!valid)
        {
            mapped.Operation = "unsupported";
            mapped.Status = "unsupported";
            mapped.Reason = "Custom dataset paths, queries, schemas, partitions or connection properties require an exporter mapping; they will not be ignored.";
        }
    }

    private static List<string> Dependencies(PipelineDefinition pipeline, PipelineActivity activity) => activity.DependsOn
        .Concat(pipeline.Edges.Where(e => e.To == activity.Id).Select(e => e.From)).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();

    private static bool HasAncestor(PipelineDefinition pipeline, string id, string target)
    {
        var pending = new Stack<string>(pipeline.Activities.Single(a => a.Id == id).DependsOn);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryPop(out var current))
        {
            if (current == target) return true;
            if (visited.Add(current)) foreach (var parent in pipeline.Activities.Single(a => a.Id == current).DependsOn) pending.Push(parent);
        }
        return false;
    }

    private static Dictionary<string, string> ReadSettings(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("resolved_project.json must contain a settings object.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in settings.EnumerateObject())
            if (item.Value.ValueKind == JsonValueKind.String) result[item.Name] = item.Value.GetString()!;
        foreach (var key in new[] { "engine", "runtime", "storage", "fileFormat", "tableFormat", "warehouse" })
            if (!result.ContainsKey(key)) throw new ArgumentException($"resolved_project.json settings.{key} is required.");
        return result;
    }

    private static void ValidateResolvedCompatibility(PipelineDefinition pipeline, Dictionary<string, string> settings, List<string> errors)
    {
        foreach (var activity in pipeline.Activities)
        {
            PipelineValidation.CheckCompatibility(activity.Engine ?? Get(settings, "engine"), activity.Runtime ?? Get(settings, "runtime"),
                activity.FileFormat ?? Get(settings, "fileFormat"), activity.TableFormat ?? Get(settings, "tableFormat"), activity.Id, errors);
            PipelineValidation.CheckSpark(activity, errors, settings);
        }
    }

    private static string? SparkMode(string runtime, Dictionary<string, string> settings) => runtime switch
    {
        "google-colab-connect-local" => "connect-local", "google-colab-connect-remote" => "connect-remote",
        _ => settings.GetValueOrDefault("sparkApiMode") ?? (runtime == "google-colab" ? "classic" : null)
    };

    private static string Get(Dictionary<string, string> settings, string key) => settings.TryGetValue(key, out var value) ? value : "none";
    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
    }

    private static string Graph(PipelineDefinition pipeline)
    {
        var graph = new StringBuilder("flowchart TD\n");
        foreach (var activity in pipeline.Activities)
        {
            graph.Append("  ").Append(activity.Id).Append("[\"").Append(activity.Id).Append(" / ").Append(activity.Kind).Append("\"]\n");
            foreach (var parent in activity.DependsOn) graph.Append("  ").Append(parent).Append(" --> ").Append(activity.Id).Append('\n');
        }
        return graph.ToString();
    }

    private static string BuildAirflow(string planJson) => PipelineRuntimeTemplates.Airflow.Replace("__PLAN_BASE64__", Convert.ToBase64String(Encoding.UTF8.GetBytes(planJson)));

    private const string CompilerReadme = """
        # Neutral pipeline compiler

        `../pipeline.json` is the source of truth. The V1 `pipeline/pipeline.json` artifact remains separate and unchanged.
        Activities, datasets, connection references, typed parameter defaults, engine/runtime/source/sink overrides,
        retries and timeouts are validated before export. Dependencies may use `dependsOn`, `edges`, or both.
        IDs are sorted stably when independent; unknown activity kinds, dangling references, cycles, duplicate JSON
        properties, unresolved parameters, incompatible formats and literal credential fields are rejected.

        The compiler emits Airflow 3, a sequential local runner, and a Mermaid graph. Exporter status in
        `../local_plan.json` is explicit: `generated-reference`, `manual-checkpoint`, or `unsupported`.
        `generated-reference` means generated code requiring its runtime and external setup, not a cloud execution claim.
        Databricks, Fabric, ADF and Google Workflows exporters are currently unsupported. Custom connector bindings
        remain in the neutral contract and stop execution until an exporter is supplied; no placeholder succeeds.

        Run locally with `python pipeline/run_local.py --root . --run-id example-001`.
        The runner verifies generated source hashes, packages a work order, and exits 75 at the human checkpoint.
        Upload the displayed ZIP into the generated Colab notebook; run Spark and native BigQuery loads there.
        Copy the returned `result_manifest.json` to the exact run directory printed by the runner, then run the same
        command again. Reusing a run ID resumes its existing work order; it does not regenerate timestamps or hide expiry.
        A new execution uses a new run ID. Invalid, stale or mismatched results fail; only an absent result waits.

        Airflow uses `airflow.sdk.DAG`, the standard provider's PythonOperator and PythonSensor, with reschedule mode
        for manual waiting. Set FORGE_PROJECT_ROOT to a read-only generated project and FORGE_STATE_ROOT to a writable
        shared volume visible to all Airflow tasks. This storage is separate from the GitSync checkout. Each DAG run
        receives isolated state. Colab never runs unattended in this preset. Sensors require valid reconciled results.

        This first exporter implements the local source / Spark Colab / Parquet / BigQuery native flow. Other
        architectures preserve contracts and receive an explicit unsupported mapping. Extending the backend does not
        require changing the core contract or the validated V1 Docker pipeline.

        API references: https://airflow.apache.org/docs/apache-airflow/stable/public-airflow-interface.html
        and https://airflow.apache.org/docs/apache-airflow-providers-standard/stable/sensors/python.html
        """;
}
