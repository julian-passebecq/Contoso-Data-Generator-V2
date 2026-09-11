namespace ContosoForge.PipelineStudio;

public sealed record RuntimeRequirements(string[] Modules, string[] Distributions, string Description)
{
    public static RuntimeRequirements Resolve(string? version, string? engine, string? kind, string? runtime,
        IEnumerable<string> implementations, bool report = false)
    {
        if (report) return new([], [], "Report build requires Node/npm");
        var operations = implementations.ToArray();
        engine ??= "duckdb";
        var engineModule = engine == "spark" ? "pyspark" : engine;
        // Dispatch imports dbt_runtime and the Arrow contract even at Bronze.
        var modules = new List<string> { "duckdb", "pyarrow" };
        if (version == "1.7" || operations.Any(o => o is "factory-bronze" or "factory-silver")) modules.Add(engineModule);
        if (operations.Any(o => o.Contains("dbt") || o == "factory-gold")) modules.AddRange(["dbt.cli.main", "dbt.adapters.duckdb"]);
        if (operations.Contains("factory-bi")) modules.Add("pandas"); // Shared report CSV helper.
        var analysis = operations.Any(o => o is "factory-ml" or "factory-analysis");
        if (analysis && (kind is not (null or "none") || version != "1.7"))
            modules.AddRange(["sklearn", "pandas", "numpy", "joblib"]); // Also imported by export helpers.
        var localSpark = analysis && kind == "spark-ml" && runtime == "local";
        if (localSpark) modules.Add("pyspark");
        return new(modules.Distinct().ToArray(), version == "1.7" ? [engineModule] : [],
            $"Selected {engine} engine" + (localSpark ? " and local Spark ML analysis (requires pyspark)" : " runtime"));
    }
}
