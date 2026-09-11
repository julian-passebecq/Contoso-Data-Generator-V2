using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ContosoForge.PipelineStudio;
using Xunit;

namespace DatabaseGenerator.Tests;

public sealed class StudioRunTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "forge-s001-" + Guid.NewGuid().ToString("N"));
    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private void Write(string path, string content) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!); File.WriteAllText(Path.Combine(root, path), content); }
    private void Json(string path, JsonObject value) => Write(path, value.ToJsonString());
    private RunSelection Fixture()
    {
        Write("data/source/a.csv", "id\n1\n"); Write("models/source_model.json", "{}");
        Write("project.json", "{\"product\":{\"version\":\"1.7\"}}");
        Json("truth_manifest.json", new JsonObject { ["datasetFingerprint"] = "fixture", ["sourceFileSha256"] = new JsonObject { ["a.csv"] = Digest(Path.Combine(root, "data/source/a.csv")) } });
        Json("run_manifest.json", new JsonObject { ["files"] = new JsonObject { ["models/source_model.json"] = Digest(Path.Combine(root, "models/source_model.json")) } });
        var identity = new JsonObject { ["datasetFingerprint"] = "fixture" };
        foreach (var (key, file) in new[] { ("truthSha256", "truth_manifest.json"), ("projectSha256", "project.json"), ("compiledManifestSha256", "run_manifest.json"), ("sourceModelSha256", "models/source_model.json") }) identity[key] = Digest(Path.Combine(root, file));
        const string state = ".forge/v15/test/";
        Json(state + "run_evidence.json", new JsonObject { ["contractVersion"] = "1.7", ["runId"] = "test", ["identity"] = identity, ["status"] = "succeeded", ["stages"] = new JsonObject() });
        var hashes = new JsonObject(); var files = new JsonObject();
        foreach (var (name, source) in new[] { ("kpi_catalog.json", "models/kpi_catalog.json"), ("semantic_model.json", "models/semantic_model.json"), ("lineage.json", "models/lineage.json"), ("pipeline_evidence.json", state + "bi/upstream_evidence.json"), ("reconciliation.json", state + "reconciliation.json"), ("manifest.json", state + "dbt/target/manifest.json"), ("run_results.json", state + "dbt/target/run_results.json") })
        {
            var contents = name == "pipeline_evidence.json" ? File.ReadAllText(Path.Combine(root, state + "run_evidence.json")) : "{}";
            Write(source, contents); Write(state + "bi/evidence/static/contracts/" + name, contents);
            hashes[name] = Digest(Path.Combine(root, source)); files["static/contracts/" + name] = Digest(Path.Combine(root, source));
        }
        Write(state + "bi/evidence/pages/index.md", "Governed results");
        files["pages/index.md"] = Digest(Path.Combine(root, state + "bi/evidence/pages/index.md"));
        Json(state + "bi/report_contract.json", new JsonObject { ["runId"] = "test", ["status"] = "package-generated", ["inputHashes"] = hashes, ["reportFileHashes"] = files });
        Write(state + "bi/evidence/build/index.html", "<h1>Governed results</h1>");
        Json(state + "bi/build_evidence.json", new JsonObject { ["runId"] = "test", ["status"] = "built", ["artifact"] = "bi/evidence/build/index.html", ["sha256"] = Digest(Path.Combine(root, state + "bi/evidence/build/index.html")), ["reportContractSha256"] = Digest(Path.Combine(root, state + "bi/report_contract.json")) });
        return new RunSelection(root, "test");
    }
    [Theory]
    [InlineData("index")][InlineData("runId")][InlineData("contract")][InlineData("input")][InlineData("source")][InlineData("path")][InlineData("version")][InlineData("malformed")]
    public void PreviewRejectsTamperingAfterSuccess(string fault)
    {
        var run = Fixture(); Assert.True(File.Exists(RunEvidence.ValidatePreview(run)));
        var receiptPath = Path.Combine(run.State, "bi/build_evidence.json");
        var receipt = JsonNode.Parse(File.ReadAllText(receiptPath))!.AsObject();
        switch (fault)
        {
            case "index": File.AppendAllText(Path.Combine(run.State, "bi/evidence/build/index.html"), "tampered"); break;
            case "runId": receipt["runId"] = "other"; File.WriteAllText(receiptPath, receipt.ToJsonString()); break;
            case "contract": receipt["reportContractSha256"] = new string('0', 64); File.WriteAllText(receiptPath, receipt.ToJsonString()); break;
            case "path": receipt["artifact"] = "../../outside.html"; File.WriteAllText(receiptPath, receipt.ToJsonString()); break;
            case "input": File.AppendAllText(Path.Combine(run.State, "reconciliation.json"), " "); break;
            case "source": File.AppendAllText(Path.Combine(root, "data/source/a.csv"), "2\n"); break;
            case "version": Write("project.json", "{\"product\":{\"version\":\"9\"}}"); break;
            case "malformed": File.WriteAllText(receiptPath, "{"); break;
        }
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, Digest);
        Assert.ThrowsAny<Exception>(() => RunEvidence.ValidatePreview(run));
        Assert.All(before, p => Assert.Equal(p.Value, Digest(p.Key)));
    }
    [Theory][InlineData("failed")][InlineData("running")][InlineData("exported-not-executed")]
    public void InspectPreservesRecordedOutcomes(string status)
    {
        var run = Fixture(); var path = Path.Combine(run.State, "run_evidence.json"); var data = RunEvidence.Read(path); data["status"] = status; File.WriteAllText(path, data.ToJsonString());
        Assert.Equal(status, RunEvidence.Text(RunEvidence.Inspect(run), "status"));
        Assert.False(run.IsCurrent("new editor"));
    }
    [Fact]
    public async Task CatalogRoundTripConcurrentWritersAndConflictingIds()
    {
        var path = Path.Combine(root, "catalog.json");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => new RunCatalog(path).Register(new CatalogEntry(Path.Combine(root, "run" + i), "same-id", "label", "time"))));
        var catalog = new RunCatalog(path); Assert.Equal(12, catalog.List().Count);
        await catalog.Register(catalog.List()[0]); Assert.Equal(12, new RunCatalog(path).List().Count);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }
    [Fact]
    public async Task CorruptCatalogIsNeverOverwritten()
    {
        Write("catalog.json", "{\"version\":1,"); var path = Path.Combine(root, "catalog.json"); var before = Digest(path);
        await Assert.ThrowsAnyAsync<Exception>(() => new RunCatalog(path).Register(new CatalogEntry(root, "test", "x", "time")));
        Assert.Equal(before, Digest(path)); Assert.Empty(new RunCatalog(Path.Combine(root, "missing.json")).List());
    }
    [Fact]
    public async Task InterruptedTemporaryWriteLeavesCommittedIndexReadable()
    {
        var path = Path.Combine(root, "catalog.json"); var catalog = new RunCatalog(path);
        await catalog.Register(new CatalogEntry(root, "first", "label", "time"));
        File.WriteAllText(path + ".interrupted.tmp", "{\"version\":1,");
        Assert.Single(new RunCatalog(path).List());
        await new RunCatalog(path).Register(new CatalogEntry(root, "second", "label", "time"));
        Assert.Equal(2, new RunCatalog(path).List().Count);
    }
    [Fact]
    public async Task CatalogWaitsForBriefExternalReadLockWithoutLosingEntries()
    {
        var path = Path.Combine(root, "catalog.json"); var catalog = new RunCatalog(path);
        await catalog.Register(new CatalogEntry(root, "first", "label", "time"));
        var busy = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var write = new RunCatalog(path).Register(new CatalogEntry(root, "second", "label", "time"));
        await Task.Delay(150); busy.Dispose();
        await write;
        Assert.Equal(2, new RunCatalog(path).List().Count);
    }
    [Theory][InlineData("../outside")][InlineData("C:/outside")][InlineData("/outside")][InlineData("data/../../outside")]
    public void RejectsEscapingPaths(string path) => Assert.Throws<InvalidDataException>(() => RunEvidence.Within(root, path));
    [Fact]
    public void MovedRunRetainsContentIdentityAndMissingOldRootFails()
    {
        var run = Fixture(); var moved = root + "-moved";
        MoveFixture(root, moved);
        try
        {
            Assert.ThrowsAny<IOException>(() => RunEvidence.Inspect(run));
            Assert.True(File.Exists(RunEvidence.ValidatePreview(new RunSelection(moved, run.RunId))));
        }
        finally { MoveFixture(moved, root); }
    }
    [Fact]
    public void WrongRunDirectoryAndMissingPartialReceiptFail()
    {
        var run = Fixture(); var path = Path.Combine(run.State, "run_evidence.json");
        var evidence = RunEvidence.Read(path); evidence["runId"] = "different"; File.WriteAllText(path, evidence.ToJsonString());
        Assert.Throws<InvalidDataException>(() => RunEvidence.Inspect(run));
        File.Delete(path); Assert.Throws<FileNotFoundException>(() => RunEvidence.Inspect(run));
    }
    [Fact]
    public void ManifestTraversalIsRejectedBeforeReadingOutsideBundle()
    {
        var run = Fixture(); var manifest = Path.Combine(root, "run_manifest.json");
        File.WriteAllText(manifest, "{\"files\":{\"../outside\":\"bad\"}}");
        var evidence = RunEvidence.Read(Path.Combine(run.State, "run_evidence.json"));
        evidence["identity"]!["compiledManifestSha256"] = Digest(manifest);
        File.WriteAllText(Path.Combine(run.State, "run_evidence.json"), evidence.ToJsonString());
        Assert.Throws<InvalidDataException>(() => RunEvidence.Inspect(run));
    }
    [Fact]
    public void ImportedScriptsAreNeverExecuted()
    {
        var run = Fixture(); Write("factory/run.py", "raise RuntimeError('MUST NEVER EXECUTE')");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, Digest);
        RunEvidence.Inspect(run); RunEvidence.ValidatePreview(run);
        Assert.All(before, p => Assert.Equal(p.Value, Digest(p.Key)));
        Assert.Equal(before.Count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
    }
    [Fact]
    public void PythonRunDirectoryConventionIsPreserved()
    {
        Assert.Equal("run.1-test", RunEvidence.StateDirectory("run.1-test"));
        var id = "a run with spaces";
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant(), RunEvidence.StateDirectory(id));
        Assert.Equal(64, RunEvidence.StateDirectory(string.Concat(Enumerable.Repeat("😀", 200))).Length);
        Assert.Throws<InvalidDataException>(() => RunEvidence.StateDirectory(new string('x', 251)));
    }
    [Theory][InlineData("1.5")][InlineData("1.6")]
    public void OwnedLegacyPreviewPreservesSnapshotContractButImportRemainsV17Only(string version)
    {
        var run = Fixture();
        Write("project.json", new JsonObject { ["product"] = new JsonObject { ["version"] = version } }.ToJsonString());
        var path = Path.Combine(run.State, "run_evidence.json"); var evidence = RunEvidence.Read(path);
        evidence["contractVersion"] = "1.5"; evidence["identity"]!["projectSha256"] = Digest(Path.Combine(root, "project.json"));
        File.WriteAllText(path, evidence.ToJsonString());
        var contractPath = Path.Combine(run.State, "bi/report_contract.json"); var contract = RunEvidence.Read(contractPath);
        var hashes = contract["inputHashes"]!.AsObject(); hashes.Remove("lineage.json");
        var files = contract["reportFileHashes"]!.AsObject();
        Write("factory/product_design.json", "{}");
        foreach (var (name, source) in new[] { ("truth_manifest.json", Path.Combine(root, "truth_manifest.json")), ("product_design.json", Path.Combine(root, "factory/product_design.json")), ("pipeline_evidence.json", path) })
        {
            var target = Path.Combine(run.State, "bi/evidence/static/contracts/" + name);
            File.Copy(source, target, true); hashes[name] = Digest(target); files["static/contracts/" + name] = Digest(target);
        }
        Write(".forge/v15/test/lake/gold/a.parquet", "gold fixture");
        contract["goldHashes"] = new JsonObject { ["a.parquet"] = Digest(Path.Combine(run.State, "lake/gold/a.parquet")) };
        File.WriteAllText(contractPath, contract.ToJsonString());
        var receiptPath = Path.Combine(run.State, "bi/build_evidence.json"); var receipt = RunEvidence.Read(receiptPath);
        receipt["reportContractSha256"] = Digest(contractPath); File.WriteAllText(receiptPath, receipt.ToJsonString());
        Assert.True(File.Exists(RunEvidence.ValidatePreview(run with { EditorIdentity = "owned", OwnedVersion = version })));
        Assert.Throws<InvalidDataException>(() => RunEvidence.ValidatePreview(run));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void SymlinkCannotRedirectManifestOrPreview(bool unrecordedAsset)
    {
        var run = Fixture(); var path = Path.GetFullPath(Path.Combine(run.State, unrecordedAsset ? "bi/evidence/build/assets" : "bi/evidence/build"));
        if (unrecordedAsset) Directory.CreateDirectory(path);
        var target = Path.Combine(root, "external-build"); Directory.Move(path, target);
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            Assert.True(process.WaitForExit(5000)); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(path, target);
        try { Assert.Throws<InvalidDataException>(() => RunEvidence.ValidatePreview(run)); }
        finally { Directory.Delete(path); Directory.Move(target, path); }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static void MoveFixture(string source, string destination)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (IOException) when (attempt < 19) { Thread.Sleep(50); }
        }
    }
}
