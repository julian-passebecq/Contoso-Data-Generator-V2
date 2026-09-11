using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ContosoForge.PipelineStudio;

// Persisted evidence is data. This service never loads or invokes bundle code.
public sealed record RunSelection(string Root, string RunId, string? EditorIdentity = null, string? OwnedVersion = null)
{
    public string State => RunEvidence.Within(Root, $".forge/v15/{RunEvidence.StateDirectory(RunId)}");
    public bool IsCurrent(string identity) => EditorIdentity == identity;
}

public sealed record StudioExecutionContext(RunSelection Run, string Python, string ProjectJson, string PipelineJson);

public static class RunEvidence
{
    public static string StateDirectory(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.EnumerateRunes().Count() > 250) throw new InvalidDataException("Run ID must contain 1–250 characters");
        return System.Text.RegularExpressions.Regex.IsMatch(id, "\\A[A-Za-z0-9][A-Za-z0-9._-]{0,99}\\z")
            ? id : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    }
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    public static string EditorIdentity(string project, string graph) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project + "\n" + graph)));
    public static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("Expected JSON object: " + path);
    public static string Text(JsonObject value, string name) => value[name]?.GetValue<string>() ?? throw new InvalidDataException("Missing " + name);
    public static string Within(string root, string relative)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(p => p is ".." or ".") || relative.Contains(':'))
            throw new InvalidDataException("Evidence path escapes its bundle: " + relative);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid bundle path");
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if ((File.Exists(current.FullName) || Directory.Exists(current.FullName)) && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked evidence paths are unsupported: " + current.FullName);
        }
        return path;
    }
    private static void Equal(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal)) throw new InvalidDataException(label + " mismatch");
    }
    private static void CheckHash(string path, string digest) => Equal(digest, Hash(path), "Checksum for " + path);
    private static void HashMap(string root, JsonObject map)
    {
        if (map.Count == 0) throw new InvalidDataException("Empty evidence hash map");
        foreach (var pair in map) CheckHash(Within(root, pair.Key), pair.Value!.GetValue<string>());
    }
    public static JsonObject Inspect(RunSelection run) => InspectCore(run, "1.7");
    private static JsonObject InspectCore(RunSelection run, string version)
    {
        if (version is not ("1.5" or "1.6" or "1.7")) throw new InvalidDataException("Unsupported evidence version");
        var project = Read(Within(run.Root, "project.json"));
        Equal(version, project["product"]?["version"]?.GetValue<string>() ?? "unknown", "Supported project version");
        var evidence = Read(Within(run.State, "run_evidence.json"));
        Equal(version == "1.7" ? "1.7" : "1.5", Text(evidence, "contractVersion"), "Supported evidence version");
        Equal(run.RunId, Text(evidence, "runId"), "Run ID");
        var identity = evidence["identity"] as JsonObject ?? throw new InvalidDataException("Missing run identity");
        foreach (var (key, file) in new[] { ("truthSha256", "truth_manifest.json"), ("projectSha256", "project.json"), ("compiledManifestSha256", "run_manifest.json"), ("sourceModelSha256", "models/source_model.json") })
            if (version == "1.7" || key != "sourceModelSha256") CheckHash(Within(run.Root, file), Text(identity, key));
        var truth = Read(Within(run.Root, "truth_manifest.json"));
        Equal(Text(identity, "datasetFingerprint"), Text(truth, "datasetFingerprint"), "Dataset");
        HashMap(Within(run.Root, "data/source"), (JsonObject)truth["sourceFileSha256"]!);
        HashMap(run.Root, (JsonObject)Read(Within(run.Root, "run_manifest.json"))["files"]!);
        return evidence;
    }
    public static string ValidatePreview(RunSelection run)
    {
        var version = run.EditorIdentity is not null ? run.OwnedVersion ?? "1.7" : "1.7";
        InspectCore(run, version);
        var contractPath = Within(run.State, "bi/report_contract.json");
        var contract = Read(contractPath);
        var receipt = Read(Within(run.State, "bi/build_evidence.json"));
        Equal(run.RunId, Text(contract, "runId"), "Report run ID");
        Equal(run.RunId, Text(receipt, "runId"), "Build run ID");
        Equal("package-generated", Text(contract, "status"), "Report status");
        Equal("built", Text(receipt, "status"), "Build status");
        CheckHash(contractPath, Text(receipt, "reportContractSha256"));
        Equal("bi/evidence/build/index.html", Text(receipt, "artifact"), "Build artifact location");
        var index = Within(run.State, Text(receipt, "artifact"));
        CheckHash(index, Text(receipt, "sha256"));
        HashMap(Within(run.State, "bi/evidence"), (JsonObject)contract["reportFileHashes"]!);
        var inputs = new Dictionary<string, string> {
            ["kpi_catalog.json"] = Within(run.Root, "models/kpi_catalog.json"), ["semantic_model.json"] = Within(run.Root, "models/semantic_model.json"),
            ["lineage.json"] = Within(run.Root, "models/lineage.json"), ["pipeline_evidence.json"] = Within(run.State, "bi/upstream_evidence.json"),
            ["reconciliation.json"] = Within(run.State, "reconciliation.json"), ["manifest.json"] = Within(run.State, "dbt/target/manifest.json"),
            ["run_results.json"] = Within(run.State, "dbt/target/run_results.json"), ["metrics.json"] = Within(run.State, "ml/metrics.json") };
        if (version != "1.7")
        {
            inputs.Remove("lineage.json"); inputs.Remove("metrics.json");
            inputs["truth_manifest.json"] = Within(run.Root, "truth_manifest.json");
            inputs["product_design.json"] = Within(run.Root, "factory/product_design.json");
            inputs["ml_metrics.json"] = Within(run.State, "ml/metrics.json");
            // V1.5/V1.6 bind the copied pre-BI snapshot, not the subsequently completed run receipt.
            inputs["pipeline_evidence.json"] = Within(run.State, "bi/evidence/static/contracts/pipeline_evidence.json");
            HashMap(Within(run.State, "lake/gold"), (JsonObject)contract["goldHashes"]!);
        }
        var hashes = contract["inputHashes"] as JsonObject ?? throw new InvalidDataException("Missing input hashes");
        foreach (var name in inputs.Keys.Where(k => k is not ("metrics.json" or "ml_metrics.json")))
            if (!hashes.ContainsKey(name)) throw new InvalidDataException("Missing report input: " + name);
        foreach (var pair in hashes)
        {
            if (!inputs.TryGetValue(pair.Key, out var source)) throw new InvalidDataException("Unsupported report input: " + pair.Key);
            CheckHash(source, pair.Value!.GetValue<string>());
            CheckHash(Within(run.State, "bi/evidence/static/contracts/" + pair.Key), pair.Value!.GetValue<string>());
        }
        var snapshot = Read(inputs["pipeline_evidence.json"]);
        Equal(run.RunId, Text(snapshot, "runId"), "Upstream snapshot run ID");
        var currentIdentity = Read(Within(run.State, "run_evidence.json"))["identity"];
        if (!JsonNode.DeepEquals(currentIdentity, snapshot["identity"])) throw new InvalidDataException("Upstream snapshot identity mismatch");
        // http.server follows links. Reject links throughout the served tree, including unhashed assets.
        var pending = new Stack<string>(); pending.Push(Path.GetDirectoryName(index)!);
        while (pending.TryPop(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked preview assets are unsupported: " + path);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        return index;
    }
}
