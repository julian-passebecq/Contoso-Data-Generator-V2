#nullable enable
using DatabaseGenerator.Forge.Architecture;
using DatabaseGenerator.Forge.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DatabaseGenerator.Forge.Planning;

public static class PlanBuilder
{
    /// <summary>Pure planning: validates intent and inspects compiler mappings without files, processes, credentials or network.</summary>
    public static ResolvedPlan Build(StudioProjectSpec project, string? pipelineJson = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.SourceProject is null) throw new ArgumentException("sourceProject cannot be null.");
        project.SourceProject.Validate();
        var scenario = ScenarioCatalog.Get(project.BusinessScenario ?? ScenarioCatalog.DefaultScenarioId);
        var resolved = ArchitecturePresets.Resolve(project);
        var resolvedJson = ArchitecturePresets.ToJson(resolved);
        pipelineJson ??= PipelineCompiler.CreateDefault(resolvedJson);
        var definition = PipelineDocument.Read(pipelineJson);
        var compilation = PipelineCompiler.Inspect(pipelineJson, resolvedJson);
        var preparation = compilation.Activities.SingleOrDefault(a => a.Operation == "prepare-colab");
        if (definition.Activities.Count(a => a.Implementation == "colab-work-order") > 1
            && (resolved.Settings.Warehouse == "bigquery" || resolved.Settings.Runtime!.StartsWith("google-colab", StringComparison.Ordinal)))
            throw new ArgumentException("The existing Colab exporter requires exactly one work-order activity; multiple independent work orders need an exporter extension.");
        var generation = project.SourceProject.Generation;
        var settings = resolved.Settings;
        var plan = new ResolvedPlan
        {
            Product = project.Product is null ? null : new ProductDesign(),
            ProjectName = resolved.Name, ArchitecturePreset = resolved.PresetId, ResolvedSettings = settings,
            BusinessScenario = new() { Id = scenario.ScenarioId, DisplayName = scenario.DisplayName, MlEnabled = scenario.MlEnabled,
                Profile = MatchesProfile(project, scenario) ? scenario.ProfileId : "custom" },
            GenerationProfile = new()
            {
                Orders = generation.Orders, Customers = generation.Customers, Products = generation.Products,
                Stores = generation.Stores, TimeSpanDays = generation.TimeSpanDays ?? 60,
                TimeSpanExplicit = generation.TimeSpanDays.HasValue, Seed = generation.Seed, StartDate = generation.StartDate
            },
            Warnings = new()
            {
                "PLAN is offline and has performed no generation, compilation or execution. Stage evidence describes historical adapter coverage, not this project's run.",
                "A valid contract is not proof that every activity has an executable compiler mapping. Unsupported operations remain explicit."
            },
            CostAndQuotaNotes = new() { "Plan and compile require no credentials, make no cloud calls and create no billable resources." }
        };

        string Unique(string name)
        {
            var candidate = "plan_" + name;
            while (definition.Activities.Any(a => a.Id == candidate) || plan.Stages.Any(a => a.Id == candidate)) candidate += "_";
            return candidate;
        }

        var source = new PlanStage
        {
            Id = Unique("generate"), Name = "Generate business source and truth", Kind = "source-generation",
            Engine = "contoso-forge-csharp", Runtime = "local-process", Inputs = new() { "project.json" },
            Outputs = new() { "data/source", "truth_manifest.json", "models/semantic_model.json" },
            ExecutionMode = "local", CompilerOperation = "forge-generate", CompilerBoundary = "generation-prerequisite",
            FileFormat = "csv", TableFormat = "none", Source = "project.json", Sink = "local",
            Reason = "The existing deterministic C# generator supplies the same entity graph and injectors; PLAN does not run it. models/semantic_model.json carries tool-neutral semantic intent under its existing filename; no semantic_intent.json alias is generated."
        };
        CapabilityResolver.Observed(source, "default-generation-regression", "reconciled", "The default profile preserved 152 generated files byte for byte and independent truth invariants.");
        plan.Stages.Add(source);

        foreach (var mapping in compilation.Activities)
        {
            var activity = definition.Activities.Single(a => a.Id == mapping.Id);
            var fileFormat = activity.FileFormat ?? settings.FileFormat;
            var tableFormat = activity.TableFormat ?? settings.TableFormat;
            if (mapping.Sink == "bigquery" && tableFormat != "none")
                throw new ArgumentException($"Activity '{mapping.Id}': native BigQuery requires tableFormat=none; open table formats require an implemented BigLake adapter.");
            var stage = new PlanStage
            {
                Id = mapping.Id, Name = activity.Name ?? mapping.Id, Kind = activity.Kind,
                Engine = mapping.Engine ?? "none", Runtime = mapping.Runtime ?? "none",
                Inputs = activity.Inputs.OrderBy(i => i, StringComparer.Ordinal).ToList(), Outputs = activity.Outputs.OrderBy(i => i, StringComparer.Ordinal).ToList(),
                ExecutionMode = mapping.Operation == "unsupported" ? "unsupported" : mapping.Operation == "await-colab" ? "external-interactive" : "local-or-airflow",
                Manual = mapping.Operation == "await-colab" || activity.Kind == "manual-checkpoint",
                CompilerOperation = mapping.Operation, Reason = mapping.Reason,
                Source = mapping.Source, Sink = mapping.Sink, FileFormat = fileFormat, TableFormat = tableFormat,
                SparkApiMode = mapping.SparkApiMode, SparkVersion = mapping.SparkVersion
            };
            if (mapping.Runtime!.StartsWith("google-colab", StringComparison.Ordinal)
                && !CapabilityResolver.SupportsColabVersion(mapping.SparkApiMode, mapping.SparkVersion))
                throw new ArgumentException($"Activity '{mapping.Id}': generated Colab bootstrap does not support Spark {mapping.SparkVersion} in {mapping.SparkApiMode} mode. Classic supports 3.5.9 or 4.0.4; Connect supports 4.0.4.");
            // Result adoption concerns the producing work order. An inherited classic setting on a
            // sensor must never promote a Connect-only activity override to classic full-run evidence.
            var evidenceMapping = mapping.Operation is "await-colab" or "reconcile-colab" && preparation is not null
                ? new PipelinePlannedActivity
                {
                    Operation = mapping.Operation, Engine = preparation.Engine, Runtime = preparation.Runtime,
                    Source = preparation.Source, Sink = preparation.Sink, SparkApiMode = preparation.SparkApiMode,
                    SparkVersion = preparation.SparkVersion
                } : mapping;
            CapabilityResolver.ResolveActivity(stage, evidenceMapping, Effective(settings, activity));
            if (mapping.Operation is "prepare-colab" or "await-colab" or "reconcile-colab" && settings.Warehouse != "bigquery")
            {
                stage.ImplementationStatus = "unsupported";
                stage.ValidationLevel = "declared";
                stage.Evidence.Clear();
                stage.Reason = "The compiler operation exists, but the generated project-level BigQuery configuration does not select warehouse=bigquery. Activity-only sink overrides do not change exporter configuration.";
                plan.Warnings.Add($"Activity '{stage.Id}': {stage.Reason}");
            }
            plan.Stages.Add(stage);
            foreach (var parent in mapping.DependsOn) plan.Edges.Add(new() { From = parent, To = stage.Id });
            if (mapping.DependsOn.Count == 0) plan.Edges.Add(new() { From = source.Id, To = stage.Id });
            if (stage.Manual) plan.ManualCheckpoints.Add(new() { AfterStage = stage.Id, Reason = stage.Reason });
            if (mapping.Operation == "unsupported") plan.Warnings.Add($"Activity '{mapping.Id}' has no executable compiler operation: {mapping.Reason}");
        }

        if (preparation is not null && settings.Warehouse == "bigquery")
            AddColabDetails(plan, compilation, preparation, Unique);

        AddControlPlane(plan, Unique);
        AddCredentialsAndCosts(plan, project, definition);
        AddArtifacts(plan, source.Id);
        if (project.Product is not null) AddProduct(plan, project, Unique);
        if (scenario.MlEnabled && preparation is null)
            plan.Warnings.Add("ML semantic intent is preserved, but this selected architecture has no implemented native BigQuery ML execution path. No training is implied.");
        if (scenario.MlEnabled && (generation.TimeSpanDays ?? 60) < 365)
            plan.Warnings.Add("The selected ML scenario retains this custom generation profile. A short horizon may leave chronological partitions without both classes; use explicit scenario selection to apply the 365-day profile, then check actual label readiness.");
        if (settings.Runtime == "docker" && settings.Engine == "spark")
            plan.Warnings.Add("The existing V1 Docker Spark/Delta/dbt reference exporter is preserved. The neutral engine-transform activity is not an implemented Docker runner mapping.");
        var unsupported = compilation.Activities.Any(a => a.Operation == "unsupported") || plan.Stages.Any(s => s.ImplementationStatus == "unsupported");
        // Aggregate stage implementation, including deployment prerequisites. Historical
        // "executed" adapters contribute runnable capability, never execution of this project.
        plan.OverallImplementationStatus = plan.Stages.Any(s => s.ImplementationStatus == "unsupported") ? "unsupported"
            : plan.Stages.Any(s => s.ImplementationStatus == "reference-only") ? "reference-only"
            : plan.Stages.Any(s => s.ImplementationStatus == "generated") ? "generated"
            : "runnable";
        // An assembled new project has no run evidence even when its individual adapters have it.
        plan.OverallReadiness = unsupported ? "declared" : "generated";
        plan.Edges = plan.Edges.DistinctBy(e => (e.From, e.To)).OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal).ToList();
        plan.RequiredCredentials = plan.RequiredCredentials.DistinctBy(c => c.Scope).OrderBy(c => c.Scope, StringComparer.Ordinal).ToList();
        return plan;
    }

    public static string ToJson(ResolvedPlan plan) => JsonSerializer.Serialize(plan, PlanningJsonContext.Default.ResolvedPlan).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    private static void AddProduct(ResolvedPlan plan, StudioProjectSpec project, Func<string, string> unique)
    {
        var intent = project.Product!;
        var settings = plan.ResolvedSettings;
        plan.Product = new ProductDesign
        {
            Version = intent.Version, PipelineMode = intent.PipelineMode, BiTarget = intent.BiTarget,
            Orchestrator = settings.Orchestrator!.StartsWith("airflow", StringComparison.Ordinal) ? "airflow" : settings.Orchestrator,
            AirflowHost = settings.AirflowHost ?? (settings.Orchestrator == "airflow-minikube" ? "minikube" : settings.Orchestrator == "airflow-docker" ? "docker-local" : null),
            Ml = plan.BusinessScenario.MlEnabled ? new MlExperimentDesign { RuntimeTarget = intent.MlTarget } : null
        };
        foreach (var stage in plan.Stages.Where(s => s.CompilerOperation.StartsWith("factory-", StringComparison.Ordinal)))
        {
            var generatedOnly = stage.CompilerOperation == "factory-export-ml" || (intent.DbtIntegration == "cosmos" && stage.CompilerOperation == "factory-dbt");
            stage.ImplementationStatus = generatedOnly ? "generated" : "runnable";
            stage.ValidationLevel = generatedOnly ? "generated" : "reconciled";
            if (intent.DbtIntegration == "cosmos" && stage.CompilerOperation == "factory-dbt")
            {
                stage.Evidence.Clear();
                stage.Reason = "Cosmos is generated/unverified. The TaskGroup runs dbt, then a second plain dbt build records authoritative results. Promotion requires real task execution with unambiguous results from both invocations.";
                plan.Warnings.Add(stage.Reason);
            }
            if (!generatedOnly) stage.Evidence.Add(new()
            {
                Id = settings.Engine == "duckdb" ? "v15-local-duckdb-dbt-sklearn-bi" : "v16-local-" + settings.Engine,
                Reference = settings.Engine == "duckdb" ? "docs/v1.5-evidence.json" : "docs/v1.6-evidence.json", ValidationLevel = "reconciled",
                Scope = $"Versioned local {settings.Engine} adapter evidence: real Bronze/Silver, dbt Gold/tests, independent KPI reconciliation, ML and Evidence. This planned project has not executed; report rendering and cross-engine parity require their own measured artifacts."
            });
            stage.ExecutionMode = "local-or-airflow";
            stage.Engine = stage.CompilerOperation switch
            {
                "factory-silver" => settings.Engine!, "factory-verify" => "python",
                "factory-dbt" => "dbt-duckdb", "factory-ml" or "factory-export-ml" => "scikit-learn",
                "factory-bi" => "evidence", _ => "duckdb"
            };
            stage.Kind = stage.CompilerOperation switch { "factory-dbt" => "analytics-transform", "factory-ml" => "ml-training", "factory-export-ml" => "ml-design", "factory-bi" => "bi-validation", _ => stage.Kind };
        }
        var gold = plan.Stages.LastOrDefault(s => s.Kind == "analytics-transform");
        if (!plan.Stages.Any(s => s.Kind == "bi-validation"))
        {
            var bi = new PlanStage { Id = unique("bi_validation"), Name = "BI & Validation / Evidence", Kind = "bi-validation", Engine = "evidence", Runtime = "local-process",
                Inputs = plan.Product.BiInputs, Outputs = new() { "Evidence report package" }, ImplementationStatus = "generated", ValidationLevel = "generated",
                CompilerOperation = "standalone-evidence", CompilerBoundary = "standalone-after-pipeline", ExecutionMode = "explicit-local-build",
                Reason = "Generated universal report consumer. Export measured Gold and bound dbt/run evidence before building; compilation does not execute or publish a report." };
            plan.Stages.Add(bi);
            if (gold is not null) plan.Edges.Add(new() { From = gold.Id, To = bi.Id });
        }
        if (plan.BusinessScenario.MlEnabled && !plan.Stages.Any(s => s.Kind is "ml-training" or "ml-design"))
        {
            var ml = new PlanStage { Id = unique("ml_design"), Name = "ML Lab / " + intent.MlTarget, Kind = "ml-design", Engine = "scikit-learn", Runtime = intent.MlTarget,
                Inputs = new() { "Gold feature mart" }, Outputs = new() { "factory/ml/spec.json", "notebook export" }, ImplementationStatus = "generated", ValidationLevel = "generated",
                CompilerOperation = "standalone-ml-export", CompilerBoundary = "standalone-after-pipeline", ExecutionMode = "explicit-notebook",
                Reason = "Delivery-time classification, mature labels, chronological split and 14-day embargo. Training requires actual metrics from the selected runtime." };
            plan.Stages.Add(ml);
            var bi = plan.Stages.Single(s => s.Kind == "bi-validation");
            if (gold is not null)
            {
                plan.Edges.RemoveAll(e => e.From == gold.Id && e.To == bi.Id);
                plan.Edges.Add(new() { From = gold.Id, To = ml.Id });
            }
            plan.Edges.Add(new() { From = ml.Id, To = bi.Id });
        }
        plan.Artifacts.Add(new() { Path = "factory/product_design.json", Purpose = "Compiled product intent, derived from this project.", StageId = plan.Stages[0].Id });
        plan.Warnings.RemoveAll(w => w.Contains("no implemented native BigQuery ML execution path", StringComparison.Ordinal));
        plan.CostAndQuotaNotes.Add("Colab remains a manual interactive checkpoint with dynamic limits. Keep small sklearn feature marts in the same Colab session; materialization limit " + intent.MaterializationLimitMb + " MiB. Kaggle/Databricks notebooks are exports; BQML training requires explicit billing authorization.");
        plan.CostAndQuotaNotes.Add("Docker/Codespaces are development hosts; Minikube/Helm/GitSync is Kubernetes proof; GitHub Actions/kind-ci are finite CI validation. MotherDuck account quotas apply; no embedded Dive or compute-backed Hugging Face Space is required.");
    }

    private static bool MatchesProfile(StudioProjectSpec project, ScenarioDefinition scenario)
    {
        var g = project.SourceProject.Generation;
        var p = scenario.GenerationProfile;
        return g.Orders == p.Orders && g.Customers == p.Customers && g.Products == p.Products && g.Stores == p.Stores
            && g.Seed == p.Seed && g.StartDate == p.StartDate && (g.TimeSpanDays ?? 60) == p.TimeSpanDays;
    }

    private static ArchitectureSettings Effective(ArchitectureSettings settings, PipelineActivity activity) => new()
    {
        FileFormat = activity.FileFormat ?? settings.FileFormat,
        TableFormat = activity.TableFormat ?? settings.TableFormat
    };

    private static void AddColabDetails(ResolvedPlan plan, PipelineExecutionPlan compilation, PipelinePlannedActivity preparation, Func<string, string> unique)
    {
        var work = plan.Stages.Single(s => s.Id == preparation.Id);
        var observedSpark = CapabilityResolver.HasHostedSparkEvidence(preparation, new() { FileFormat = work.FileFormat, TableFormat = work.TableFormat });
        var spark = new PlanStage
        {
            Id = unique("spark_silver"), Name = "Colab Bronze / Silver", Kind = "transform", Engine = preparation.Engine!, Runtime = preparation.Runtime!,
            Inputs = new() { "data/source" }, Outputs = new() { "lake/silver" }, ExecutionMode = "external-interactive", Manual = true,
            ImplementationStatus = "runnable", ValidationLevel = "generated", Source = preparation.Source, Sink = "local", FileFormat = work.FileFormat, TableFormat = work.TableFormat,
            SparkApiMode = preparation.SparkApiMode, SparkVersion = preparation.SparkVersion,
            CompilerOperation = "prepare-colab", CompilerBoundary = "external-work-order-detail",
            Reason = "Runs inside the generated Colab notebook after a human opens and starts it; this is a detail of the work order, not an additional Airflow task."
        };
        if (observedSpark)
            CapabilityResolver.Observed(spark, preparation.SparkApiMode == "connect-local" ? "hosted-colab-connect-local-4.0.4" : "hosted-colab-classic-4.0.4", "reconciled",
                "Local files, Parquet, Spark 4.0.4: 11 Bronze and 13 Silver tables plus DataFrame/SQL probes matched truth. Connect-local used the Connect session class and is_remote=true; classic remained classic.");
        plan.Stages.Add(spark);
        var warehouse = new PlanStage
        {
            Id = unique("warehouse"), Name = "Native BigQuery Silver load", Kind = "warehouse-load", Engine = preparation.Sink!,
            Runtime = plan.ResolvedSettings.CostProfile == "gcp-sandbox-no-card" ? "bigquery-sandbox" : "bigquery",
            Inputs = new() { "lake/silver", "truth_manifest.json" }, Outputs = new() { "native-bigquery-silver", "result_manifest.json" },
            ExecutionMode = "cloud-batch", Manual = true, ImplementationStatus = "runnable", ValidationLevel = "generated",
            Source = "local", Sink = preparation.Sink, FileFormat = "parquet", TableFormat = "none", CompilerOperation = "prepare-colab", CompilerBoundary = "external-work-order-detail",
            Reason = "The generated notebook loads native Parquet batches and reconciles counts and five KPIs. Authentication and explicit notebook execution are required."
        };
        CapabilityResolver.Observed(warehouse, "native-bigquery-sandbox-silver", "reconciled", "Classic hosted Colab loaded 13 native tables, 536 rows and five exact KPIs into a US Sandbox dataset. This is loader evidence, not proof of a full Connect composition or other dataset/location.");
        plan.Stages.Add(warehouse);
        plan.Edges.Add(new() { From = preparation.Id, To = spark.Id });
        plan.Edges.Add(new() { From = spark.Id, To = warehouse.Id });
        foreach (var awaiter in compilation.Activities.Where(a => a.Operation == "await-colab")) plan.Edges.Add(new() { From = warehouse.Id, To = awaiter.Id });
        plan.ManualCheckpoints.Add(new() { AfterStage = spark.Id, Reason = "Open the generated notebook in Colab, upload the exact work package and run it. Colab is interactive and ephemeral." });
        plan.ManualCheckpoints.Add(new() { AfterStage = warehouse.Id, Reason = "Authenticate BigQuery, execute native load/reconciliation and return this run's result manifest to the waiting runner." });
        if (preparation.SparkApiMode == "connect-local")
            plan.Warnings.Add("Connect-local Spark and the BigQuery loader have separate historical evidence. Their complete composed Connect -> BigQuery -> GitHub GitSync/Airflow path is not marked reconciled.");

        var reconcile = compilation.Activities.LastOrDefault(a => a.Operation == "reconcile-colab");
        if (reconcile is null) return;
        var gold = new PlanStage
        {
            Id = unique("gold"), Name = "dbt BigQuery Gold", Kind = "analytics-transform", Engine = "dbt-bigquery", Runtime = "bigquery",
            Inputs = new() { "native-bigquery-silver", "result_manifest.json", "truth_manifest.json" }, Outputs = new() { "gold-models", "dbt_bigquery/gold_evidence.json" },
            ExecutionMode = "cloud", Manual = true, Source = "bigquery", Sink = "bigquery", FileFormat = "sql", TableFormat = "none",
            CompilerOperation = "standalone-dbt", CompilerBoundary = "standalone-after-pipeline",
            Reason = "Generated dbt_bigquery/run_dbt.py is run explicitly after the full reconciled result. Arbitrary dbt activities are not mapped by the neutral compiler."
        };
        CapabilityResolver.Observed(gold, "native-dbt-bigquery-24-models-121-tests", "reconciled", "24 Gold models and 121 tests passed; an independent native Gold query matched all five truth KPIs. This was a separate classic-hosted run.");
        plan.Stages.Add(gold);
        plan.Edges.Add(new() { From = reconcile.Id, To = gold.Id });
        plan.ManualCheckpoints.Add(new() { AfterStage = gold.Id, Reason = gold.Reason });
        plan.Warnings.Add("Gold is a generated standalone adapter after the core pipeline. PLAN does not add an executable dbt activity to the compiled DAG.");
        if (!plan.BusinessScenario.MlEnabled || plan.Product is not null) return;
        var features = new PlanStage
        {
            Id = unique("ml_features"), Name = "ML features and partition readiness", Kind = "ml-features", Engine = "bigquery-sql", Runtime = "bigquery",
            Inputs = new() { "gold-models", "bqml/features.sql" }, Outputs = new() { "labelled-temporal-features", "partition-readiness" },
            ExecutionMode = "cloud-query", Manual = true, Source = "bigquery", Sink = "bigquery", FileFormat = "sql", TableFormat = "none",
            CompilerOperation = "standalone-bqml-preview", CompilerBoundary = "standalone-after-pipeline",
            Reason = "Delivery-time features, mature labels, chronological partitions and a 14-day embargo. The actual run must prove both classes in each partition; row count alone is insufficient."
        };
        CapabilityResolver.Observed(features, "native-bqml-feature-query", "executes", "The native feature SELECT executed. The 60-day fixture had insufficient partitions; a 365-day/1200-order profile passed offline Spark/Gold partition viability. Model training was not run.");
        plan.Stages.Add(features);
        plan.Edges.Add(new() { From = gold.Id, To = features.Id });
        var training = new PlanStage
        {
            Id = unique("ml_train"), Name = "Optional cost-authorized ML training", Kind = "ml-training", Engine = "bigquery-ml", Runtime = "bigquery",
            Inputs = new() { "labelled-temporal-features", "partition-readiness" }, Outputs = new() { "model", "metrics", "model-card" },
            ExecutionMode = "explicit-opt-in-cloud", Manual = true, ImplementationStatus = "generated", ValidationLevel = "generated",
            Source = "bigquery", Sink = "bigquery", FileFormat = "sql", TableFormat = "none", CompilerOperation = "standalone-bqml-execute", CompilerBoundary = "standalone-after-pipeline",
            Reason = "Generated training adapter requires --execute and --allow-training-cost plus successful native prerequisites. Training has no real execution evidence and is not guaranteed available without billing."
        };
        plan.Stages.Add(training);
        plan.Edges.Add(new() { From = features.Id, To = training.Id });
        plan.ManualCheckpoints.Add(new() { AfterStage = training.Id, Reason = training.Reason });
        plan.Warnings.Add("BQML model training is generated only and has never been claimed validated. The ML scenario does not authorize training or imply free Sandbox training.");
    }

    private static void AddControlPlane(ResolvedPlan plan, Func<string, string> unique)
    {
        var s = plan.ResolvedSettings;
        if (s.Orchestrator is not ("none" or "local-sequential"))
        {
            var orchestration = new PlanStage
            {
                Id = unique("orchestration"), Name = "Orchestration / DAG delivery", Kind = "orchestration", Engine = s.Orchestrator!.StartsWith("airflow", StringComparison.Ordinal) ? "airflow" : s.Orchestrator,
                Runtime = s.Orchestrator, Inputs = new() { "pipeline.json" }, Outputs = new() { "orchestrated-run" },
                ExecutionMode = "operator-managed", Manual = true, CompilerOperation = "control-plane", CompilerBoundary = "deployment-prerequisite",
                ImplementationStatus = s.Orchestrator == "airflow-minikube" ? "generated" : "reference-only", ValidationLevel = s.Orchestrator == "airflow-minikube" ? "parses" : "declared",
                Reason = s.Orchestrator == "airflow-minikube"
                    ? "Airflow 3 / Helm / Minikube uses GitSync from the configured public HTTPS repository. Historical four-task execution used a local Git server; public GitHub delivery remains pending."
                    : "Existing reference artifacts are retained; this orchestration backend is not an executable neutral compiler exporter."
            };
            if (s.Orchestrator == "airflow-minikube")
                orchestration.Evidence.Add(CapabilityResolver.Historical("minikube-local-gitsync", "reconciled", "All four tasks accepted an exact hosted classic/BigQuery result with local GitSync. This does not validate the selected public GitHub repository or this project."));
            plan.Stages.Add(orchestration);
            var first = plan.Stages.FirstOrDefault(stage => stage.CompilerBoundary == "pipeline-activity");
            if (first is not null) plan.Edges.Add(new() { From = orchestration.Id, To = first.Id });
            plan.ManualCheckpoints.Add(new() { AfterStage = orchestration.Id, Reason = orchestration.Reason });
        }
        if (s.Iac != "none")
        {
            var implemented = s.Warehouse == "bigquery";
            var iac = new PlanStage
            {
                Id = unique("infrastructure"), Name = "Optional infrastructure", Kind = "infrastructure", Engine = s.Iac!, Runtime = "local-cli",
                Inputs = new() { "infra/gcp/*.tf" }, Outputs = new() { "optional-resources" }, ExecutionMode = "explicit-opt-in", Manual = true,
                ImplementationStatus = implemented ? "generated" : "reference-only", ValidationLevel = implemented ? "parses" : "declared",
                CompilerOperation = "standalone-iac", CompilerBoundary = "optional-deployment",
                Reason = implemented ? "OpenTofu and Terraform validation cover generated BigQuery infrastructure. Validation is not apply evidence; Sandbox loading does not require an automatic infrastructure apply."
                    : "Reference IaC intent is preserved; no provider resources are provisioned by PLAN."
            };
            if (implemented) iac.Evidence.Add(CapabilityResolver.Historical("bigquery-iac-validate", "parses", "OpenTofu and Terraform validated the generated native BigQuery configuration; cloud apply was not performed."));
            plan.Stages.Add(iac);
            plan.ManualCheckpoints.Add(new() { AfterStage = iac.Id, Reason = iac.Reason });
        }
    }

    private static void AddCredentialsAndCosts(ResolvedPlan plan, StudioProjectSpec project, PipelineDefinition definition)
    {
        var s = plan.ResolvedSettings;
        if (plan.Stages.Any(a => a.Runtime.StartsWith("google-colab", StringComparison.Ordinal)))
            plan.RequiredCredentials.Add(new() { Scope = "google-colab-session", Reason = "A signed-in interactive Colab session is required at execution time." });
        if (plan.Stages.Any(a => a.Sink == "bigquery") || s.Warehouse is "bigquery" or "biglake")
        {
            plan.RequiredCredentials.Add(new() { Scope = "bigquery", Reason = "User OAuth / ADC with dataset and job permissions is required for native loads and queries; no credential is read during planning." });
            if (project.Gcp.ProjectId == "your-gcp-project") plan.Warnings.Add("The GCP project ID is a public placeholder. Choose an actual destination before execution; planning does not check its existence or permissions.");
        }
        if (s.Orchestrator == "airflow-minikube")
            plan.RequiredCredentials.Add(new() { Scope = "kubernetes-context", Reason = "An explicitly selected Minikube context and local cluster permissions are required to deploy Airflow." });
        if (s.DagSource == "github-gitsync")
        {
            plan.RequiredCredentials.Add(new() { Scope = "git-read", RequiredAtExecutionTime = false, Reason = "Public HTTPS repositories need no Git secret. Private repositories require a separate read-only secret; no credentials belong in the URL." });
            plan.Warnings.Add("The Git repository, branch and generated subpaths are validated syntactically only. PLAN does not verify public availability, exact generated identity or GitSync execution.");
        }
        foreach (var provider in plan.Stages.SelectMany(stage => new[] { stage.Sink, stage.Runtime })
                     .Concat(new[] { s.Warehouse, s.Runtime, s.Orchestrator })
                     .Select(value => value switch
                     {
                         "fabric" or "fabric-spark" => "fabric",
                         "databricks" or "databricks-spark" or "databricks-jobs" => "databricks",
                         "sqlserver" => "sqlserver", "motherduck" => "motherduck", "adf" => "azure-data-factory", _ => null
                     }).Where(value => value is not null).Distinct(StringComparer.Ordinal))
            plan.RequiredCredentials.Add(new()
            {
                Scope = "provider:" + provider,
                Reason = "Declared " + provider + " intent requires an external account/workspace or database connection with suitable permissions at execution. The reference adapter is not made executable by supplying credentials."
            });
        if (plan.Stages.Any(stage => stage.Runtime == "google-colab-connect-remote"))
            plan.RequiredCredentials.Add(new() { Scope = "spark-connect-remote", Reason = "A reachable Connect endpoint, endpoint-specific authentication and shared storage would be required. The Forge remote transport remains unsupported." });
        foreach (var storage in plan.Stages.Select(a => a.Source).Append(s.Storage).Where(v => v is "gcs" or "azure-adls" or "fabric-onelake" or "r2" or "seaweedfs" or "b2" or "s3").Distinct(StringComparer.Ordinal))
            plan.RequiredCredentials.Add(new() { Scope = "storage:" + storage, Reason = "This declared external storage requires a provider-specific credential at execution; reference adapters remain unimplemented." });
        foreach (var connection in definition.Connections.Where(c => c.SecretRef is not null).OrderBy(c => c.Id, StringComparer.Ordinal))
            plan.RequiredCredentials.Add(new() { Scope = "connection:" + connection.Id, Reason = "A secret reference is declared. The actual secret remains in its external provider; no secret is loaded or emitted by PLAN." });
        if (s.CostProfile == "gcp-sandbox-no-card")
            plan.CostAndQuotaNotes.Add("BigQuery Sandbox native batch loads and quotas apply; tables expire after 60 days. No GCS bucket, streaming or billing-dependent resource is required by the default path. Eligibility and remaining quotas are checked only at execution.");
        else if (s.CostProfile == "gcp-free-tier-billing-enabled")
            plan.CostAndQuotaNotes.Add("GCP billing is enabled for this intent. Free allowances do not guarantee no charges, and external storage may add costs.");
        else if (s.CostProfile == "local") plan.CostAndQuotaNotes.Add("Local runtimes consume host compute, memory and disk. Cloud billing is not part of this local profile.");
        else plan.CostAndQuotaNotes.Add("External providers have separate account, quota and pricing requirements; the planner does not estimate or authorize charges.");
        if (s.Warehouse is "bigquery" or "biglake") plan.CostAndQuotaNotes.Add($"BigQuery query maximumBytesBilled is {project.Gcp.MaximumBytesBilled}; reported bytes billed are quota/accounting metrics, not a monetary charge estimate.");
        if (plan.BusinessScenario.MlEnabled) plan.CostAndQuotaNotes.Add("BQML training can require billing and incur charges. Feature-readiness checks are distinct from training; no model is created by PLAN or COMPILE.");
    }

    private static void AddArtifacts(ResolvedPlan plan, string sourceId)
    {
        foreach (var path in new[] { "project.json", "resolved_project.json", "run_manifest.json", "truth_manifest.json", "models/source_model.json", "models/gold_model.json", "models/semantic_model.json", "models/kpi_catalog.json", "pipeline/pipeline.json" })
            plan.Artifacts.Add(new() { Path = path, Purpose = path == "models/semantic_model.json"
                ? "Existing tool-neutral semantic intent starter/reference. Its preserved filename is semantic_model.json; the conceptual semantic_intent.json name does not add a generated file or imply final Power BI deployment."
                : "Existing generated business/semantic JSON bridge; no final Power BI deployment is implied.", StageId = sourceId });
        plan.Artifacts.Add(new() { Path = "plan/resolved_plan.json", Purpose = "This opt-in plan output; ordinary generation remains unchanged.", StageId = sourceId });
        plan.Artifacts.Add(new() { Path = "pipeline.json", Purpose = "Editable neutral activity contract consumed by the existing compiler.", StageId = sourceId });
        plan.Artifacts.Add(new() { Path = "local_plan.json", Purpose = "Exact executable/unsupported compiler operations; generated only by COMPILE.", StageId = sourceId });
        foreach (var stage in plan.Stages)
        {
            var artifact = stage.CompilerOperation switch
            {
                "standalone-dbt" => "dbt_bigquery/run_dbt.py", "standalone-bqml-preview" => "bqml/features.sql", "standalone-bqml-execute" => "bqml/run_bqml.py",
                "standalone-iac" when stage.ImplementationStatus != "reference-only" => "infra/gcp/main.tf", "control-plane" when stage.Runtime == "airflow-minikube" => "minikube/values.yaml",
                "prepare-colab" when stage.CompilerBoundary == "pipeline-activity" => "colab/contoso_free_gcp.ipynb", _ => null
            };
            if (artifact is not null) plan.Artifacts.Add(new() { Path = artifact, Purpose = stage.Reason, StageId = stage.Id });
        }
    }
}
