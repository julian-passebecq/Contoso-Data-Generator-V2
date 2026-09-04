using DatabaseGenerator.Forge;
using DatabaseGenerator.Forge.Export;
using System.Text.RegularExpressions;

namespace DatabaseGenerator.Tests;

public class BigQueryAnalyticsTests
{
    [Fact]
    public async Task BigQueryGenerationKeepsDuckDbAndAddsRunScopedGoldAndExplicitMl()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-analytics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = Path.Combine(root, "input");
            var output = Path.Combine(root, "generated");
            Assert.Equal(0, await ForgeCommand.RunAsync(new[] { "project", "init", "--output", input }));
            Assert.Equal(0, await ForgeCommand.RunAsync(new[] { "generate", "--project", Path.Combine(input, "project.json"), "--output", output }));
            Assert.Contains("read_json_auto", File.ReadAllText(Path.Combine(output, "dbt/tests/reconcile_kpis_with_truth_manifest.sql")));
            Assert.Equal(24, Directory.GetFiles(Path.Combine(output, "dbt_bigquery/models"), "*.sql", SearchOption.AllDirectories).Length);
            Assert.Contains("FORGE_BQ_PREFIX", File.ReadAllText(Path.Combine(output, "dbt_bigquery/models/sources.yml")));
            var truthTest = File.ReadAllText(Path.Combine(output, "dbt_bigquery/tests/reconcile_truth.sql"));
            foreach (var metric in new[] { "order_count", "gross_sales_amount", "on_time_delivery_rate", "return_rate", "average_review_rating" })
                Assert.Contains($"'{metric}' AS metric", truthTest);
            Assert.Contains("--allow-training-cost", File.ReadAllText(Path.Combine(output, "bqml/run_bqml.py")));
            Assert.DoesNotContain("run_bqml", File.ReadAllText(Path.Combine(output, "local_plan.json")));
            var stagingTests = File.ReadAllText(Path.Combine(output, "dbt_bigquery/models/staging/schema.yml"));
            // GoogleSQL rejects comparing INT64 columns to dbt's default quoted
            // STRING accepted values. Both numeric checks must retain all five
            // values and put quote:false inside arguments at the same indentation.
            Assert.Equal(2, Regex.Matches(stagingTests,
                @"(?m)^              arguments:\r?\n                values: \[1, 2, 3, 4, 5\]\r?\n                quote: false\r?$").Count);
            Assert.DoesNotContain("quote: false", File.ReadAllText(Path.Combine(output, "dbt/models/staging/schema.yml")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void OtherWarehousesDoNotReceiveBigQueryAnalytics()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-analytics-other-" + Guid.NewGuid().ToString("N"));
        BigQueryAnalyticsExporter.Export(root, "{\"settings\":{\"warehouse\":\"sqlserver\"}}");
        Assert.False(Directory.Exists(root));
    }
}
