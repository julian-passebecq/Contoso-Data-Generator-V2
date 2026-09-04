#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DatabaseGenerator.Forge.Pipeline;

/// <summary>Shared JSON boundary for editors of the existing neutral pipeline contract.</summary>
public static class PipelineDocument
{
    public static PipelineDefinition Read(string json)
    {
        var errors = new List<string>();
        var definition = PipelineValidation.Parse(json, errors);
        if (definition is null || errors.Count > 0)
            throw new ArgumentException("Invalid pipeline JSON: " + string.Join(" ", errors));
        // Editors may open incomplete graphs, but their collection/member shape
        // must be safe to render before semantic compiler validation is requested.
        if (definition.Parameters is null || definition.Variables is null || definition.Connections is null ||
            definition.Datasets is null || definition.Activities is null || definition.Edges is null || definition.Annotations is null)
            throw new ArgumentException("Invalid pipeline JSON: parameters, variables, connections, datasets, activities, edges and annotations must not be null.");
        foreach (var parameter in definition.Parameters.Values)
            if (parameter is null) throw new ArgumentException("Invalid pipeline JSON: parameter definitions must not be null.");
        foreach (var connection in definition.Connections)
            if (connection is null || connection.NonSecretProperties is null)
                throw new ArgumentException("Invalid pipeline JSON: connection references and their nonSecretProperties must not be null.");
        foreach (var dataset in definition.Datasets)
            if (dataset is null || dataset.Partitioning is null || dataset.Options is null)
                throw new ArgumentException("Invalid pipeline JSON: datasets and their partitioning/options must not be null.");
        foreach (var activity in definition.Activities)
            if (activity is null || activity.Id is null || activity.Kind is null || activity.Retry is null || activity.Inputs is null || activity.Outputs is null || activity.Parameters is null || activity.DependsOn is null)
                throw new ArgumentException("Invalid pipeline JSON: activities and their retry/inputs/outputs/parameters/dependsOn must not be null.");
            else if (activity.DependsOn.Exists(id => id is null) || activity.Inputs.Exists(id => id is null) || activity.Outputs.Exists(id => id is null))
                throw new ArgumentException("Invalid pipeline JSON: activity dataset and dependency references must not be null.");
        foreach (var edge in definition.Edges)
            if (edge is null || edge.From is null || edge.To is null)
                throw new ArgumentException("Invalid pipeline JSON: dependency edges and their from/to references must not be null.");
        return definition;
    }

    // Editors may serialize an incomplete graph; explicit compiler validation supplies diagnostics.
    public static string Write(PipelineDefinition definition) =>
        JsonSerializer.Serialize(definition, PipelineJsonContext.Default.PipelineDefinition) + "\n";
}
