using System.Text.Json;
using DatabaseGenerator.DataWriter;
using DatabaseGenerator.Models;

namespace DatabaseGenerator.Tests;

public class LegacyParquetContractTests
{
    private static readonly string[] TableNames =
    [
        "currencyexchange", "customer", "date", "orderrows", "orders", "product", "sales", "store"
    ];

    static LegacyParquetContractTests()
    {
        Logger.Init(Path.Combine(Path.GetTempPath(), $"contoso-forge-parquet-regression-{Guid.NewGuid():N}.log"));
    }

    [Fact]
    public async Task ParquetMode_WritesEveryLegacyTableAsAReadableParquetContainer()
    {
        var output = CreateOutputDirectory("parquet");
        try
        {
            var writer = CreateWriter("PARQUET", output);
            await WriteRepresentativeData(writer);

            Assert.Equal(
                TableNames.Select(name => $"{name}.parquet").Order(StringComparer.Ordinal),
                Directory.GetFiles(output, "*.parquet").Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.All(Directory.GetFiles(output, "*.parquet"), AssertParquetMagic);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public async Task DeltaTableMode_WritesEveryLegacyTableWithProtocolMetadataAndDataAction()
    {
        var output = CreateOutputDirectory("delta");
        try
        {
            var writer = CreateWriter("DELTATABLE", output);
            await WriteRepresentativeData(writer);

            Assert.Equal(
                TableNames,
                Directory.GetDirectories(output).Select(Path.GetFileName).Order(StringComparer.Ordinal));

            foreach (var tableName in TableNames)
            {
                var tableRoot = Path.Combine(output, tableName);
                var parquetFile = Assert.Single(Directory.GetFiles(tableRoot, "*.parquet"));
                AssertParquetMagic(parquetFile);

                var logPath = Path.Combine(tableRoot, "_delta_log", "00000000000000000000.json");
                Assert.True(File.Exists(logPath), $"Delta log missing for {tableName}.");
                var actions = File.ReadAllLines(logPath);
                Assert.Equal(3, actions.Length);
                using var protocol = JsonDocument.Parse(actions[0]);
                using var metadata = JsonDocument.Parse(actions[1]);
                using var add = JsonDocument.Parse(actions[2]);
                Assert.True(protocol.RootElement.TryGetProperty("protocol", out _));
                Assert.True(metadata.RootElement.TryGetProperty("metaData", out _));
                Assert.Equal(Path.GetFileName(parquetFile), add.RootElement.GetProperty("add").GetProperty("path").GetString());
            }
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static ParquetWriter CreateWriter(string format, string output)
    {
        var writer = new ParquetWriter(
            new Config
            {
                OutputFormat = format,
                SalesOrdersOut = new SOOutput("BOTH"),
                DeltaTableOrdersPerFile = 10,
                ParquetOrdersRowGroupSize = 10
            },
            output);
        writer.Init();
        return writer;
    }

    private static async Task WriteRepresentativeData(ParquetWriter writer)
    {
        var order = new Order
        {
            OrderID = 9_000_000_001,
            CustomerID = 42,
            StoreID = 7,
            DT = new DateTime(2024, 2, 3, 0, 0, 0, DateTimeKind.Utc),
            DeliveryDate = new DateTime(2024, 2, 8, 0, 0, 0, DateTimeKind.Utc),
            CurrencyCode = "EUR",
            Rows =
            [
                new OrderRow
                {
                    RowNumber = 1,
                    ProductID = 101,
                    Quantity = 2,
                    UnitPrice = 125m,
                    NetPrice = 100m,
                    UnitCost = 60m
                }
            ]
        };
        var sale = new Sale
        {
            OrderKey = order.OrderID,
            LineNumber = 1,
            OrderDate = order.DT,
            DeliveryDate = order.DeliveryDate,
            CustomerKey = order.CustomerID,
            StoreKey = order.StoreID,
            ProductKey = 101,
            Quantity = 2,
            UnitPrice = 125m,
            NetPrice = 100m,
            UnitCost = 60m,
            CurrencyCode = "EUR",
            ExchangeRate = 0.92m
        };

        await writer.WriteOrderWithRows(order, [sale]);
        await writer.WriteStaticData(
            [
                new Customer
                {
                    CustomerID = 42,
                    GeoAreaID = 1,
                    StartDT = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    EndDT = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Continent = "Europe",
                    Gender = "F",
                    Title = "Ms",
                    GivenName = "Ada",
                    MiddleInitial = "L",
                    Surname = "Contoso",
                    StreetAddress = "1 Main Street",
                    City = "Zurich",
                    State = "ZH",
                    StateFull = "Zurich",
                    ZipCode = "8001",
                    Country = "CH",
                    CountryFull = "Switzerland",
                    Birthday = new DateTime(1985, 4, 12, 0, 0, 0, DateTimeKind.Utc),
                    Age = 38,
                    Occupation = "Engineer",
                    Company = "Contoso",
                    Vehicle = "Bicycle",
                    Latitude = 47.3769,
                    Longitude = 8.5417
                }
            ],
            [
                new Store
                {
                    StoreID = 7,
                    StoreCode = 700,
                    GeoAreaID = 1,
                    CountryCode = "CH",
                    Country = "Switzerland",
                    State = "ZH",
                    Description = "Zurich Store",
                    Status = "Open",
                    OpenDate = new DateTime(2015, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    SquareMeters = 250
                }
            ],
            [
                new Product
                {
                    ProductID = 101,
                    ProductCode = "P-101",
                    ProductName = "Contoso Speaker",
                    Manufacturer = "Contoso",
                    Brand = "Contoso",
                    Color = "Black",
                    WeightUnit = "kg",
                    Weight = 1.25m,
                    Price = 125,
                    Cost = 60,
                    CategoryID = 1,
                    CategoryName = "Audio",
                    SubCategoryID = 10,
                    SubCategoryName = "Speakers"
                }
            ],
            [
                new DateExtended
                {
                    Date = order.DT,
                    DateKey = "20240203",
                    Year = 2024,
                    YearQuarter = "2024 Q1",
                    YearQuarterNumber = 20241,
                    Quarter = "Q1",
                    YearMonth = "2024 February",
                    YearMonthShort = "2024 Feb",
                    YearMonthNumber = 202402,
                    Month = "February",
                    MonthShort = "Feb",
                    MonthNumber = 2,
                    DayofWeek = "Saturday",
                    DayofWeekShort = "Sat",
                    DayofWeekNumber = 6,
                    WorkingDay = 0,
                    WorkingDayNumber = 0
                }
            ],
            [
                new CurrencyExchange
                {
                    Date = order.DT,
                    FromCurrency = "USD",
                    ToCurrency = "EUR",
                    Exchange = 0.92m
                }
            ]);
        writer.Close();
    }

    private static string CreateOutputDirectory(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"contoso-forge-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertParquetMagic(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 8, $"Parquet file is unexpectedly short: {path}");
        Assert.Equal("PAR1"u8.ToArray(), bytes[..4]);
        Assert.Equal("PAR1"u8.ToArray(), bytes[^4..]);
    }
}
