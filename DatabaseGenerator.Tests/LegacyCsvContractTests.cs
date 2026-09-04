using System.Globalization;
using DatabaseGenerator.DataWriter;
using DatabaseGenerator.Models;

namespace DatabaseGenerator.Tests;

public class LegacyCsvContractTests
{
    private static readonly string[] OrderHeaders =
    [
        "OrderKey", "CustomerKey", "StoreKey", "OrderDate", "DeliveryDate", "CurrencyCode"
    ];

    private static readonly string[] SalesHeaders =
    [
        "OrderKey", "LineNumber", "OrderDate", "DeliveryDate", "CustomerKey", "StoreKey",
        "ProductKey", "Quantity", "UnitPrice", "NetPrice", "UnitCost", "CurrencyCode",
        "ExchangeRate"
    ];

    [Fact]
    public void HeaderOrder_RemainsCompatibleWithLegacyCsvConsumers()
    {
        var writer = CreateWriter(Path.GetTempPath());

        Assert.Equal(OrderHeaders, writer.DumpOrders_Headers());
        Assert.Equal(SalesHeaders, writer.DumpSales_Headers());
    }

    [Fact]
    public void OrderAndSaleFormatting_RemainsIsoDatedAndInvariant()
    {
        var writer = CreateWriter(Path.GetTempPath());
        var order = CreateOrder();
        var sale = CreateSale();

        Assert.Equal(
            ["9000000001", "42", "7", "2024-02-03", "2024-02-08", "EUR"],
            writer.DumpOrders_DataField(order));

        Assert.Equal(
            [
                "9000000001", "3", "2024-02-03", "2024-02-08", "42", "7", "101",
                "4", "1234.56", "987.65", "456.78", "EUR", "0.92345"
            ],
            writer.DumpSale_DataField(sale, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task BothMode_WritesLegacyFilesHeadersAndRows()
    {
        var outputFolder = Path.Combine(Path.GetTempPath(), $"contoso-legacy-csv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputFolder);

        try
        {
            var writer = CreateWriter(outputFolder);
            writer.Init();

            await writer.WriteOrderWithRows(CreateOrder(), [CreateSale()]);
            writer.Close();

            Assert.Equal(
                ["orderrows.csv", "orders.csv", "sales.csv"],
                Directory.GetFiles(outputFolder).Select(path => Path.GetFileName(path)!).Order().ToArray());

            Assert.Equal(
                [
                    string.Join(',', OrderHeaders),
                    "9000000001,42,7,2024-02-03,2024-02-08,EUR"
                ],
                File.ReadAllLines(Path.Combine(outputFolder, "orders.csv")));

            Assert.Equal(
                [
                    "OrderKey,LineNumber,ProductKey,Quantity,UnitPrice,NetPrice,UnitCost",
                    "9000000001,3,101,4,1234.56,987.65,456.78"
                ],
                File.ReadAllLines(Path.Combine(outputFolder, "orderrows.csv")));

            Assert.Equal(
                [
                    string.Join(',', SalesHeaders),
                    "9000000001,3,2024-02-03,2024-02-08,42,7,101,4,1234.56,987.65,456.78,EUR,0.92345"
                ],
                File.ReadAllLines(Path.Combine(outputFolder, "sales.csv")));
        }
        finally
        {
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    private static CSVWriter CreateWriter(string outputFolder) =>
        new(
            new Config
            {
                SalesOrdersOut = new SOOutput("BOTH"),
                CsvMaxOrdersPerFile = null,
                CsvGzCompression = 0
            },
            outputFolder);

    private static Order CreateOrder() =>
        new()
        {
            OrderID = 9_000_000_001,
            CustomerID = 42,
            StoreID = 7,
            DT = new DateTime(2024, 2, 3, 22, 15, 0, DateTimeKind.Utc),
            DeliveryDate = new DateTime(2024, 2, 8, 7, 30, 0, DateTimeKind.Utc),
            CurrencyCode = "EUR",
            Rows =
            [
                new OrderRow
                {
                    RowNumber = 3,
                    ProductID = 101,
                    Quantity = 4,
                    UnitPrice = 1234.56m,
                    NetPrice = 987.65m,
                    UnitCost = 456.78m
                }
            ]
        };

    private static Sale CreateSale() =>
        new()
        {
            OrderKey = 9_000_000_001,
            LineNumber = 3,
            OrderDate = new DateTime(2024, 2, 3, 22, 15, 0, DateTimeKind.Utc),
            DeliveryDate = new DateTime(2024, 2, 8, 7, 30, 0, DateTimeKind.Utc),
            CustomerKey = 42,
            StoreKey = 7,
            ProductKey = 101,
            Quantity = 4,
            UnitPrice = 1234.56m,
            NetPrice = 987.65m,
            UnitCost = 456.78m,
            CurrencyCode = "EUR",
            ExchangeRate = 0.92345m
        };
}
