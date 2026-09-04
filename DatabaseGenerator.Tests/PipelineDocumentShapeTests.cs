using System;
using System.Text.Json.Nodes;
using DatabaseGenerator.Forge.Pipeline;
using Xunit;

namespace DatabaseGenerator.Tests;

public sealed class PipelineDocumentShapeTests
{
    private static JsonNode EditableGraph() => JsonNode.Parse(PipelineDocument.Write(new PipelineDefinition
    {
        Activities = [new PipelineActivity { Id = "source", Kind = "source" }],
        Datasets = [new PipelineDataset { Id = "input", Path = "data/source" }],
        Connections = [new PipelineConnectionReference { Id = "local", Type = "local" }]
    }))!;

    [Theory]
    [InlineData("activities")]
    [InlineData("datasets")]
    [InlineData("connections")]
    [InlineData("parameters")]
    [InlineData("variables")]
    [InlineData("edges")]
    [InlineData("annotations")]
    public void EditorRejectsNullRootCollectionsBeforeRendering(string field)
    {
        var document = EditableGraph();
        document[field] = null;
        var error = Assert.Throws<ArgumentException>(() => PipelineDocument.Read(document.ToJsonString()));
        Assert.Contains("null", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("inputs")]
    [InlineData("outputs")]
    [InlineData("parameters")]
    [InlineData("dependsOn")]
    [InlineData("id")]
    [InlineData("kind")]
    public void EditorRejectsNullActivityMembersBeforeRendering(string field)
    {
        var document = EditableGraph();
        document["activities"]![0]![field] = null;
        var error = Assert.Throws<ArgumentException>(() => PipelineDocument.Read(document.ToJsonString()));
        Assert.Contains("null", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("activities")]
    [InlineData("datasets")]
    [InlineData("connections")]
    public void EditorRejectsNullArrayMembers(string field)
    {
        var document = EditableGraph();
        document[field]![0] = null;
        Assert.Throws<ArgumentException>(() => PipelineDocument.Read(document.ToJsonString()));
    }

    [Fact]
    public void EditorStillAcceptsIncompleteGraphForExplicitCompilerValidation()
    {
        var draft = new PipelineDefinition { Activities = [] };
        var document = PipelineDocument.Read(PipelineDocument.Write(draft));
        Assert.Empty(document.Activities);
        Assert.Contains(PipelineCompiler.Validate(PipelineDocument.Write(document)), error => error.Contains("at least one activity", StringComparison.Ordinal));
    }
}
