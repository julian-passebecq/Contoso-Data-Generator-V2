using ContosoForge.PipelineStudio;
using Xunit;

namespace DatabaseGenerator.Tests;

public class StudioRuntimeRequirementsTests
{
    [Fact]
    public void EngineAndAnalysisRequirementsAreIndependentAcrossStopBoundaries()
    {
        foreach (var engine in new[] { "duckdb", "polars", "pandas", "spark" })
        foreach (var kind in new[] { "none", "sklearn", "spark-ml", "mljar" })
        foreach (var runtime in new[] { "local", "colab", "kaggle", "databricks-export" })
        foreach (var included in new[] { false, true })
        {
            var r = RuntimeRequirements.Resolve("1.7", engine, kind, runtime,
                included ? ["factory-bronze", "factory-analysis", "factory-bi"] : ["factory-bronze"]);
            Assert.Equal(engine == "spark" || (included && kind == "spark-ml" && runtime == "local"), r.Modules.Contains("pyspark"));
            Assert.Contains("duckdb", r.Modules);
            Assert.Contains("pyarrow", r.Modules);
            Assert.Equal(new[] { engine == "spark" ? "pyspark" : engine }, r.Distributions);
            Assert.Equal(included && kind != "none", r.Modules.Contains("sklearn"));
        }
    }

    [Fact]
    public void SharedExportsLegacyAndReportRequirementsRemainIntact()
    {
        Assert.Contains("sklearn", RuntimeRequirements.Resolve("1.5", "duckdb", null, null, ["factory-ml"]).Modules);
        Assert.Contains("pandas", RuntimeRequirements.Resolve("1.7", "duckdb", "none", "local", ["factory-bi"]).Modules);
        Assert.Contains("dbt.adapters.duckdb", RuntimeRequirements.Resolve("1.7", "duckdb", null, null, ["factory-gold"]).Modules);
        Assert.Empty(RuntimeRequirements.Resolve("1.7", "spark", "spark-ml", "local", ["factory-analysis"], true).Modules);
    }
}
