#nullable enable
using DatabaseGenerator.Forge.Planning;
using System.Text;
using System.Text.Json;

namespace DatabaseGenerator.Forge;

/// <summary>Text presentation of the same versioned contract written by --output.</summary>
public static class ForgePlanPresentation
{
    public static string Describe(ResolvedPlan plan)
    {
        using var json = JsonDocument.Parse(PlanBuilder.ToJson(plan));
        var root = json.RootElement;
        var text = new StringBuilder();
        text.AppendLine($"Plan: {Value(root, "projectName")}");
        text.AppendLine($"Business scenario: {plan.BusinessScenario.DisplayName} ({plan.BusinessScenario.Id})");
        text.AppendLine($"Architecture: {Value(root, "architecturePreset")}");
        text.AppendLine($"Readiness: {Value(root, "overallReadiness")}");
        text.AppendLine("This plan has not executed the project. Evidence describes its stated scope.");
        text.AppendLine();
        foreach (var setting in root.GetProperty("resolvedSettings").EnumerateObject())
            text.AppendLine($"  {setting.Name}: {setting.Value}");
        text.AppendLine();
        foreach (var stage in root.GetProperty("stages").EnumerateArray())
            text.AppendLine($"  {Value(stage, "id")}: {Value(stage, "kind")} | {Value(stage, "engine")} / {Value(stage, "runtime")} | {Value(stage, "implementationStatus")} / {Value(stage, "validationLevel")}" +
                (stage.TryGetProperty("manual", out var manual) && manual.ValueKind == JsonValueKind.True ? " | MANUAL" : ""));
        foreach (var section in new[] { "manualCheckpoints", "requiredCredentials", "costAndQuotaNotes", "warnings" })
        {
            if (!root.TryGetProperty(section, out var items) || items.GetArrayLength() == 0) continue;
            text.AppendLine();
            text.AppendLine(section + ":");
            foreach (var item in items.EnumerateArray()) text.AppendLine("  " + item.ToString());
        }
        return text.ToString();
    }

    private static string Value(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) ? value.ToString() : "";
}
