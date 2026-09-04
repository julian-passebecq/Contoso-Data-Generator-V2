namespace DatabaseGenerator.Tests;

public class OutputContractTests
{
    public static TheoryData<string, bool, bool> SupportedOutputModes =>
        new()
        {
            { "ORDERS", true, false },
            { "orders", true, false },
            { "SALES", false, true },
            { "sales", false, true },
            { "BOTH", true, true },
            { "both", true, true }
        };

    [Theory]
    [MemberData(nameof(SupportedOutputModes))]
    public void SOOutput_MapsSupportedModes(string mode, bool writeOrders, bool writeSales)
    {
        var output = new SOOutput(mode);

        Assert.Equal(writeOrders, output.WriteOrders);
        Assert.Equal(writeSales, output.WriteSales);
    }

    [Fact]
    public void SOOutput_RejectsUnknownModesWithLegacyDiagnostic()
    {
        var exception = Assert.Throws<Exception>(() => new SOOutput("archive"));

        Assert.Equal("Unknown option for [SalesOrders] - 'ARCHIVE'", exception.Message);
    }

    [Fact]
    public void OutputConstants_RemainCompatibleWithExistingFoldersAndFiles()
    {
        Assert.Equal("orders", Consts.ORDERS);
        Assert.Equal("orderrows", Consts.ORDERROWS);
        Assert.Equal("sales", Consts.SALES);
        Assert.Equal("currencyexchange", Consts.CURREXCHS);
        Assert.Equal("customer", Consts.CUSTOMERS);
        Assert.Equal("date", Consts.DATES);
        Assert.Equal("product", Consts.PRODUCTS);
        Assert.Equal("store", Consts.STORES);
        Assert.Equal("ECB_eurofxref-hist.csv", Consts.FILE_ECB_EXCH_CSV);
        Assert.Equal(
            "https://github.com/sql-bi/Contoso-Data-Generator-V2-Data/releases/download/static-files/",
            Consts.DOWNLOAD_BASE_URL);
    }
}
