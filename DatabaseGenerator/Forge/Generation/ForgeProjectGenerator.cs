#nullable enable

using DatabaseGenerator.Forge.Specs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DatabaseGenerator.Forge.Generation;

public sealed class ForgeGenerationResult
{
    public required string OutputRoot { get; init; }
    public string? LakeRoot { get; init; }
    public required string DatasetFingerprint { get; init; }
}

public sealed class ForgeProjectGenerator
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private const string OutputMarkerName = ".contoso-forge-output";
    private const string OutputMarkerContent = "contoso-forge-output-v1";
    private static readonly string[] OwnedOutputDirectories =
    [
        Path.Combine("data", "source"),
        "models",
        "sql",
        "pyspark",
        "dbt",
        "airflow",
        "pipeline",
        "fabric",
        "adf",
        "databricks",
        "gcp",
        "infra"
    ];

    public Task<ForgeGenerationResult> GenerateAsync(ProjectSpec spec, string outputRoot, string? lakeRoot = null)
    {
        spec.Validate();
        var absoluteOutputRoot = Path.GetFullPath(outputRoot);
        ValidateOwnedOutput(absoluteOutputRoot);
        var absoluteLakeRoot = string.IsNullOrWhiteSpace(lakeRoot) ? null : Path.GetFullPath(lakeRoot);
        if (absoluteLakeRoot is not null)
            ForgeIo.ValidateLakeRootForMaterialization(absoluteLakeRoot);

        ResetOwnedOutput(absoluteOutputRoot);
        var sourceRoot = Path.Combine(absoluteOutputRoot, "data", "source");
        Directory.CreateDirectory(sourceRoot);

        var generated = GenerateRows(spec);
        WriteSourceFiles(sourceRoot, generated);

        var normalizedProjectJson = JsonSerializer.Serialize(spec, ForgeJsonContext.Default.ProjectSpec);
        ForgeIo.WriteText(Path.Combine(absoluteOutputRoot, "project.json"), normalizedProjectJson);
        CopyArtifacts(spec, absoluteOutputRoot);

        var manifest = BuildTruthManifest(spec, normalizedProjectJson, sourceRoot, generated);
        var manifestJson = JsonSerializer.Serialize(manifest, ForgeJsonContext.Default.TruthManifest);
        ForgeIo.WriteText(Path.Combine(absoluteOutputRoot, "truth_manifest.json"), manifestJson);

        if (absoluteLakeRoot is not null)
        {
            ForgeIo.MaterializeRaw(sourceRoot, absoluteLakeRoot);
            foreach (var layer in new[] { "bronze", "silver", "gold", "checkpoints" })
                Directory.CreateDirectory(Path.Combine(absoluteLakeRoot, layer));
        }

        return Task.FromResult(new ForgeGenerationResult
        {
            OutputRoot = absoluteOutputRoot,
            LakeRoot = absoluteLakeRoot,
            DatasetFingerprint = manifest.DatasetFingerprint
        });
    }

    private static void ValidateOwnedOutput(string outputRoot)
    {
        var hasEntries = Directory.Exists(outputRoot) && Directory.EnumerateFileSystemEntries(outputRoot).Any();
        var markerPath = Path.Combine(outputRoot, OutputMarkerName);
        var hasValidMarker = File.Exists(markerPath) &&
            string.Equals(File.ReadAllText(markerPath).Trim(), OutputMarkerContent, StringComparison.Ordinal);
        if (hasEntries && !hasValidMarker)
            throw new InvalidOperationException(
                $"Refusing to reset non-empty output directory without a valid Forge ownership marker: {outputRoot}");
    }

    private static void ResetOwnedOutput(string outputRoot)
    {
        Directory.CreateDirectory(outputRoot);
        var markerPath = Path.Combine(outputRoot, OutputMarkerName);
        ForgeIo.WriteText(markerPath, OutputMarkerContent);
        foreach (var relativePath in OwnedOutputDirectories)
        {
            var path = Path.GetFullPath(Path.Combine(outputRoot, relativePath));
            var relative = Path.GetRelativePath(outputRoot, path);
            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                throw new InvalidOperationException($"Refusing to reset generated path outside the output root: {path}");
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }

        foreach (var fileName in new[] { "project.json", "truth_manifest.json" })
        {
            var path = Path.Combine(outputRoot, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static CustomerSatisfactionData GenerateRows(ProjectSpec spec)
    {
        var rng = new StableRandom(spec.Generation.Seed);
        var epoch = DateTimeOffset.Parse(
            spec.Generation.StartDate,
            Invariant,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        var givenNames = new[] { "Avery", "Casey", "Emery", "Frankie", "Harper", "Jamie", "Jordan", "Kai", "Morgan", "Riley", "Robin", "Taylor" };
        var surnames = new[] { "Bauer", "Conti", "Dubois", "Fischer", "Garcia", "Keller", "Martin", "Novak", "Rossi", "Smith", "Taylor", "Weber" };
        var locations = new[]
        {
            (City: "Zurich", Country: "CH"), (City: "Berlin", Country: "DE"),
            (City: "Paris", Country: "FR"), (City: "Milan", Country: "IT"),
            (City: "London", Country: "GB"), (City: "Seattle", Country: "US")
        };
        var tiers = new[] { "Bronze", "Silver", "Gold" };
        var categories = new[] { "Audio", "Computers", "Home", "Mobile" };
        var brands = new[] { "A. Datum", "Contoso", "Fabrikam", "Northwind" };

        var data = new CustomerSatisfactionData { GenerationEpoch = epoch };
        for (var index = 0; index < spec.Generation.Customers; index++)
        {
            var customerKey = index + 1;
            var location = locations[(index + rng.NextInt(locations.Length)) % locations.Length];
            var givenName = givenNames[(index + rng.NextInt(givenNames.Length)) % givenNames.Length];
            var surname = surnames[(index * 3 + rng.NextInt(surnames.Length)) % surnames.Length];
            data.Customers.Add(new CustomerRow(
                customerKey,
                givenName,
                surname,
                $"{givenName.ToLowerInvariant()}.{surname.ToLowerInvariant()}.{customerKey}@example.test",
                location.City,
                location.Country,
                tiers[index % tiers.Length],
                epoch.AddDays(-365)));
        }

        for (var index = 0; index < spec.Generation.Products; index++)
        {
            var price = rng.NextMoney(12m, 480m);
            data.Products.Add(new ProductRow(
                index + 1,
                $"Contoso Product {index + 1:000}",
                categories[index % categories.Length],
                brands[(index + 1) % brands.Length],
                price,
                decimal.Round(price * 0.62m, 2, MidpointRounding.AwayFromZero)));
        }

        for (var index = 0; index < spec.Generation.Stores; index++)
        {
            var location = locations[index % locations.Length];
            data.Stores.Add(new StoreRow(index + 1, $"{location.City} Store", index == 0 ? "Online" : "Retail", location.Country));
        }

        for (var index = 0; index < spec.Generation.Orders; index++)
        {
            var orderKey = 100_001L + index;
            var customerKey = 1 + rng.NextInt(spec.Generation.Customers);
            var storeKey = 1 + rng.NextInt(spec.Generation.Stores);
            var orderDate = epoch.AddDays(index % (spec.Generation.TimeSpanDays ?? 60)).AddHours(8 + rng.NextInt(10));
            data.Orders.Add(new OrderRow(orderKey, customerKey, storeKey, orderDate, "USD", "Completed"));

            var lineCount = 1 + rng.NextInt(3);
            for (var line = 1; line <= lineCount; line++)
            {
                var product = data.Products[rng.NextInt(data.Products.Count)];
                var quantity = 1 + rng.NextInt(4);
                var discountPercent = rng.NextInt(5) * 0.05m;
                data.OrderLines.Add(new OrderLineRow(
                    orderKey,
                    line,
                    product.ProductKey,
                    quantity,
                    product.UnitPrice,
                    decimal.Round(product.UnitPrice * (1m - discountPercent), 2, MidpointRounding.AwayFromZero),
                    product.UnitCost));
            }

            var shipmentKey = 200_001L + index;
            var shippedAt = orderDate.AddDays(1);
            var promisedAt = shippedAt.AddDays(4);
            var deliveredAt = shippedAt.AddDays(2 + rng.NextInt(5));
            var tracking = index == 4 ? null : $"CF{shipmentKey:000000000}";
            data.Shipments.Add(new ShipmentRow(
                shipmentKey,
                orderKey,
                new[] { "Alpine Parcel", "Contoso Express", "Northwind Logistics" }[index % 3],
                tracking,
                shippedAt,
                promisedAt,
                deliveredAt,
                "Delivered"));

            var baseEventKey = 300_000L + (index * 10L);
            data.ShipmentEvents.Add(new ShipmentEventRow(baseEventKey + 1, shipmentKey, "SHIPPED", shippedAt, shippedAt.AddHours(1), "Origin facility"));
            data.ShipmentEvents.Add(new ShipmentEventRow(baseEventKey + 2, shipmentKey, "IN_TRANSIT", shippedAt.AddDays(1), shippedAt.AddDays(1).AddHours(1), "Regional hub"));
            var deliveredIngestedAt = index == 2 ? deliveredAt.AddHours(72) : deliveredAt.AddHours(1);
            data.ShipmentEvents.Add(new ShipmentEventRow(baseEventKey + 3, shipmentKey, "DELIVERED", deliveredAt, deliveredIngestedAt, "Destination"));

            if (index % 9 == 5)
            {
                var refund = data.OrderLines.Where(line => line.OrderKey == orderKey).Sum(line => line.NetPrice * line.Quantity);
                data.Returns.Add(new ReturnRow(400_001L + data.Returns.Count, orderKey, customerKey, deliveredAt.AddDays(3),
                    new[] { "Damaged", "Not as expected", "Wrong item" }[data.Returns.Count % 3], "Refunded", refund));
            }

            if (index % 7 == 3)
            {
                var openedAt = deliveredAt.AddHours(6);
                data.SupportTickets.Add(new SupportTicketRow(
                    500_001L + data.SupportTickets.Count,
                    orderKey,
                    customerKey,
                    openedAt,
                    openedAt.AddHours(4 + rng.NextInt(44)),
                    new[] { "Chat", "Email", "Phone" }[data.SupportTickets.Count % 3],
                    deliveredAt > promisedAt ? "Late delivery" : "Product question",
                    deliveredAt > promisedAt ? "High" : "Normal",
                    deliveredAt > promisedAt ? 2 : 4));
            }

            if (index % 3 == 1)
            {
                var firstProductKey = data.OrderLines.First(line => line.OrderKey == orderKey).ProductKey;
                var hasReturn = data.Returns.Any(item => item.OrderKey == orderKey);
                var hasTicket = data.SupportTickets.Any(item => item.OrderKey == orderKey);
                var penalty = (deliveredAt > promisedAt ? 2 : 0) + (hasReturn ? 1 : 0) + (hasTicket ? 1 : 0);
                var rating = Math.Clamp(5 - penalty + (rng.NextInt(3) - 1), 1, 5);
                data.Reviews.Add(new ReviewRow(
                    600_001L + data.Reviews.Count,
                    orderKey,
                    customerKey,
                    firstProductKey,
                    deliveredAt.AddDays(2),
                    rating,
                    rating >= 4 ? "Satisfied with the order" : "The experience needs improvement",
                    true));
            }
        }

        // Exact duplicate source row. Silver must retain one record by ShipmentEventKey.
        data.DuplicateShipmentEventKey = data.ShipmentEvents[4].ShipmentEventKey;
        data.ShipmentEvents.Add(data.ShipmentEvents[4] with { });

        // One deterministic invalid rating. Silver quarantines it rather than changing source truth.
        data.InvalidReviewKey = data.Reviews[1].ReviewKey;
        data.Reviews[1] = data.Reviews[1] with { Rating = 7 };
        data.NullTrackingShipmentKey = data.Shipments[4].ShipmentKey;
        data.LateShipmentEventKey = data.ShipmentEvents[8].ShipmentEventKey;

        var insertedKey = spec.Generation.Customers + 1;
        var cdcEpoch = epoch.AddDays(30);
        data.CustomerCdc.Add(new CustomerCdcRow(
            "customer-0001-I", "I", 1, insertedKey, cdcEpoch, cdcEpoch.AddMinutes(5),
            "Alex", "Insert", $"alex.insert.{insertedKey}@example.test", "Geneva", "CH", "Bronze"));
        var original = data.Customers[0];
        data.CustomerCdc.Add(new CustomerCdcRow(
            "customer-0001-U", "U", 2, original.CustomerKey, cdcEpoch.AddDays(1), cdcEpoch.AddDays(1).AddMinutes(5),
            original.GivenName, original.Surname, original.Email, "Basel", original.CountryCode, "Platinum"));
        data.CustomerCdc.Add(new CustomerCdcRow(
            "customer-0001-D", "D", 3, insertedKey, cdcEpoch.AddDays(2), cdcEpoch.AddDays(2).AddMinutes(5),
            "Alex", "Insert", $"alex.insert.{insertedKey}@example.test", "Geneva", "CH", "Bronze"));
        data.DuplicateCdcEventId = data.CustomerCdc[1].EventId;
        data.CustomerCdc.Add(data.CustomerCdc[1] with { });

        ValidateForeignKeys(data);
        return data;
    }

    private static void ValidateForeignKeys(CustomerSatisfactionData data)
    {
        var customers = data.Customers.Select(row => row.CustomerKey).ToHashSet();
        var products = data.Products.Select(row => row.ProductKey).ToHashSet();
        var stores = data.Stores.Select(row => row.StoreKey).ToHashSet();
        var orders = data.Orders.Select(row => row.OrderKey).ToHashSet();
        var shipments = data.Shipments.Select(row => row.ShipmentKey).ToHashSet();

        if (data.Orders.Any(row => !customers.Contains(row.CustomerKey) || !stores.Contains(row.StoreKey)) ||
            data.OrderLines.Any(row => !orders.Contains(row.OrderKey) || !products.Contains(row.ProductKey)) ||
            data.Shipments.Any(row => !orders.Contains(row.OrderKey)) ||
            data.ShipmentEvents.Any(row => !shipments.Contains(row.ShipmentKey)) ||
            data.Returns.Any(row => !orders.Contains(row.OrderKey) || !customers.Contains(row.CustomerKey)) ||
            data.SupportTickets.Any(row => !orders.Contains(row.OrderKey) || !customers.Contains(row.CustomerKey)) ||
            data.Reviews.Any(row => !orders.Contains(row.OrderKey) || !customers.Contains(row.CustomerKey) || !products.Contains(row.ProductKey)))
            throw new InvalidOperationException("Generated customer-satisfaction data contains an invalid foreign key.");
    }

    private static void WriteSourceFiles(string root, CustomerSatisfactionData data)
    {
        DeterministicCsv.Write(Path.Combine(root, "customers.csv"),
            new[] { "CustomerKey", "GivenName", "Surname", "Email", "City", "CountryCode", "LoyaltyTier", "ValidFrom" },
            data.Customers.Select(row => Cells(row.CustomerKey, row.GivenName, row.Surname, row.Email, row.City, row.CountryCode, row.LoyaltyTier, Timestamp(row.ValidFrom))));
        DeterministicCsv.Write(Path.Combine(root, "products.csv"),
            new[] { "ProductKey", "ProductName", "Category", "Brand", "UnitPrice", "UnitCost" },
            data.Products.Select(row => Cells(row.ProductKey, row.ProductName, row.Category, row.Brand, Money(row.UnitPrice), Money(row.UnitCost))));
        DeterministicCsv.Write(Path.Combine(root, "stores.csv"),
            new[] { "StoreKey", "StoreName", "Channel", "CountryCode" },
            data.Stores.Select(row => Cells(row.StoreKey, row.StoreName, row.Channel, row.CountryCode)));
        DeterministicCsv.Write(Path.Combine(root, "orders.csv"),
            new[] { "OrderKey", "CustomerKey", "StoreKey", "OrderDate", "CurrencyCode", "OrderStatus" },
            data.Orders.Select(row => Cells(row.OrderKey, row.CustomerKey, row.StoreKey, Timestamp(row.OrderDate), row.CurrencyCode, row.OrderStatus)));
        DeterministicCsv.Write(Path.Combine(root, "order_rows.csv"),
            new[] { "OrderKey", "LineNumber", "ProductKey", "Quantity", "UnitPrice", "NetPrice", "UnitCost" },
            data.OrderLines.Select(row => Cells(row.OrderKey, row.LineNumber, row.ProductKey, row.Quantity, Money(row.UnitPrice), Money(row.NetPrice), Money(row.UnitCost))));
        DeterministicCsv.Write(Path.Combine(root, "shipments.csv"),
            new[] { "ShipmentKey", "OrderKey", "Carrier", "TrackingNumber", "ShippedAt", "PromisedAt", "DeliveredAt", "ShipmentStatus" },
            data.Shipments.Select(row => Cells(row.ShipmentKey, row.OrderKey, row.Carrier, row.TrackingNumber, Timestamp(row.ShippedAt), Timestamp(row.PromisedAt), Timestamp(row.DeliveredAt), row.ShipmentStatus)));
        DeterministicCsv.Write(Path.Combine(root, "shipment_events.csv"),
            new[] { "ShipmentEventKey", "ShipmentKey", "EventType", "EventTime", "IngestedAt", "Location" },
            data.ShipmentEvents.Select(row => Cells(row.ShipmentEventKey, row.ShipmentKey, row.EventType, Timestamp(row.EventTime), Timestamp(row.IngestedAt), row.Location)));
        DeterministicCsv.Write(Path.Combine(root, "returns.csv"),
            new[] { "ReturnKey", "OrderKey", "CustomerKey", "RequestedAt", "Reason", "ReturnStatus", "RefundAmount" },
            data.Returns.Select(row => Cells(row.ReturnKey, row.OrderKey, row.CustomerKey, Timestamp(row.RequestedAt), row.Reason, row.ReturnStatus, Money(row.RefundAmount))));
        DeterministicCsv.Write(Path.Combine(root, "support_tickets.csv"),
            new[] { "TicketKey", "OrderKey", "CustomerKey", "OpenedAt", "ClosedAt", "Channel", "Topic", "Priority", "SatisfactionScore" },
            data.SupportTickets.Select(row => Cells(row.TicketKey, row.OrderKey, row.CustomerKey, Timestamp(row.OpenedAt), Timestamp(row.ClosedAt), row.Channel, row.Topic, row.Priority, row.SatisfactionScore)));
        DeterministicCsv.Write(Path.Combine(root, "reviews.csv"),
            new[] { "ReviewKey", "OrderKey", "CustomerKey", "ProductKey", "ReviewedAt", "Rating", "ReviewText", "VerifiedPurchase" },
            data.Reviews.Select(row => Cells(row.ReviewKey, row.OrderKey, row.CustomerKey, row.ProductKey, Timestamp(row.ReviewedAt), row.Rating, row.ReviewText, row.VerifiedPurchase.ToString().ToLowerInvariant())));
        DeterministicCsv.Write(Path.Combine(root, "customer_cdc.csv"),
            new[] { "EventId", "Operation", "Sequence", "CustomerKey", "EventTime", "IngestedAt", "GivenName", "Surname", "Email", "City", "CountryCode", "LoyaltyTier" },
            data.CustomerCdc.Select(row => Cells(row.EventId, row.Operation, row.Sequence, row.CustomerKey, Timestamp(row.EventTime), Timestamp(row.IngestedAt), row.GivenName, row.Surname, row.Email, row.City, row.CountryCode, row.LoyaltyTier)));
    }

    private static void CopyArtifacts(ProjectSpec spec, string outputRoot)
    {
        var templateRoot = Path.Combine(AppContext.BaseDirectory, "Forge", "Templates", "customer_satisfaction");
        ForgeIo.CopyTreeWithTokens(templateRoot, outputRoot, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__PROJECT_NAME__"] = spec.Name,
            ["__SCENARIO__"] = spec.Scenario,
            ["__SEED__"] = spec.Generation.Seed.ToString(Invariant),
            ["__EXPECTED_ORDER_COUNT__"] = spec.Generation.Orders.ToString(Invariant)
        });
    }

    private static TruthManifest BuildTruthManifest(
        ProjectSpec spec,
        string projectJson,
        string sourceRoot,
        CustomerSatisfactionData data)
    {
        var manifest = new TruthManifest
        {
            Seed = spec.Generation.Seed,
            GenerationEpoch = Timestamp(data.GenerationEpoch),
            ProjectFingerprint = ForgeIo.Sha256Text(projectJson.Replace("\r\n", "\n").TrimEnd() + "\n")
        };

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.csv", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            manifest.SourceFileSha256[Path.GetFileName(file)] = ForgeIo.Sha256File(file);
        manifest.DatasetFingerprint = ForgeIo.DatasetFingerprint(manifest.SourceFileSha256);

        AddCounts(manifest.SourceRowCounts, data);
        manifest.ExpectedSilverRowCounts["customer_cdc"] = 3;
        manifest.ExpectedSilverRowCounts["customer_scd2"] = data.Customers.Count + 2;
        manifest.ExpectedSilverRowCounts["customers"] = data.Customers.Count;
        manifest.ExpectedSilverRowCounts["order_rows"] = data.OrderLines.Count;
        manifest.ExpectedSilverRowCounts["orders"] = data.Orders.Count;
        manifest.ExpectedSilverRowCounts["products"] = data.Products.Count;
        manifest.ExpectedSilverRowCounts["quality_issues"] = 2;
        manifest.ExpectedSilverRowCounts["returns"] = data.Returns.Count;
        manifest.ExpectedSilverRowCounts["reviews"] = data.Reviews.Count - 1;
        manifest.ExpectedSilverRowCounts["shipment_events"] = data.ShipmentEvents.Count - 1;
        manifest.ExpectedSilverRowCounts["shipments"] = data.Shipments.Count - 1;
        manifest.ExpectedSilverRowCounts["stores"] = data.Stores.Count;
        manifest.ExpectedSilverRowCounts["support_tickets"] = data.SupportTickets.Count;

        var grossSales = data.OrderLines.Sum(row => row.NetPrice * row.Quantity);
        var validShipments = data.Shipments.Where(row => !string.IsNullOrWhiteSpace(row.TrackingNumber)).ToList();
        var validReviews = data.Reviews.Where(row => row.Rating is >= 1 and <= 5).ToList();
        manifest.ExpectedKpis["average_review_rating"] = decimal.Round(validReviews.Average(row => (decimal)row.Rating), 6);
        manifest.ExpectedKpis["gross_sales_amount"] = decimal.Round(grossSales, 2);
        manifest.ExpectedKpis["on_time_delivery_rate"] = decimal.Round(
            (decimal)validShipments.Count(row => row.DeliveredAt <= row.PromisedAt) / validShipments.Count, 6);
        manifest.ExpectedKpis["order_count"] = data.Orders.Count;
        manifest.ExpectedKpis["return_rate"] = decimal.Round((decimal)data.Returns.Count / data.Orders.Count, 6);

        manifest.Evidence.Add(Evidence("EV-CDC-I", "cdc", "Customer", new[] { "customer-0001-I" }, "apply insert",
            ("operation", "I"), ("customerKey", (spec.Generation.Customers + 1).ToString(Invariant))));
        manifest.Evidence.Add(Evidence("EV-CDC-U", "cdc", "Customer", new[] { "customer-0001-U" }, "apply update and create a new SCD2 version",
            ("operation", "U"), ("customerKey", "1")));
        manifest.Evidence.Add(Evidence("EV-CDC-D", "cdc", "Customer", new[] { "customer-0001-D" }, "close the inserted customer's active version",
            ("operation", "D"), ("customerKey", (spec.Generation.Customers + 1).ToString(Invariant))));
        manifest.Evidence.Add(Evidence("EV-DUP-CDC", "duplicate", "CustomerCdc", new[] { data.DuplicateCdcEventId }, "deduplicate by EventId and retain one",
            ("rawCopies", "2"), ("expectedCopies", "1")));
        manifest.Evidence.Add(Evidence("EV-DUP-SHIPMENT-EVENT", "duplicate", "ShipmentEvent", new[] { data.DuplicateShipmentEventKey.ToString(Invariant) }, "deduplicate by ShipmentEventKey and retain one",
            ("rawCopies", "2"), ("expectedCopies", "1")));
        manifest.Evidence.Add(Evidence("EV-LATE-ARRIVAL", "late_arrival", "ShipmentEvent", new[] { data.LateShipmentEventKey.ToString(Invariant) }, "flag when ingestion lag exceeds 24 hours",
            ("thresholdHours", "24"), ("ingestionLagHours", "72")));
        manifest.Evidence.Add(Evidence("EV-SCD2", "scd2", "Customer", new[] { "1", "customer-0001-U" }, "close the prior version and open a Platinum/Basel version",
            ("changedAttributes", "City,LoyaltyTier")));
        manifest.Evidence.Add(Evidence("EV-QUALITY-NULL", "quality", "Shipment", new[] { data.NullTrackingShipmentKey.ToString(Invariant) }, "quarantine null TrackingNumber",
            ("field", "TrackingNumber"), ("rule", "not_null")));
        manifest.Evidence.Add(Evidence("EV-QUALITY-RANGE", "quality", "Review", new[] { data.InvalidReviewKey.ToString(Invariant) }, "quarantine rating outside 1..5",
            ("field", "Rating"), ("badValue", "7")));
        return manifest;
    }

    private static TruthEvidence Evidence(
        string evidenceId,
        string injector,
        string entity,
        IEnumerable<string> keys,
        string expectedEffect,
        params (string Key, string Value)[] details)
    {
        var evidence = new TruthEvidence
        {
            EvidenceId = evidenceId,
            Injector = injector,
            Entity = entity,
            RecordKeys = keys.ToList(),
            ExpectedEffect = expectedEffect
        };
        foreach (var detail in details)
            evidence.Details[detail.Key] = detail.Value;
        return evidence;
    }

    private static void AddCounts(SortedDictionary<string, long> counts, CustomerSatisfactionData data)
    {
        counts["customer_cdc"] = data.CustomerCdc.Count;
        counts["customers"] = data.Customers.Count;
        counts["order_rows"] = data.OrderLines.Count;
        counts["orders"] = data.Orders.Count;
        counts["products"] = data.Products.Count;
        counts["returns"] = data.Returns.Count;
        counts["reviews"] = data.Reviews.Count;
        counts["shipment_events"] = data.ShipmentEvents.Count;
        counts["shipments"] = data.Shipments.Count;
        counts["stores"] = data.Stores.Count;
        counts["support_tickets"] = data.SupportTickets.Count;
    }

    private static string Money(decimal value) => value.ToString("0.00", Invariant);
    private static string Timestamp(DateTimeOffset? value) => value.HasValue ? Timestamp(value.Value) : string.Empty;
    private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Invariant);
    private static IReadOnlyList<string?> Cells(params object?[] values) =>
        values.Select(value => value switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, Invariant),
            _ => value.ToString()
        }).ToArray();

    private sealed class CustomerSatisfactionData
    {
        public DateTimeOffset GenerationEpoch { get; init; }
        public List<CustomerRow> Customers { get; } = new();
        public List<ProductRow> Products { get; } = new();
        public List<StoreRow> Stores { get; } = new();
        public List<OrderRow> Orders { get; } = new();
        public List<OrderLineRow> OrderLines { get; } = new();
        public List<ShipmentRow> Shipments { get; } = new();
        public List<ShipmentEventRow> ShipmentEvents { get; } = new();
        public List<ReturnRow> Returns { get; } = new();
        public List<SupportTicketRow> SupportTickets { get; } = new();
        public List<ReviewRow> Reviews { get; } = new();
        public List<CustomerCdcRow> CustomerCdc { get; } = new();
        public long DuplicateShipmentEventKey { get; set; }
        public string DuplicateCdcEventId { get; set; } = string.Empty;
        public long LateShipmentEventKey { get; set; }
        public long NullTrackingShipmentKey { get; set; }
        public long InvalidReviewKey { get; set; }
    }
}
