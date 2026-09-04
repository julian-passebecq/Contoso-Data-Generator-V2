#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using DatabaseGenerator.Forge.Architecture;

namespace DatabaseGenerator.Forge.Pipeline;

internal static class PipelineValidation
{
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex Reference = new(@"\$\{(parameters|variables)\.([^}]+)\}", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
        { "source", "extract", "copy", "transform", "spark", "sql", "dbt", "validate", "ml", "notebook", "handoff", "sink", "load", "manual-checkpoint" };
    private static readonly HashSet<string> CredentialKeys = new(StringComparer.OrdinalIgnoreCase)
        { "password", "passwd", "pwd", "secret", "clientsecret", "privatekey", "privatekeyid", "token", "accesstoken", "refreshtoken", "apikey", "accesskey", "secretkey", "awsaccesskeyid", "awssecretaccesskey", "credentials", "credentialjson", "connectionstring", "sas", "sastoken" };

    internal static PipelineDefinition? Parse(string json, List<string> errors)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Pipeline must be a JSON object.");
                return null;
            }
            InspectJson(document.RootElement, "$", errors);
            var root = document.RootElement;
            if (!root.TryGetProperty("contractVersion", out _)) errors.Add("contractVersion is required.");
            if (!root.TryGetProperty("name", out _)) errors.Add("name is required.");
            if (!root.TryGetProperty("activities", out _) && !root.TryGetProperty("nodes", out _)) errors.Add("activities is required.");
            var pipeline = JsonSerializer.Deserialize(json, PipelineJsonContext.Default.PipelineDefinition);
            if (pipeline is null) { errors.Add("Pipeline cannot be null."); return null; }
            if (pipeline.Nodes is not null)
            {
                if (pipeline.Activities.Count > 0) errors.Add("Specify activities or legacy nodes, not both.");
                else pipeline.Activities = pipeline.Nodes;
                pipeline.Nodes = null;
            }
            return pipeline;
        }
        catch (JsonException ex) { errors.Add($"Invalid pipeline JSON: {ex.Message}"); return null; }
        catch (NotSupportedException ex) { errors.Add($"Invalid pipeline contract: {ex.Message}"); return null; }
        catch (NullReferenceException) { errors.Add("Pipeline collections and their members must not be null."); return null; }
    }

    internal static void Validate(PipelineDefinition pipeline, List<string> errors)
    {
        if (pipeline.Parameters is null || pipeline.Variables is null || pipeline.Connections is null ||
            pipeline.Datasets is null || pipeline.Activities is null || pipeline.Edges is null || pipeline.Annotations is null ||
            pipeline.Parameters.Values.Any(p => p is null) ||
            pipeline.Connections.Any(c => c is null || c.NonSecretProperties is null) ||
            pipeline.Datasets.Any(d => d is null || d.Partitioning is null || d.Options is null) ||
            pipeline.Activities.Any(a => a is null || a.Inputs is null || a.Outputs is null || a.Parameters is null || a.Retry is null || a.DependsOn is null) ||
            pipeline.Edges.Any(e => e is null))
        {
            errors.Add("Pipeline collections, retry policies and their members must not be null.");
            return;
        }
        if (pipeline.ContractVersion is not ("1.1" or "1.2")) errors.Add("contractVersion must be 1.1 or 1.2.");
        CheckId(pipeline.Id, "pipeline", errors);
        if (string.IsNullOrWhiteSpace(pipeline.Name)) errors.Add("Pipeline name is required.");
        try
        {
            if (pipeline.Activities.Count == 0) errors.Add("Pipeline must contain at least one activity.");
            var activities = Unique(pipeline.Activities.Select(a => a.Id), "activity", errors);
            var datasets = Unique(pipeline.Datasets.Select(d => d.Id), "dataset", errors);
            var connections = Unique(pipeline.Connections.Select(c => c.Id), "connection", errors);
            foreach (var (name, parameter) in pipeline.Parameters)
            {
                CheckId(name, "parameter", errors);
                CheckParameter(name, parameter, errors);
            }
            foreach (var name in pipeline.Variables.Keys) CheckId(name, "variable", errors);
            foreach (var connection in pipeline.Connections)
            {
                if (string.IsNullOrWhiteSpace(connection.Type)) errors.Add($"Connection '{connection.Id}' requires type.");
                if ((connection.SecretRef is null) != (connection.SecretProvider is null))
                    errors.Add($"Connection '{connection.Id}' requires both secretProvider and secretRef.");
                if (connection.SecretRef is not null && (string.IsNullOrWhiteSpace(connection.SecretRef) || string.IsNullOrWhiteSpace(connection.SecretProvider)))
                    errors.Add($"Connection '{connection.Id}' secret references cannot be empty.");
            }
            foreach (var dataset in pipeline.Datasets)
            {
                CheckConnection(dataset.ConnectionRef, dataset.Id, connections, errors);
                if (string.IsNullOrWhiteSpace(dataset.Path) && string.IsNullOrWhiteSpace(dataset.Table) && string.IsNullOrWhiteSpace(dataset.Query))
                    errors.Add($"Dataset '{dataset.Id}' requires path, table or query.");
                CheckFormats(dataset.Format, dataset.TableFormat, $"Dataset '{dataset.Id}'", errors);
            }
            foreach (var activity in pipeline.Activities)
            {
                if (!Kinds.Contains(activity.Kind)) errors.Add($"Activity '{activity.Id}' has unknown kind '{activity.Kind}'.");
                CheckConnection(activity.ConnectionRef, activity.Id, connections, errors);
                foreach (var dataset in activity.Inputs.Concat(activity.Outputs))
                    if (!datasets.Contains(dataset)) errors.Add($"Activity '{activity.Id}' references missing dataset '{dataset}'.");
                foreach (var dependency in activity.DependsOn)
                    if (!activities.Contains(dependency)) errors.Add($"Activity '{activity.Id}' has dangling dependency '{dependency}'.");
                if (activity.Retry.MaximumAttempts is < 1 or > 100) errors.Add($"Activity '{activity.Id}' retry.maximumAttempts must be between 1 and 100.");
                if (activity.Retry.BackoffSeconds is < 0 or > 86400) errors.Add($"Activity '{activity.Id}' retry.backoffSeconds must be between 0 and 86400.");
                if (activity.TimeoutSeconds is < 1 or > 604800) errors.Add($"Activity '{activity.Id}' timeoutSeconds must be between 1 and 604800.");
                CheckCompatibility(activity.Engine, activity.Runtime, activity.FileFormat, activity.TableFormat, activity.Id, errors);
                CheckSpark(activity, errors);
            }
            foreach (var edge in pipeline.Edges)
            {
                if (!activities.Contains(edge.From)) errors.Add($"Edge has dangling from activity '{edge.From}'.");
                if (!activities.Contains(edge.To)) errors.Add($"Edge has dangling to activity '{edge.To}'.");
            }
            foreach (var value in Strings(JsonSerializer.SerializeToElement(pipeline, PipelineJsonContext.Default.PipelineDefinition)))
                foreach (Match reference in Reference.Matches(value))
                {
                    var exists = reference.Groups[1].Value == "parameters"
                        ? pipeline.Parameters.ContainsKey(reference.Groups[2].Value)
                        : pipeline.Variables.ContainsKey(reference.Groups[2].Value);
                    if (!exists) errors.Add($"Unresolved {reference.Groups[1].Value} reference '{reference.Groups[2].Value}'.");
                }
            if (activities.Count == pipeline.Activities.Count && TopologicalSort(pipeline).Count != pipeline.Activities.Count)
                errors.Add("Pipeline contains a dependency cycle (including self dependencies).");
        }
        catch (NullReferenceException) { errors.Add("Pipeline collections, retry policies and their members must not be null."); }
        catch (ArgumentNullException) { errors.Add("Pipeline identifiers and references must not be null."); }
    }

    internal static List<string> TopologicalSort(PipelineDefinition pipeline)
    {
        var incoming = pipeline.Activities.ToDictionary(a => a.Id, a => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var activity in pipeline.Activities)
            foreach (var predecessor in activity.DependsOn) incoming[activity.Id].Add(predecessor);
        foreach (var edge in pipeline.Edges)
            if (incoming.TryGetValue(edge.To, out var dependencies)) dependencies.Add(edge.From);
        var ready = new SortedSet<string>(incoming.Where(p => p.Value.Count == 0).Select(p => p.Key), StringComparer.Ordinal);
        var order = new List<string>();
        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            order.Add(id);
            foreach (var (child, dependencies) in incoming)
                if (dependencies.Remove(id) && dependencies.Count == 0) ready.Add(child);
        }
        return order;
    }

    internal static void CheckCompatibility(string? engine, string? runtime, string? format, string? tableFormat, string id, List<string> errors)
    {
        CheckFormats(format, tableFormat, $"Activity '{id}'", errors);
        if (runtime is "google-colab" or "google-colab-connect-local" or "google-colab-connect-remote" or "databricks" or "fabric-spark" && engine is not null && engine != "spark")
            errors.Add($"Activity '{id}': runtime '{runtime}' requires engine 'spark'.");
        if (engine is "pandas" or "polars" && tableFormat == "delta")
            errors.Add($"Activity '{id}': engine '{engine}' with Delta is not a supported contract combination.");
    }

    internal static void CheckSpark(PipelineActivity activity, List<string> errors, Dictionary<string, string>? settings = null)
    {
        // Source verification and result checkpoints do not execute Spark or access its storage.
        if (activity.Kind is "source" or "validate" or "manual-checkpoint" && activity.SparkApiMode is null &&
            activity.SparkVersionPolicy is null && activity.SparkVersion is null && activity.SparkRemote is null && activity.Runtime is null)
            return;
        var runtime = activity.Runtime ?? settings?.GetValueOrDefault("runtime");
        var mode = activity.SparkApiMode ?? (runtime switch
        {
            "google-colab-connect-local" => "connect-local", "google-colab-connect-remote" => "connect-remote",
            _ => settings?.GetValueOrDefault("sparkApiMode")
        });
        try
        {
            ArchitecturePresets.ValidateSpark(new ArchitectureSettings
            {
                Runtime = runtime, SparkApiMode = mode ?? (settings is null && activity.SparkRemote is not null ? "connect-remote" : null),
                Storage = activity.Source ?? settings?.GetValueOrDefault("storage"),
                SparkVersionPolicy = activity.SparkVersionPolicy ?? settings?.GetValueOrDefault("sparkVersionPolicy"),
                SparkVersion = activity.SparkVersion ?? settings?.GetValueOrDefault("sparkVersion"),
                SparkRemote = activity.SparkRemote ?? settings?.GetValueOrDefault("sparkRemote")
            }, requireEndpoint: settings is not null);
        }
        catch (ArgumentException ex) { errors.Add($"Activity '{activity.Id}': {ex.Message}"); }
    }

    private static void CheckFormats(string? format, string? tableFormat, string owner, List<string> errors)
    {
        if (format is not null && format is not ("parquet" or "csv" or "json" or "jsonl" or "avro" or "orc"))
            errors.Add($"{owner} has unknown file format '{format}'.");
        if (tableFormat is not null && tableFormat is not ("none" or "delta" or "iceberg"))
            errors.Add($"{owner} has unknown table format '{tableFormat}'.");
        if (tableFormat is "delta" or "iceberg" && format is not null && format != "parquet")
            errors.Add($"{owner}: '{tableFormat}' requires parquet files.");
    }

    private static HashSet<string> Unique(IEnumerable<string> ids, string kind, List<string> errors)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            CheckId(id, kind, errors);
            if (!result.Add(id)) errors.Add($"Duplicate {kind} id '{id}'.");
        }
        return result;
    }

    private static void CheckId(string? id, string owner, List<string> errors)
    {
        if (id is null || !Identifier.IsMatch(id)) errors.Add($"Invalid {owner} id '{id}'; use letters, digits, underscores or hyphens (128 characters maximum).");
    }

    private static void CheckConnection(string? reference, string owner, HashSet<string> connections, List<string> errors)
    {
        if (reference is not null && !connections.Contains(reference)) errors.Add($"'{owner}' references missing connection '{reference}'.");
    }

    private static void CheckParameter(string name, PipelineParameter parameter, List<string> errors)
    {
        var value = parameter.Default;
        var missing = value.ValueKind == JsonValueKind.Undefined;
        var valid = parameter.Type switch
        {
            "string" => missing || value.ValueKind == JsonValueKind.String,
            "int" => missing || value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "float" => missing || value.ValueKind == JsonValueKind.Number,
            "bool" => missing || value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "array" => missing || value.ValueKind == JsonValueKind.Array,
            "object" => missing || value.ValueKind == JsonValueKind.Object,
            "secretReference" => missing || value.ValueKind == JsonValueKind.String && Regex.IsMatch(value.GetString()!, @"^(env|airflow|secret-manager|keyvault)://[^\s]+$"),
            _ => false
        };
        if (!valid) errors.Add($"Parameter '{name}' has unknown type or a default incompatible with type '{parameter.Type}'.");
        if (missing && !parameter.Required) errors.Add($"Parameter '{name}' requires a default or required=true.");
    }

    private static IEnumerable<string> Strings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) yield return element.GetString()!;
        else if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                foreach (var value in Strings(property.Value)) yield return value;
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var value in Strings(item)) yield return value;
    }

    private static void InspectJson(JsonElement element, string path, List<string> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!properties.Add(property.Name)) errors.Add($"Duplicate JSON property at {path}.{property.Name}.");
                var normalized = property.Name.Replace("_", "").Replace("-", "");
                if (CredentialKeys.Contains(normalized)) errors.Add($"Credential literal field '{path}.{property.Name}' is forbidden; use connectionRef/secretRef or a secretReference parameter.");
                InspectJson(property.Value, path + "." + property.Name, errors);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) InspectJson(item, path + "[]", errors);
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString()!;
            if (value.Contains("-----BEGIN PRIVATE KEY", StringComparison.Ordinal) ||
                Regex.IsMatch(value, @"(?i)(password|pwd|accountkey|sharedaccesssignature)\s*=") ||
                Regex.IsMatch(value, @"^[a-zA-Z][a-zA-Z0-9+.-]*://[^/\s]+:[^/\s]+@"))
                errors.Add($"Credential literal at '{path}' is forbidden; use a secret reference.");
        }
    }
}
