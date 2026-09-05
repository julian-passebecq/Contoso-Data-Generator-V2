#nullable enable
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Planning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DatabaseGenerator.Forge.Export;

internal static class FactoryExporter
{
    internal static void Export(string root, StudioProjectSpec project)
    {
        if (project.Product is null) return;
        var templates = Path.Combine(AppContext.BaseDirectory, "Forge", "Templates");
        var destination = Path.Combine(root, "factory");
        ForgeIo.CopyTreeWithTokens(Path.Combine(templates, "v15"), destination, new Dictionary<string, string>());
        ForgeIo.CopyTreeWithTokens(Path.Combine(templates, "customer_satisfaction", "dbt"), Path.Combine(destination, "dbt"),
            new Dictionary<string, string> { ["__EXPECTED_ORDER_COUNT__"] = project.SourceProject.Generation.Orders.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        // Extract the existing business join into a visible intermediate model; its logic stays unchanged.
        var fact = Path.Combine(destination, "dbt", "models", "gold", "fact_customer_experience.sql");
        ForgeIo.WriteText(Path.Combine(destination, "dbt", "models", "intermediate", "int_customer_experience.sql"), File.ReadAllText(fact));
        ForgeIo.WriteText(fact, "select * from {{ ref('int_customer_experience') }}");
        var dbtProject = Path.Combine(destination, "dbt", "dbt_project.yml");
        ForgeIo.WriteText(dbtProject, File.ReadAllText(dbtProject) + "\n    intermediate:\n      +materialized: view\n      +schema: intermediate\n");
        ForgeIo.CopyTreeWithTokens(Path.Combine(templates, "v15_dbt"), Path.Combine(destination, "dbt"), new Dictionary<string, string>());
        var design = new MlExperimentDesign { RuntimeTarget = project.Product.MlTarget };
        ForgeIo.WriteText(Path.Combine(destination, "ml", "spec.json"), JsonSerializer.Serialize(design, PlanningJsonContext.Default.MlExperimentDesign));
        var config = new JsonObject
        {
            ["enabled"] = project.BusinessScenario == ScenarioCatalog.MlScenarioId,
            ["target"] = project.Product.MlTarget, ["labelAsOf"] = project.Product.LabelAsOf,
            ["seed"] = project.SourceProject.Generation.Seed, ["materializationLimitMb"] = project.Product.MaterializationLimitMb,
            ["trainingStatus"] = "not-executed", ["threshold"] = 0.5
        };
        ForgeIo.WriteText(Path.Combine(destination, "ml", "run_config.json"), config.ToJsonString());
        var plan = PlanBuilder.Build(project);
        ForgeIo.WriteText(Path.Combine(destination, "product_design.json"), JsonSerializer.Serialize(plan.Product!, PlanningJsonContext.Default.ProductDesign));
        if (project.Product.DbtIntegration == "cosmos")
            ForgeIo.WriteText(Path.Combine(root, "airflow/dags/contoso_forge_cosmos.py"), File.ReadAllText(Path.Combine(destination, "cosmos_dag.py")));
        // Only opt-in packages include V1.5 authored inputs. Run outputs and credentials are excluded.
        var handoff = Path.Combine(root, "colab/work_order.py");
        if (File.Exists(handoff))
        {
            var code = File.ReadAllText(handoff);
            var additions = "    names += [\"project.json\", \"models/source_model.json\", \"models/kpi_catalog.json\", \"models/semantic_model.json\"]\n"
                + "    for path in sorted((root / \"factory\").rglob(\"*\")):\n"
                + "        relative = path.relative_to(root)\n"
                + "        if path.is_file() and path.suffix in (\".py\", \".json\", \".sql\", \".yml\", \".txt\", \".md\", \".tsx\") and not {\"target\", \"logs\", \"dbt_packages\", \"__pycache__\"}.intersection(relative.parts):\n"
                + "            names.append(relative.as_posix())\n";
            code = code.Replace("    names += [\"data/source/\" + name for name in sorted(hashes)]", additions + "    names += [\"data/source/\" + name for name in sorted(hashes)]", StringComparison.Ordinal);
            ForgeIo.WriteText(handoff, code);
            var notebookPath = Path.Combine(root, "colab/contoso_free_gcp.ipynb");
            var notebook = JsonNode.Parse(File.ReadAllText(notebookPath))!;
            var cells = notebook["cells"]!.AsArray();
            cells.Add(new JsonObject
            {
                ["cell_type"] = "markdown", ["metadata"] = new JsonObject(),
                ["source"] = new JsonArray("## V1.5: continue from verified Spark Silver in this session\n", "Run dbt, reconcile Gold, train the selected scikit-learn experiment and prepare Evidence locally. This works with Spark-only orders and requires no BigQuery billing. Native BigQuery results remain separate. Export targets produce packages without claiming training.\n")
            });
            cells.Add(new JsonObject
            {
                ["cell_type"] = "code", ["metadata"] = new JsonObject(), ["execution_count"] = null, ["outputs"] = new JsonArray(),
                ["source"] = new JsonArray(
                    "subprocess.run([sys.executable, '-m', 'pip', 'install', '-r', str(root / 'factory/requirements.txt')], check=True)\n",
                    "factory_state = root / ('factory-session-' + uuid.uuid4().hex)\n",
                    "subprocess.run([sys.executable, str(root / 'factory/after_spark.py'), '--root', str(root), '--lake', str(root / 'lake'), '--work-order', str(root / 'colab/work_order.json'), '--spark-runtime', str(root / 'colab/spark_runtime.json'), '--state', str(factory_state)], check=True)\n",
                    "print((factory_state / 'run_evidence.json').read_text())\n",
                    "# Optional report rendering needs Node/npm; generated source alone is not a rendered report.\n",
                    "BUILD_EVIDENCE = False\n",
                    "if BUILD_EVIDENCE:\n",
                    "    subprocess.run([sys.executable, str(root / 'factory/build_evidence.py'), '--state', str(factory_state)], check=True)\n")
            });
            ForgeIo.WriteText(notebookPath, notebook.ToJsonString(new() { WriteIndented = true }));
        }
        foreach (var relative in new[] { "pipeline/forge_pipeline_runtime.py", "airflow/dags/forge_pipeline_runtime.py" })
        {
            var path = Path.Combine(root, relative);
            var code = File.ReadAllText(path);
            code = code.Replace("    if operation == \"unsupported\":", "    if operation.startswith(\"factory-\"):\n        invoke(root, \"factory/run.py\", [\"--root\", root, \"--run-id\", run_id, \"--stage\", operation[8:]], timeout)\n        return True\n    if operation == \"unsupported\":", StringComparison.Ordinal);
            ForgeIo.WriteText(path, code);
        }
    }
}
