using DatabaseGenerator.Forge.Export;
using System.Text.Json.Nodes;

namespace DatabaseGenerator.Tests;

public sealed class FreeGcpInfrastructureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "forge-infra-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SandboxExport_IsAdditiveAndDoesNotProvisionPaidDependencies()
    {
        Directory.CreateDirectory(Path.Combine(root, "infra"));
        File.WriteAllText(Path.Combine(root, "infra", "README.md"), "validated V1 infrastructure");
        FreeGcpInfrastructureExporter.Export(root, Project(), "{}");
        Assert.Equal("validated V1 infrastructure", File.ReadAllText(Path.Combine(root, "infra", "README.md")));
        var variables = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "infra", "gcp", "forge.auto.tfvars.json")))!;
        Assert.False(variables["create_gcs_bucket"]!.GetValue<bool>());
        Assert.Equal("gcp-sandbox-no-card", variables["cost_profile"]!.GetValue<string>());
        Assert.Equal("generated-reference", JsonNode.Parse(File.ReadAllText(Path.Combine(root, "infra", "gcp", "validation_status.json")))!["status"]!.GetValue<string>());
        Assert.DoesNotContain(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories), path => path.EndsWith(".tfstate", StringComparison.Ordinal));
        var values = File.ReadAllText(Path.Combine(root, "minikube", "values.yaml"));
        Assert.Contains("executor: LocalExecutor", values);
        Assert.Contains("ref: \"my-branch\"", values);
        Assert.Contains("subPath: \"generated/airflow/dags\"", values);
        Assert.DoesNotContain("__GIT_", values);
    }

    [Fact]
    public void NoneIac_OmitsCloudInfrastructureButKeepsSelectedOrchestration()
    {
        FreeGcpInfrastructureExporter.Export(root, Project(iac: "none"), "{}");
        Assert.False(Directory.Exists(Path.Combine(root, "infra", "gcp")));
        Assert.True(File.Exists(Path.Combine(root, "minikube", "values.yaml")));
    }

    [Fact]
    public void MinikubeMigration_IsNotAPostInstallHookThatDeadlocksHelmWait()
    {
        FreeGcpInfrastructureExporter.Export(root, Project(), "{}");
        var values = File.ReadAllText(Path.Combine(root, "minikube", "values.yaml"));
        var migration = values.Split("migrateDatabaseJob:", StringSplitOptions.None)[1].Split("dags:", StringSplitOptions.None)[0];
        Assert.Contains("useHelmHooks: false", migration);
        var bootstrap = File.ReadAllText(Path.Combine(root, "minikube", "bootstrap_secrets.py"));
        Assert.Contains("--context", bootstrap);
        Assert.Contains("--kubectl", bootstrap);
    }

    [Fact]
    public void ReplacingGcpDestinationAndMinikube_DoesNotForceGcpArtifacts()
    {
        FreeGcpInfrastructureExporter.Export(root, Project(warehouse: "sqlserver", orchestrator: "local-sequential"), "{}");
        Assert.False(Directory.Exists(Path.Combine(root, "infra", "gcp")));
        Assert.False(Directory.Exists(Path.Combine(root, "minikube")));
    }

    [Fact]
    public void Sandbox_RejectsGcsBeforeWritingArtifacts()
    {
        var exception = Assert.Throws<ArgumentException>(() => FreeGcpInfrastructureExporter.Export(root, Project(storage: "gcs"), "{}"));
        Assert.Contains("billing", exception.Message);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void BillingEnabledGcs_ExportsBucketIntentAndExplicitIam()
    {
        FreeGcpInfrastructureExporter.Export(root, Project(storage: "gcs", cost: "gcp-free-tier-billing-enabled", iac: "dual-validate"), "{}");
        var variables = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "infra", "gcp", "forge.auto.tfvars.json")))!;
        Assert.True(variables["create_gcs_bucket"]!.GetValue<bool>());
        Assert.Equal("my-example-bucket", variables["bucket_name"]!.GetValue<string>());
        Assert.Equal("user:learner@example.com", variables["dataset_iam_members"]![0]!.GetValue<string>());
    }

    [Theory]
    [InlineData("https://user:secret@github.com/example/repo.git", "main", "dags")]
    [InlineData("https://github.com/example/repo.git?token=secret", "main", "dags")]
    [InlineData("https://github.com/example/repo.git", "{{ readFile \"secret\" }}", "dags")]
    [InlineData("https://github.com/example/repo.git", "main", "../dags")]
    public void GitSync_RejectsEmbeddedCredentialsTemplatesAndTraversal(string repo, string branch, string subPath)
    {
        var project = JsonNode.Parse(Project())!;
        project["git"]!["repository"] = repo;
        project["git"]!["branch"] = branch;
        project["git"]!["subPath"] = subPath;
        Assert.Throws<ArgumentException>(() => FreeGcpInfrastructureExporter.Export(root, project.ToJsonString(), "{}"));
        Assert.False(Directory.Exists(root));
    }

    private static string Project(string iac = "opentofu", string storage = "local", string cost = "gcp-sandbox-no-card", string warehouse = "bigquery", string orchestrator = "airflow-minikube") => new JsonObject
    {
        ["contractVersion"] = "1.2",
        ["settings"] = new JsonObject { ["iac"] = iac, ["storage"] = storage, ["costProfile"] = cost, ["warehouse"] = warehouse, ["orchestrator"] = orchestrator },
        ["gcp"] = new JsonObject { ["projectId"] = "your-gcp-project", ["dataset"] = "contoso_forge", ["location"] = "US", ["bucketName"] = "my-example-bucket", ["iamMembers"] = new JsonArray("user:learner@example.com") },
        ["git"] = new JsonObject { ["repository"] = "https://github.com/example/repo.git", ["branch"] = "my-branch", ["subPath"] = "generated/airflow/dags" }
    }.ToJsonString();

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
