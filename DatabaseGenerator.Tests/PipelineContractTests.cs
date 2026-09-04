using DatabaseGenerator.Forge.Pipeline;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DatabaseGenerator.Tests;

public sealed class PipelineContractTests
{
    private const string Resolved = """
        {"contractVersion":"1.2","presetId":"free-gcp-lab","settings":{
        "engine":"spark","runtime":"google-colab","orchestrator":"airflow-minikube",
        "dagSource":"github-gitsync","storage":"local","fileFormat":"parquet",
        "tableFormat":"none","warehouse":"bigquery","iac":"opentofu","costProfile":"gcp-sandbox-no-card"}}
        """;

    private const string Independent = """
        {"contractVersion":"1.2","id":"portable","name":"Portable",
        "activities":[{"id":"z","kind":"source"},{"id":"a","kind":"source"},{"id":"last","kind":"validate","dependsOn":["z"]}],
        "edges":[{"from":"a","to":"last"}]}
        """;

    [Fact]
    public void DefaultContract_ValidatesAndExportsAnExplicitManualBoundary()
    {
        var json = PipelineCompiler.CreateDefault(Resolved);
        Assert.Empty(PipelineCompiler.Validate(json));
        WithTemp(root =>
        {
            var result = PipelineCompiler.Compile(json, Resolved, root);
            Assert.Equal(new[] { "verify_source", "prepare_colab", "await_result", "reconcile" }, result.TopologicalOrder);
            Assert.Equal("manual-checkpoint", result.Plan.ArtifactStatus);
            Assert.Equal("await-colab", result.Plan.Activities[2].Operation);
            var dag = File.ReadAllText(Path.Combine(root, "airflow/dags/contoso_forge_pipeline.py"));
            Assert.Contains("from airflow.sdk import DAG", dag);
            Assert.Contains("from airflow.providers.standard.sensors.python import PythonSensor", dag);
            Assert.Contains("soft_fail=False", dag);
            Assert.Contains("mode=\"reschedule\"", dag);
            Assert.DoesNotContain("EmptyOperator", dag);
            var runtime = File.ReadAllText(Path.Combine(root, "pipeline/forge_pipeline_runtime.py"));
            Assert.Contains("check=True", runtime);
            Assert.Contains("except ManualCheckpointPending", runtime);
            Assert.Contains("hashlib.sha256((pipeline_id", runtime);
            Assert.Contains("--work-order", runtime);
            Assert.True(File.Exists(Path.Combine(root, "pipeline/graph.mmd")));
            Assert.Empty(PipelineCompiler.Validate(File.ReadAllText(Path.Combine(root, "pipeline.json"))));
        });
    }

    [Fact]
    public void StableTopologicalOrder_RespectsEdgesAndDependencies()
    {
        WithTemp(root =>
        {
            var result = PipelineCompiler.Compile(Independent, Resolved, root);
            Assert.Equal(new[] { "a", "z", "last" }, result.TopologicalOrder);
            Assert.Equal(new[] { "a", "z" }, result.Plan.Activities.Last().DependsOn);
            var first = File.ReadAllText(Path.Combine(root, "pipeline.json"));
            var plan = File.ReadAllText(Path.Combine(root, "local_plan.json"));
            PipelineCompiler.Compile(first, Resolved, root);
            Assert.Equal(first, File.ReadAllText(Path.Combine(root, "pipeline.json")));
            Assert.Equal(plan, File.ReadAllText(Path.Combine(root, "local_plan.json")));
        });
    }

    [Theory]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source"},{"id":"a","kind":"source"}]}""", "Duplicate activity")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source","dependsOn":["a"]}]}""", "cycle")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source","dependsOn":["b"]},{"id":"b","kind":"source","dependsOn":["a"]}]}""", "cycle")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source","dependsOn":["absent"]}]}""", "dangling")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source"}],"edges":[{"from":"a","to":"absent"}]}""", "dangling")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"mystery"}]}""", "unknown kind")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"../../x","kind":"source"}]}""", "Invalid activity id")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source","inputs":["absent"]}]}""", "missing dataset")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source","connectionRef":"absent"}]}""", "missing connection")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"spark","engine":"polars","runtime":"google-colab"}]}""", "requires engine")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"spark","fileFormat":"csv","tableFormat":"delta"}]}""", "requires parquet")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source","table":"${parameters.missing}"}]}""", "Unresolved")]
    [InlineData("""{"contractVersion":"1.2","name":"x","parameters":{"rows":{"type":"int","default":"100"}},"activities":[{"id":"a","kind":"source"}]}""", "incompatible")]
    [InlineData("""{"contractVersion":"1.2","name":"x","parameters":{"auth":{"type":"secretReference","default":"literal-password"}},"activities":[{"id":"a","kind":"source"}]}""", "incompatible")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source","retry":{"maximumAttempts":0}}]}""", "maximumAttempts")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source","timeoutSeconds":0}]}""", "timeoutSeconds")]
    [InlineData("""{"contractVersion":"1.2","name":"x","name":"y","activities":[{"id":"a","kind":"source"}]}""", "Duplicate JSON")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":null}""", "must not be null")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[null]}""", "must not be null")]
    [InlineData("""{"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source","unexpected":1}]}""", "Invalid pipeline JSON")]
    [InlineData("""{"name":"x","activities":[{"id":"a","kind":"source"}]}""", "contractVersion")]
    public void InvalidContracts_AreRejected(string json, string expected)
    {
        Assert.Contains(PipelineCompiler.Validate(json), error => error.Contains(expected, StringComparison.OrdinalIgnoreCase));
        WithTemp(root =>
        {
            Assert.Throws<ArgumentException>(() => PipelineCompiler.Compile(json, Resolved, root));
            Assert.False(File.Exists(Path.Combine(root, "pipeline.json")));
        });
    }

    [Theory]
    [InlineData("""{"password":"do-not-store"}""")]
    [InlineData("""{"client_secret":"do-not-store"}""")]
    [InlineData("""{"nested":{"private_key":"do-not-store"}}""")]
    [InlineData("""{"endpoint":"https://user:password@example.test"}""")]
    [InlineData("""{"endpoint":"Server=host;Password=literal"}""")]
    public void LiteralCredentials_AreRejectedRecursively(string properties)
    {
        var json = $$"""
            {"contractVersion":"1.2","name":"x","activities":[{"id":"a","kind":"source"}],
            "connections":[{"id":"remote","type":"custom","nonSecretProperties":{{properties}}}]}
            """;
        Assert.Contains(PipelineCompiler.Validate(json), e => e.Contains("Credential literal"));
    }

    [Fact]
    public void PortableConnectionDatasetAndTypedParameters_AreNotBigQuerySpecific()
    {
        const string json = """
            {"contractVersion":"1.2","id":"azure_copy","name":"Portable ADLS example",
             "parameters":{"inputTable":{"type":"string","default":"dbo.Sales"},
               "rows":{"type":"int","default":100},"ratio":{"type":"float","default":0.5},
               "active":{"type":"bool","default":true},"tags":{"type":"array","default":["x"]},
               "options":{"type":"object","default":{"batchSize":500}},
               "auth":{"type":"secretReference","default":"env://AZURE_CREDENTIAL"}},
             "connections":[{"id":"sql","type":"sqlserver","secretProvider":"environment","secretRef":"SQL_CONNECTION"},
               {"id":"lake","type":"azure-adls","secretProvider":"keyvault","secretRef":"adls-account"}],
             "datasets":[{"id":"sales","connectionRef":"sql","table":"${parameters.inputTable}"},
               {"id":"silver","connectionRef":"lake","path":"container/silver","format":"parquet","tableFormat":"none"}],
             "activities":[{"id":"extract","kind":"source","connectionRef":"sql","outputs":["sales"]},
               {"id":"copy","kind":"copy","source":"sqlserver","sink":"azure-adls","engine":"spark","runtime":"docker",
                "inputs":["sales"],"outputs":["silver"],"dependsOn":["extract"]}]}
            """;
        Assert.Empty(PipelineCompiler.Validate(json));
        WithTemp(root =>
        {
            var result = PipelineCompiler.Compile(json, Resolved, root);
            Assert.All(result.Plan.Activities, a => Assert.Equal("unsupported", a.Status));
            Assert.Equal("azure-adls", result.Plan.Activities[1].Sink);
            Assert.Equal("docker", result.Plan.Activities[1].Runtime);
            Assert.Contains("keyvault", File.ReadAllText(Path.Combine(root, "pipeline.json")));
        });
    }

    [Fact]
    public void LegacyNodes_NormalizeToCanonicalActivities()
    {
        const string json = """{"contractVersion":"1.1","name":"legacy-studio","nodes":[{"id":"a","kind":"source"}]}""";
        Assert.Empty(PipelineCompiler.Validate(json));
        WithTemp(root =>
        {
            PipelineCompiler.Compile(json, Resolved, root);
            using var canonical = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "pipeline.json")));
            Assert.False(canonical.RootElement.TryGetProperty("nodes", out _));
            Assert.Equal("1.2", canonical.RootElement.GetProperty("contractVersion").GetString());
        });
    }

    [Fact]
    public void IncompatibleInheritedSettings_FailBeforeWriting()
    {
        var resolved = Resolved.Replace("\"spark\"", "\"polars\"");
        Assert.NotEmpty(PipelineCompiler.Validate(Independent, resolved));
        WithTemp(root => Assert.Throws<ArgumentException>(() => PipelineCompiler.Compile(Independent, resolved, root)));
    }

    [Fact]
    public void ActivityOverride_IsValidatedAgainstInheritedRuntimeBeforeCompilation()
    {
        var json = JsonNode.Parse(PipelineCompiler.CreateDefault(Resolved))!;
        json["activities"]![1]!["engine"] = "duckdb";
        Assert.Empty(PipelineCompiler.Validate(json.ToJsonString()));
        Assert.Contains(PipelineCompiler.Validate(json.ToJsonString(), Resolved), error => error.Contains("requires engine"));
    }

    [Theory]
    [InlineData("warehouse", "sqlserver")]
    [InlineData("storage", "azure-adls")]
    [InlineData("runtime", "docker")]
    public void ChangingResolvedSettings_ReusesNeutralContractAndStopsUnsupportedRuntime(string setting, string value)
    {
        var json = PipelineCompiler.CreateDefault(Resolved);
        var resolved = JsonNode.Parse(Resolved)!;
        resolved["settings"]![setting] = value;
        WithTemp(root =>
        {
            var result = PipelineCompiler.Compile(json, resolved.ToJsonString(), root);
            Assert.Equal("unsupported", result.Plan.ArtifactStatus);
            Assert.DoesNotContain(result.Plan.Activities, a => a.Operation is "prepare-colab" or "await-colab" or "reconcile-colab");
        });
    }

    [Fact]
    public void OrphanManualResult_DoesNotSilentlySucceed()
    {
        var json = JsonNode.Parse(PipelineCompiler.CreateDefault(Resolved))!;
        json["activities"]![2]!["dependsOn"] = new JsonArray("verify_source");
        WithTemp(root =>
        {
            var result = PipelineCompiler.Compile(json.ToJsonString(), Resolved, root);
            Assert.Equal("unsupported", result.Plan.Activities.Single(a => a.Id == "await_result").Operation);
        });
    }

    [Fact]
    public void CustomDatasetBindings_AreNeverSilentlyIgnored()
    {
        var json = JsonNode.Parse(PipelineCompiler.CreateDefault(Resolved))!;
        json["datasets"]![0]!["path"] = "some-other-data";
        WithTemp(root =>
        {
            var result = PipelineCompiler.Compile(json.ToJsonString(), Resolved, root);
            Assert.Equal("unsupported", result.Plan.Activities.Single(a => a.Id == "verify_source").Operation);
            Assert.Equal("unsupported", result.Plan.Activities.Single(a => a.Id == "prepare_colab").Operation);
        });
    }

    [Theory]
    [InlineData("airflow-minikube", true)]
    [InlineData("airflow-docker", false)]
    public void MinikubeDagDiscovery_IgnoresRetainedV1DockerDagOnly(string orchestrator, bool expectedIgnore)
    {
        var resolved = Resolved.Replace("airflow-minikube", orchestrator);
        WithTemp(root =>
        {
            PipelineCompiler.Compile(PipelineCompiler.CreateDefault(resolved), resolved, root);
            var ignore = Path.Combine(root, "airflow/dags/.airflowignore");
            Assert.Equal(expectedIgnore, File.Exists(ignore));
            if (expectedIgnore) Assert.Equal("contoso_forge_customer_satisfaction.py\n", File.ReadAllText(ignore));
        });
    }

    [Theory]
    [InlineData("parameters")]
    [InlineData("inputs")]
    [InlineData("outputs")]
    [InlineData("retry")]
    [InlineData("dependsOn")]
    public void NullActivityCollections_AreContractErrors(string property)
    {
        var json = JsonNode.Parse(PipelineCompiler.CreateDefault(Resolved))!;
        json["activities"]![0]![property] = null;
        Assert.NotEmpty(PipelineCompiler.Validate(json.ToJsonString()));
    }

    private static void WithTemp(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-pipeline-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
