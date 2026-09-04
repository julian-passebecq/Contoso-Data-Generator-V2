using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Specs;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public class ProjectSpecValidationTests
{
    [Fact]
    public void SmallCustomerSatisfactionSpec_IsValid()
    {
        var spec = ForgeTestProject.CreateSmallSpec();

        spec.Validate();
    }

    [Theory]
    [InlineData("version", "ProjectSpec version")]
    [InlineData("scenario", "scenario")]
    [InlineData("name", "Project name")]
    [InlineData("unsafe-name", "Project name")]
    [InlineData("seed", "generation.seed")]
    [InlineData("orders", "generation.orders")]
    [InlineData("customers", "generation.customers")]
    [InlineData("products", "generation.products")]
    [InlineData("stores", "generation.stores")]
    [InlineData("start-date", "generation.startDate")]
    [InlineData("date-only", "generation.startDate")]
    [InlineData("legacy-mode", "generation.legacyContosoMode")]
    [InlineData("format", "generation.formats")]
    [InlineData("format-case", "generation.formats")]
    [InlineData("extension", "extension entity")]
    [InlineData("injector", "problem injector")]
    [InlineData("output", "output artifact")]
    [InlineData("lab", "local lab target")]
    public void Validate_RejectsSpecsOutsideTheV1Schema(string invalidField, string diagnostic)
    {
        var spec = ForgeTestProject.CreateSmallSpec();
        switch (invalidField)
        {
            case "version":
                spec.Version = "2.0.0";
                break;
            case "scenario":
                spec.Scenario = "retail.inventory";
                break;
            case "name":
                spec.Name = " ";
                break;
            case "unsafe-name":
                spec.Name = "unsafe\"name\n";
                break;
            case "seed":
                spec.Generation.Seed = -1;
                break;
            case "orders":
                spec.Generation.Orders = 11;
                break;
            case "customers":
                spec.Generation.Customers = 7;
                break;
            case "products":
                spec.Generation.Products = 3;
                break;
            case "stores":
                spec.Generation.Stores = 1;
                break;
            case "start-date":
                spec.Generation.StartDate = "not-a-date";
                break;
            case "date-only":
                spec.Generation.StartDate = "2024-01-01";
                break;
            case "legacy-mode":
                spec.Generation.LegacyContosoMode = false;
                break;
            case "format":
                spec.Generation.Formats = ["PARQUET"];
                break;
            case "format-case":
                spec.Generation.Formats = ["csv"];
                break;
            case "extension":
                spec.Extensions.Reviews = false;
                break;
            case "injector":
                spec.Problems.LateArrivals = false;
                break;
            case "output":
                spec.Outputs.Dbt = false;
                break;
            case "lab":
                spec.Lab.OpenTofu = false;
                break;
            default:
                throw new InvalidOperationException($"Unknown validation case '{invalidField}'.");
        }

        var exception = Assert.Throws<ArgumentException>(spec.Validate);

        Assert.Contains(diagnostic, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonContract_RejectsMisspelledAndMissingRequiredFlags()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(ForgeTestProject.CreateSmallSpec(), options);
        var misspelled = json.Replace(
            "\"reviews\":true",
            "\"reviews\":true,\"reviewz\":true",
            StringComparison.Ordinal);
        var missing = json.Replace(
            ",\"reviews\":true",
            string.Empty,
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectSpec>(misspelled, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProjectSpec>(missing, options));
    }
}

public class ForgeProjectGeneratorTests : IClassFixture<ForgeProjectFixture>
{
    private static readonly string[] RequiredSourceFiles =
    [
        "customer_cdc.csv",
        "customers.csv",
        "order_rows.csv",
        "orders.csv",
        "products.csv",
        "returns.csv",
        "reviews.csv",
        "shipment_events.csv",
        "shipments.csv",
        "stores.csv",
        "support_tickets.csv"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> RequiredSchemas =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["customer_cdc.csv"] = ["EventId", "Operation", "Sequence", "CustomerKey", "EventTime", "IngestedAt", "GivenName", "Surname", "Email", "City", "CountryCode", "LoyaltyTier"],
            ["customers.csv"] = ["CustomerKey", "GivenName", "Surname", "Email", "City", "CountryCode", "LoyaltyTier", "ValidFrom"],
            ["order_rows.csv"] = ["OrderKey", "LineNumber", "ProductKey", "Quantity", "UnitPrice", "NetPrice", "UnitCost"],
            ["orders.csv"] = ["OrderKey", "CustomerKey", "StoreKey", "OrderDate", "CurrencyCode", "OrderStatus"],
            ["products.csv"] = ["ProductKey", "ProductName", "Category", "Brand", "UnitPrice", "UnitCost"],
            ["returns.csv"] = ["ReturnKey", "OrderKey", "CustomerKey", "RequestedAt", "Reason", "ReturnStatus", "RefundAmount"],
            ["reviews.csv"] = ["ReviewKey", "OrderKey", "CustomerKey", "ProductKey", "ReviewedAt", "Rating", "ReviewText", "VerifiedPurchase"],
            ["shipment_events.csv"] = ["ShipmentEventKey", "ShipmentKey", "EventType", "EventTime", "IngestedAt", "Location"],
            ["shipments.csv"] = ["ShipmentKey", "OrderKey", "Carrier", "TrackingNumber", "ShippedAt", "PromisedAt", "DeliveredAt", "ShipmentStatus"],
            ["stores.csv"] = ["StoreKey", "StoreName", "Channel", "CountryCode"],
            ["support_tickets.csv"] = ["TicketKey", "OrderKey", "CustomerKey", "OpenedAt", "ClosedAt", "Channel", "Topic", "Priority", "SatisfactionScore"]
        };

    private readonly ForgeProjectFixture _fixture;

    public ForgeProjectGeneratorTests(ForgeProjectFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GenerateAsync_TwoRunsAreByteForByteDeterministicIncludingManifest()
    {
        var firstFiles = RelativeFiles(_fixture.FirstOutput);
        var secondFiles = RelativeFiles(_fixture.SecondOutput);

        Assert.Contains("truth_manifest.json", firstFiles);
        Assert.Equal(firstFiles, secondFiles);
        foreach (var relativePath in firstFiles)
        {
            var firstBytes = File.ReadAllBytes(Path.Combine(_fixture.FirstOutput, relativePath));
            var secondBytes = File.ReadAllBytes(Path.Combine(_fixture.SecondOutput, relativePath));
            Assert.True(
                firstBytes.SequenceEqual(secondBytes),
                $"Generated artifact '{relativePath}' was not byte-for-byte deterministic.");
        }

        Assert.Equal(_fixture.FirstResult.DatasetFingerprint, _fixture.SecondResult.DatasetFingerprint);
    }

    [Fact]
    public async Task GenerateAsync_ReplacesOwnedOutputsAndIgnoresDestinationHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"contoso-forge-dirty-output-{Guid.NewGuid():N}");
        var output = Path.Combine(root, "out");
        var lake = Path.Combine(root, "lake");
        try
        {
            var generator = new ForgeProjectGenerator();
            var spec = ForgeTestProject.CreateSmallSpec();
            var first = await generator.GenerateAsync(spec, output, lake);

            File.WriteAllText(Path.Combine(output, "data", "source", "stale.csv"), "stale\n");
            Directory.CreateDirectory(Path.Combine(output, "dbt", "target"));
            File.WriteAllText(Path.Combine(output, "dbt", "target", "stale.json"), "{}");
            File.WriteAllText(Path.Combine(output, "user-note.txt"), "preserve me");
            File.WriteAllText(Path.Combine(lake, "raw", "stale.csv"), "stale\n");

            var second = await generator.GenerateAsync(spec, output, lake);

            Assert.Equal(first.DatasetFingerprint, second.DatasetFingerprint);
            Assert.False(File.Exists(Path.Combine(output, "data", "source", "stale.csv")));
            Assert.False(Directory.Exists(Path.Combine(output, "dbt", "target")));
            Assert.False(File.Exists(Path.Combine(lake, "raw", "stale.csv")));
            Assert.True(File.Exists(Path.Combine(output, "user-note.txt")));
            Assert.Equal("contoso-forge-output-v1", File.ReadAllText(Path.Combine(output, ".contoso-forge-output")).Trim());
            Assert.Equal("contoso-forge-lake-v1", File.ReadAllText(Path.Combine(lake, ".contoso-forge-lake")).Trim());
            Assert.Equal(
                RequiredSourceFiles,
                Directory.GetFiles(Path.Combine(lake, "raw"), "*.csv")
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_RefusesAnUnownedNonEmptyOutputWithoutDeletingAnything()
    {
        var root = Path.Combine(Path.GetTempPath(), $"contoso-forge-unowned-output-{Guid.NewGuid():N}");
        var output = Path.Combine(root, "output");
        var sentinel = Path.Combine(output, "infra", "keep.tf");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            File.WriteAllText(sentinel, "must remain");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ForgeProjectGenerator().GenerateAsync(ForgeTestProject.CreateSmallSpec(), output));

            Assert.Contains("ownership marker", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("must remain", File.ReadAllText(sentinel));
            Assert.False(File.Exists(Path.Combine(output, ".contoso-forge-output")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_RefusesAnUnownedNonEmptyLakeWithoutDeletingRaw()
    {
        var root = Path.Combine(Path.GetTempPath(), $"contoso-forge-unowned-lake-{Guid.NewGuid():N}");
        var output = Path.Combine(root, "output");
        var lake = Path.Combine(root, "lake");
        var sentinel = Path.Combine(lake, "raw", "keep.csv");
        var outputSentinel = Path.Combine(output, "infra", "keep.tf");
        try
        {
            var generator = new ForgeProjectGenerator();
            var spec = ForgeTestProject.CreateSmallSpec();
            await generator.GenerateAsync(spec, output);
            File.WriteAllText(outputSentinel, "owned output must remain untouched");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            File.WriteAllText(sentinel, "must remain");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                generator.GenerateAsync(spec, output, lake));

            Assert.Contains("ownership marker", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("must remain", File.ReadAllText(sentinel));
            Assert.Equal("owned output must remain untouched", File.ReadAllText(outputSentinel));
            Assert.False(File.Exists(Path.Combine(lake, ".contoso-forge-lake")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GenerateAsync_EmitsRequiredSourceEntitiesWithStableSchemasAndValidForeignKeys()
    {
        var sourceRoot = _fixture.SourceRoot;
        var actualFiles = Directory.GetFiles(sourceRoot, "*.csv", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(RequiredSourceFiles, actualFiles);

        var tables = RequiredSourceFiles.ToDictionary(
            name => name,
            name => CsvTable.Read(Path.Combine(sourceRoot, name)),
            StringComparer.Ordinal);

        foreach (var (fileName, expectedHeaders) in RequiredSchemas)
        {
            Assert.Equal(expectedHeaders, tables[fileName].Headers);
            Assert.NotEmpty(tables[fileName].Rows);
        }

        var customerKeys = Values(tables["customers.csv"], "CustomerKey");
        var productKeys = Values(tables["products.csv"], "ProductKey");
        var storeKeys = Values(tables["stores.csv"], "StoreKey");
        var orderKeys = Values(tables["orders.csv"], "OrderKey");
        var shipmentKeys = Values(tables["shipments.csv"], "ShipmentKey");

        Assert.All(tables["orders.csv"].Rows, row =>
        {
            Assert.Contains(row["CustomerKey"], customerKeys);
            Assert.Contains(row["StoreKey"], storeKeys);
        });
        Assert.All(tables["order_rows.csv"].Rows, row =>
        {
            Assert.Contains(row["OrderKey"], orderKeys);
            Assert.Contains(row["ProductKey"], productKeys);
        });
        Assert.All(tables["shipments.csv"].Rows, row => Assert.Contains(row["OrderKey"], orderKeys));
        Assert.All(tables["shipment_events.csv"].Rows, row => Assert.Contains(row["ShipmentKey"], shipmentKeys));
        Assert.All(tables["returns.csv"].Rows, row =>
        {
            Assert.Contains(row["OrderKey"], orderKeys);
            Assert.Contains(row["CustomerKey"], customerKeys);
        });
        Assert.All(tables["support_tickets.csv"].Rows, row =>
        {
            Assert.Contains(row["OrderKey"], orderKeys);
            Assert.Contains(row["CustomerKey"], customerKeys);
        });
        Assert.All(tables["reviews.csv"].Rows, row =>
        {
            Assert.Contains(row["OrderKey"], orderKeys);
            Assert.Contains(row["CustomerKey"], customerKeys);
            Assert.Contains(row["ProductKey"], productKeys);
        });
    }

    [Fact]
    public void TruthManifest_RecordsDataBackedEvidenceForEveryRequiredInjector()
    {
        var manifest = _fixture.ReadManifest();
        var evidence = manifest.Evidence.ToDictionary(item => item.EvidenceId, StringComparer.Ordinal);
        Assert.Equal(
            [
                "EV-CDC-I", "EV-CDC-U", "EV-CDC-D", "EV-DUP-CDC",
                "EV-DUP-SHIPMENT-EVENT", "EV-LATE-ARRIVAL", "EV-SCD2",
                "EV-QUALITY-NULL", "EV-QUALITY-RANGE"
            ],
            manifest.Evidence.Select(item => item.EvidenceId));

        var shipmentEvents = _fixture.ReadSource("shipment_events.csv");
        var duplicateShipmentEvent = evidence["EV-DUP-SHIPMENT-EVENT"];
        Assert.Equal("duplicate", duplicateShipmentEvent.Injector);
        Assert.Equal("ShipmentEvent", duplicateShipmentEvent.Entity);
        Assert.Equal("2", duplicateShipmentEvent.Details["rawCopies"]);
        Assert.Equal(2, shipmentEvents.Rows.Count(row => row["ShipmentEventKey"] == duplicateShipmentEvent.RecordKeys.Single()));

        var customerCdc = _fixture.ReadSource("customer_cdc.csv");
        var duplicateCdc = evidence["EV-DUP-CDC"];
        Assert.Equal("duplicate", duplicateCdc.Injector);
        Assert.Equal("CustomerCdc", duplicateCdc.Entity);
        Assert.Equal("2", duplicateCdc.Details["rawCopies"]);
        Assert.Equal(2, customerCdc.Rows.Count(row => row["EventId"] == duplicateCdc.RecordKeys.Single()));

        AssertCdcEvidence(evidence["EV-CDC-I"], customerCdc, "I", 1);
        AssertCdcEvidence(evidence["EV-CDC-U"], customerCdc, "U", 2);
        AssertCdcEvidence(evidence["EV-CDC-D"], customerCdc, "D", 3);

        var lateEvidence = evidence["EV-LATE-ARRIVAL"];
        var lateRow = Assert.Single(shipmentEvents.Rows, row => row["ShipmentEventKey"] == lateEvidence.RecordKeys.Single());
        var eventTime = DateTimeOffset.Parse(lateRow["EventTime"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var ingestedAt = DateTimeOffset.Parse(lateRow["IngestedAt"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        Assert.Equal("late_arrival", lateEvidence.Injector);
        Assert.Equal("72", lateEvidence.Details["ingestionLagHours"]);
        Assert.Equal(TimeSpan.FromHours(72), ingestedAt - eventTime);

        var customers = _fixture.ReadSource("customers.csv");
        var scd2Evidence = evidence["EV-SCD2"];
        var originalCustomer = Assert.Single(customers.Rows, row => row["CustomerKey"] == "1");
        var updateEventId = scd2Evidence.RecordKeys.Single(key => key.StartsWith("customer-", StringComparison.Ordinal));
        var updateRows = customerCdc.Rows.Where(row => row["EventId"] == updateEventId).ToArray();
        Assert.NotEmpty(updateRows);
        Assert.All(updateRows, row =>
        {
            Assert.Equal("U", row["Operation"]);
            Assert.Equal("1", row["CustomerKey"]);
            Assert.NotEqual(originalCustomer["City"], row["City"]);
            Assert.NotEqual(originalCustomer["LoyaltyTier"], row["LoyaltyTier"]);
            Assert.Equal("Basel", row["City"]);
            Assert.Equal("Platinum", row["LoyaltyTier"]);
        });
        Assert.Equal("City,LoyaltyTier", scd2Evidence.Details["changedAttributes"]);

        var shipments = _fixture.ReadSource("shipments.csv");
        var nullEvidence = evidence["EV-QUALITY-NULL"];
        var nullTrackingRow = Assert.Single(shipments.Rows, row => row["ShipmentKey"] == nullEvidence.RecordKeys.Single());
        Assert.Equal("quality", nullEvidence.Injector);
        Assert.Equal("TrackingNumber", nullEvidence.Details["field"]);
        Assert.Equal(string.Empty, nullTrackingRow["TrackingNumber"]);

        var reviews = _fixture.ReadSource("reviews.csv");
        var rangeEvidence = evidence["EV-QUALITY-RANGE"];
        var invalidReview = Assert.Single(reviews.Rows, row => row["ReviewKey"] == rangeEvidence.RecordKeys.Single());
        Assert.Equal("quality", rangeEvidence.Injector);
        Assert.Equal("Rating", rangeEvidence.Details["field"]);
        Assert.Equal("7", rangeEvidence.Details["badValue"]);
        Assert.Equal("7", invalidReview["Rating"]);
    }

    [Fact]
    public void GenerateAsync_MaterializesSourceToRawByteForByteAndCreatesCanonicalLakeLayers()
    {
        var rawRoot = Path.Combine(_fixture.LakeRoot, "raw");
        Assert.True(File.Exists(Path.Combine(rawRoot, ".gitkeep")));
        Assert.Equal(RequiredSourceFiles, Directory.GetFiles(rawRoot, "*.csv")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray());

        foreach (var fileName in RequiredSourceFiles)
        {
            var sourceBytes = File.ReadAllBytes(Path.Combine(_fixture.SourceRoot, fileName));
            var rawBytes = File.ReadAllBytes(Path.Combine(rawRoot, fileName));
            Assert.True(sourceBytes.SequenceEqual(rawBytes), $"lake/raw/{fileName} differs from data/source/{fileName}.");
        }

        foreach (var layer in new[] { "raw", "bronze", "silver", "gold", "checkpoints" })
            Assert.True(Directory.Exists(Path.Combine(_fixture.LakeRoot, layer)), $"Lake layer '{layer}' was not created.");
    }

    [Fact]
    public void GenerateAsync_EmitsTheV1ArtifactTreeWithExplicitStatusMarkers()
    {
        var requiredFiles = new[]
        {
            "project.json",
            "truth_manifest.json",
            "models/source_model.json",
            "models/gold_model.json",
            "models/kpi_catalog.json",
            "models/semantic_model.json",
            "models/ml_spec.json",
            "sql/customer_satisfaction_reference.sql",
            "pyspark/bronze_silver.py",
            "pyspark/README.md",
            "dbt/dbt_project.yml",
            "dbt/profiles.yml",
            "dbt/packages.yml",
            "dbt/README.md",
            "dbt/models/sources.yml",
            "airflow/dags/contoso_forge_customer_satisfaction.py",
            "airflow/README.md",
            "pipeline/pipeline.json",
            "fabric/README.md",
            "adf/README.md",
            "databricks/README.md",
            "gcp/README.md",
            "infra/README.md"
        };
        Assert.All(requiredFiles, relativePath =>
            Assert.True(File.Exists(_fixture.OutputPath(relativePath)), $"Required artifact '{relativePath}' was not generated."));

        Assert.NotEmpty(Directory.GetFiles(_fixture.OutputPath("dbt/models/staging"), "*.sql"));
        Assert.NotEmpty(Directory.GetFiles(_fixture.OutputPath("dbt/models/gold"), "*.sql"));
        Assert.NotEmpty(Directory.GetFiles(_fixture.OutputPath("dbt/tests"), "*.sql"));

        var jsonStatuses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["models/source_model.json"] = "validated",
            ["models/gold_model.json"] = "validated",
            ["models/kpi_catalog.json"] = "validated",
            ["models/semantic_model.json"] = "starter/reference",
            ["models/ml_spec.json"] = "starter/reference",
            ["pipeline/pipeline.json"] = "validated"
        };
        foreach (var (relativePath, expectedStatus) in jsonStatuses)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_fixture.OutputPath(relativePath)));
            Assert.Equal(expectedStatus, document.RootElement.GetProperty("artifactStatus").GetString());
        }

        AssertTextStatus("sql/customer_satisfaction_reference.sql", "starter/reference");
        AssertTextStatus("pyspark/bronze_silver.py", "validated");
        AssertTextStatus("pyspark/README.md", "validated");
        AssertTextStatus("airflow/dags/contoso_forge_customer_satisfaction.py", "validated");
        AssertTextStatus("airflow/README.md", "validated");
        foreach (var dbtArtifact in Directory.GetFiles(_fixture.OutputPath("dbt"), "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(_fixture.FirstOutput, dbtArtifact).Replace('\\', '/');
            AssertTextStatus(relativePath, "validated");
        }
        foreach (var exporter in new[] { "fabric", "adf", "databricks", "gcp", "infra" })
            AssertTextStatus($"{exporter}/README.md", "starter/reference");

        void AssertTextStatus(string relativePath, string expectedStatus)
        {
            var text = File.ReadAllText(_fixture.OutputPath(relativePath));
            Assert.Contains(expectedStatus, text, StringComparison.OrdinalIgnoreCase);
            Assert.True(
                text.Contains("artifact-status", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("artifact status", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("artifactStatus", StringComparison.Ordinal),
                $"Artifact '{relativePath}' does not carry an explicit status marker.");
        }
    }

    [Fact]
    public void GeneratedAirflowDag_MatchesPipelineSpecRetryContract()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(_fixture.OutputPath("pipeline/pipeline.json")));
        var dbtTest = document.RootElement.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("id").GetString() == "dbt_test");
        var retry = dbtTest.GetProperty("retry");
        Assert.Equal(1, retry.GetProperty("maximumAttempts").GetInt32());
        Assert.Equal(0, retry.GetProperty("backoffSeconds").GetInt32());

        var dag = File.ReadAllText(_fixture.OutputPath("airflow/dags/contoso_forge_customer_satisfaction.py"));
        Assert.Contains("default_args={\"retries\": 1", dag, StringComparison.Ordinal);
        Assert.Contains("task_id=\"dbt_test\"", dag, StringComparison.Ordinal);
        Assert.Contains("retries=0", dag, StringComparison.Ordinal);
    }

    [Fact]
    public void TruthManifest_ReconcilesSourceRowCountsHashesAndFingerprints()
    {
        var manifest = _fixture.ReadManifest();
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("validated", manifest.ArtifactStatus);
        Assert.Equal("retail.customer_satisfaction", manifest.Scenario);
        Assert.Equal(_fixture.Spec.Generation.Seed, manifest.Seed);
        Assert.Equal(_fixture.FirstResult.DatasetFingerprint, manifest.DatasetFingerprint);
        Assert.True(manifest.Invariants.Deterministic);
        Assert.True(manifest.Invariants.ForeignKeysValid);
        Assert.Contains("byte-for-byte", manifest.Invariants.RawToLakeContract, StringComparison.Ordinal);

        var sourceFiles = Directory.GetFiles(_fixture.SourceRoot, "*.csv", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(sourceFiles.Select(Path.GetFileName), manifest.SourceFileSha256.Keys);
        Assert.Equal(sourceFiles.Select(path => Path.GetFileNameWithoutExtension(path)), manifest.SourceRowCounts.Keys);

        foreach (var sourceFile in sourceFiles)
        {
            var fileName = Path.GetFileName(sourceFile);
            var entityName = Path.GetFileNameWithoutExtension(sourceFile);
            Assert.Equal(CsvTable.Read(sourceFile).Rows.Count, manifest.SourceRowCounts[entityName]);
            Assert.Equal(Sha256File(sourceFile), manifest.SourceFileSha256[fileName]);
        }

        var canonicalHashes = string.Join("\n", manifest.SourceFileSha256.Select(pair => $"{pair.Key}:{pair.Value}"));
        Assert.Equal(Sha256Text(canonicalHashes), manifest.DatasetFingerprint);

        var normalizedProject = File.ReadAllText(_fixture.OutputPath("project.json"))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd() + "\n";
        Assert.Equal(Sha256Text(normalizedProject), manifest.ProjectFingerprint);

        Assert.Equal(manifest.SourceRowCounts["customer_cdc"] - 1, manifest.ExpectedSilverRowCounts["customer_cdc"]);
        Assert.Equal(manifest.SourceRowCounts["shipment_events"] - 1, manifest.ExpectedSilverRowCounts["shipment_events"]);
        Assert.Equal(manifest.SourceRowCounts["shipments"] - 1, manifest.ExpectedSilverRowCounts["shipments"]);
        Assert.Equal(manifest.SourceRowCounts["reviews"] - 1, manifest.ExpectedSilverRowCounts["reviews"]);
        Assert.Equal(manifest.SourceRowCounts["customers"] + 2, manifest.ExpectedSilverRowCounts["customer_scd2"]);
        Assert.Equal(2, manifest.ExpectedSilverRowCounts["quality_issues"]);
        Assert.Equal(
            new[]
            {
                "customer_cdc", "customer_scd2", "customers", "order_rows", "orders", "products",
                "quality_issues", "returns", "reviews", "shipment_events", "shipments", "stores", "support_tickets"
            },
            manifest.ExpectedSilverRowCounts.Keys);
        Assert.Equal(_fixture.Spec.Generation.Orders, manifest.SourceRowCounts["orders"]);
    }

    private static void AssertCdcEvidence(TruthEvidence evidence, CsvTable customerCdc, string operation, int sequence)
    {
        Assert.Equal("cdc", evidence.Injector);
        Assert.Equal("Customer", evidence.Entity);
        Assert.Equal(operation, evidence.Details["operation"]);
        var rows = customerCdc.Rows.Where(row => row["EventId"] == evidence.RecordKeys.Single()).ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.Equal(operation, row["Operation"]);
            Assert.Equal(sequence.ToString(CultureInfo.InvariantCulture), row["Sequence"]);
        });
    }

    private static HashSet<string> Values(CsvTable table, string column) =>
        table.Rows.Select(row => row[column]).ToHashSet(StringComparer.Ordinal);

    private static string[] RelativeFiles(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string Sha256File(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string Sha256Text(string value) =>
        Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(value))).ToLowerInvariant();
}

public sealed class ForgeProjectFixture : IDisposable
{
    public ForgeProjectFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), $"contoso-forge-contract-{Guid.NewGuid():N}");
        FirstOutput = Path.Combine(Root, "first");
        SecondOutput = Path.Combine(Root, "second");
        LakeRoot = Path.Combine(Root, "lake");
        Directory.CreateDirectory(Root);

        Spec = ForgeTestProject.CreateSmallSpec();
        var generator = new ForgeProjectGenerator();
        FirstResult = generator.GenerateAsync(Spec, FirstOutput, LakeRoot).GetAwaiter().GetResult();
        SecondResult = generator.GenerateAsync(Spec, SecondOutput).GetAwaiter().GetResult();
    }

    public string Root { get; }
    public string FirstOutput { get; }
    public string SecondOutput { get; }
    public string LakeRoot { get; }
    public string SourceRoot => Path.Combine(FirstOutput, "data", "source");
    public ProjectSpec Spec { get; }
    public ForgeGenerationResult FirstResult { get; }
    public ForgeGenerationResult SecondResult { get; }

    public string OutputPath(string relativePath) =>
        Path.Combine(FirstOutput, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public CsvTable ReadSource(string fileName) => CsvTable.Read(Path.Combine(SourceRoot, fileName));

    public TruthManifest ReadManifest() =>
        JsonSerializer.Deserialize<TruthManifest>(
            File.ReadAllText(OutputPath("truth_manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("truth_manifest.json was empty.");

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

internal static class ForgeTestProject
{
    public static ProjectSpec CreateSmallSpec() =>
        new()
        {
            Name = "customer-satisfaction-contract-tests",
            Scenario = "retail.customer_satisfaction",
            Generation = new GenerationSpec
            {
                LegacyContosoMode = true,
                Seed = 2_026_0904,
                Orders = 12,
                Customers = 8,
                Products = 4,
                Stores = 2,
                StartDate = "2024-01-01T00:00:00Z",
                Formats = ["CSV"]
            }
        };
}

public sealed class CsvTable
{
    private CsvTable(string[] headers, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        Headers = headers;
        Rows = rows;
    }

    public string[] Headers { get; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; }

    public static CsvTable Read(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
            throw new InvalidDataException($"CSV '{path}' has no header.");

        var headers = ParseLine(lines[0]);
        var rows = new List<IReadOnlyDictionary<string, string>>(Math.Max(0, lines.Length - 1));
        for (var lineNumber = 1; lineNumber < lines.Length; lineNumber++)
        {
            var values = ParseLine(lines[lineNumber]);
            if (values.Length != headers.Length)
                throw new InvalidDataException($"CSV '{path}' line {lineNumber + 1} has {values.Length} fields; expected {headers.Length}.");

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < headers.Length; index++)
                row.Add(headers[index], values[index]);
            rows.Add(row);
        }

        return new CsvTable(headers, rows);
    }

    private static string[] ParseLine(string line)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                values.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        if (inQuotes)
            throw new InvalidDataException("CSV line contains an unterminated quoted field.");
        values.Add(value.ToString());
        return values.ToArray();
    }
}
