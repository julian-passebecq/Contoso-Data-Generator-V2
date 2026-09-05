#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DatabaseGenerator.Forge.Planning;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WranglingRecipe
{
    public int RecipeVersion { get; set; } = 1;
    public int MaxRows { get; set; } = 100000;
    public List<WranglingStep> Steps { get; set; } = new();
    internal static void Name(string? name)
    {
        if (name is null || !Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z")) throw new ArgumentException("Recipe names must be safe identifiers, never paths or expressions.");
    }
    public void Validate()
    {
        if (RecipeVersion != 1 || MaxRows is < 1 or > 1000000 || Steps is null || Steps.Count is < 1 or > 100 || Steps.Any(s => s is null)) throw new ArgumentException("Recipe requires version 1, 1..100 steps and maxRows 1..1000000.");
        var ids = new HashSet<string>();
        foreach (var s in Steps)
        {
            Name(s.Id);
            if (!ids.Add(s.Id)) throw new ArgumentException("Duplicate recipe step.");
            if (s.Op is not ("source" or "select" or "rename" or "cast" or "derive" or "filter" or "join" or "lookup" or "conditional-split" or "aggregate" or "deduplicate" or "window" or "sink")) throw new ArgumentException("Unsupported recipe operator: " + s.Op);
            if (s.Op == "source") Name(s.Entity); else Name(s.Input);
            if (s.Op is "join" or "lookup") { Name(s.Right); if (s.How is not ("inner" or "left")) throw new ArgumentException("Only inner/left recipe joins are supported."); }
            foreach (var name in (s.Columns ?? new()).Concat(s.GroupBy ?? new()).Concat(s.OrderBy ?? new()).Concat(s.On ?? new())) Name(name);
            if (s.Renames is not null) foreach (var pair in s.Renames) { Name(pair.Key); Name(pair.Value); }
            if (s.Casts is not null) foreach (var pair in s.Casts) { Name(pair.Key); if (pair.Value is not ("int64" or "float64" or "string" or "boolean")) throw new ArgumentException("Unsupported recipe cast."); }
            if (s.Op is "derive" or "window") Name(s.Name);
            if (s.Op == "window" && s.Function is not ("row_number" or "sum")) throw new ArgumentException("Unsupported window function.");
            if (s.Field is not null) Name(s.Field);
            s.Expression?.Validate(); s.Predicate?.Validate();
            if (s.Op == "derive" && s.Expression is null || s.Op is "filter" or "conditional-split" && s.Predicate is null) throw new ArgumentException("Recipe expression or predicate is required.");
            if (s.Measures is not null) foreach (var m in s.Measures) { Name(m.Name); if (m.Function is not ("count" or "sum" or "mean" or "min" or "max")) throw new ArgumentException("Unsupported aggregate."); if (m.Function != "count") Name(m.Field); }
        }
        var done = new HashSet<string>();
        while (done.Count < ids.Count)
        {
            var ready = Steps.Where(s => !done.Contains(s.Id) && new[] { s.Input, s.Right }.Where(i => i is not null).All(i => done.Contains(i!))).ToList();
            if (ready.Count == 0) throw new ArgumentException("Recipe cycle or missing input.");
            foreach (var step in ready) done.Add(step.Id);
        }
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WranglingStep
{
    public string Id { get; set; } = "";
    public string Op { get; set; } = "";
    public string? Entity { get; set; }
    public string? Input { get; set; }
    public string? Right { get; set; }
    public List<string>? Columns { get; set; }
    public Dictionary<string, string>? Renames { get; set; }
    public Dictionary<string, string>? Casts { get; set; }
    public string? Name { get; set; }
    public string? Field { get; set; }
    public string? Function { get; set; }
    public RecipeExpression? Expression { get; set; }
    public RecipeExpression? Predicate { get; set; }
    public List<string>? On { get; set; }
    public string How { get; set; } = "inner";
    public bool Matched { get; set; } = true;
    public List<string>? GroupBy { get; set; }
    public List<string>? OrderBy { get; set; }
    public List<RecipeMeasure>? Measures { get; set; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecipeMeasure
{
    public string Name { get; set; } = "";
    public string Function { get; set; } = "count";
    public string? Field { get; set; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RecipeExpression
{
    public string? Field { get; set; }
    public JsonElement? Value { get; set; }
    public string? Operator { get; set; }
    public List<RecipeExpression>? Args { get; set; }
    internal void Validate(int depth = 0)
    {
        if (depth > 12) throw new ArgumentException("Recipe expression depth exceeds 12.");
        if (Field is not null) WranglingRecipe.Name(Field);
        if (Value is { } v && (v.ValueKind is JsonValueKind.Array or JsonValueKind.Object || v.ToString().Length > 1024)) throw new ArgumentException("Recipe literals must be bounded scalar values.");
        if (Operator is not null && Operator is not ("+" or "-" or "*" or "/" or "==" or "!=" or ">" or ">=" or "<" or "<=" or "and" or "or" or "not" or "is-null" or "coalesce")) throw new ArgumentException("Unsupported recipe expression operator.");
        if (Operator is null && (Field is null) == (Value is null)) throw new ArgumentException("Recipe expression requires one field or literal.");
        if (Operator is not null && (Args is null || Args.Count != (Operator is "not" or "is-null" ? 1 : 2))) throw new ArgumentException("Recipe operator arity mismatch.");
        if (Args is not null) foreach (var arg in Args) { if (arg is null) throw new ArgumentException("Null expression."); arg.Validate(depth + 1); }
    }
}
