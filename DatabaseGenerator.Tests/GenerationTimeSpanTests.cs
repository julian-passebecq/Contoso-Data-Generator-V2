using DatabaseGenerator.Forge.Generation;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public sealed class GenerationTimeSpanTests
{
    [Fact]
    public async Task OmittedSpanPreservesTheExistingSixtyDaySourceAndManifestBytes()
    {
        var root = NewRoot();
        try
        {
            var spec = ForgeTestProject.CreateSmallSpec();
            spec.Generation.Orders = 120;
            var result = await new ForgeProjectGenerator().GenerateAsync(spec, root);
            // Captured from the previous generator before making its 60-day horizon optional.
            Assert.Equal("663709952ac8cb629f5ca9b79e753708baaf1d5c76b33343051806a6dfe1f30e", result.DatasetFingerprint);
            Assert.Equal("0f9a68d279d4cf007e450a46c966297986cfb3051a4ece4f84eba4763948cb8f", Hash(root, "project.json"));
            Assert.Equal("6dfb68e70108a6ff46ae6e9d5e1a1fa669f81de10a9c3b68006516f48f378830", Hash(root, "truth_manifest.json"));
            Assert.DoesNotContain("timeSpanDays", File.ReadAllText(Path.Combine(root, "project.json")));
            AssertDateCycle(root, spec.Generation.StartDate, 120, 60);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OptionalSpanExtendsDatesWithoutChangingOtherOrderFieldsOrRandomChoices()
    {
        var root = NewRoot();
        try
        {
            var spec = ForgeTestProject.CreateSmallSpec();
            spec.Generation.Orders = 400;
            var generator = new ForgeProjectGenerator();
            var defaultRoot = Path.Combine(root, "default");
            var extendedRoot = Path.Combine(root, "extended");
            await generator.GenerateAsync(spec, defaultRoot);
            spec.Generation.TimeSpanDays = 365;
            await generator.GenerateAsync(spec, extendedRoot);
            AssertDateCycle(extendedRoot, spec.Generation.StartDate, 400, 365);
            using var project = JsonDocument.Parse(File.ReadAllText(Path.Combine(extendedRoot, "project.json")));
            Assert.Equal(365, project.RootElement.GetProperty("generation").GetProperty("timeSpanDays").GetInt32());
            var before = CsvTable.Read(Path.Combine(defaultRoot, "data/source/orders.csv")).Rows;
            var after = CsvTable.Read(Path.Combine(extendedRoot, "data/source/orders.csv")).Rows;
            for (var i = 0; i < before.Count; i++)
            {
                foreach (var key in before[i].Keys.Where(key => key != "OrderDate"))
                    Assert.Equal(before[i][key], after[i][key]);
                Assert.Equal(DateTimeOffset.Parse(before[i]["OrderDate"], CultureInfo.InvariantCulture).TimeOfDay,
                    DateTimeOffset.Parse(after[i]["OrderDate"], CultureInfo.InvariantCulture).TimeOfDay);
            }
            Assert.Equal(Hash(defaultRoot, "data/source/order_rows.csv"), Hash(extendedRoot, "data/source/order_rows.csv"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3651)]
    public void InvalidSpanIsRejected(int days)
    {
        var spec = ForgeTestProject.CreateSmallSpec();
        spec.Generation.TimeSpanDays = days;
        Assert.Contains("generation.timeSpanDays", Assert.Throws<ArgumentException>(spec.Validate).Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(3650)]
    public void BoundaryAndLegacySpansAreAccepted(int days)
    {
        var spec = ForgeTestProject.CreateSmallSpec();
        spec.Generation.TimeSpanDays = days;
        spec.Validate();
    }

    private static void AssertDateCycle(string root, string start, int count, int span)
    {
        var epoch = DateTimeOffset.Parse(start, CultureInfo.InvariantCulture);
        var rows = CsvTable.Read(Path.Combine(root, "data/source/orders.csv")).Rows;
        Assert.Equal(count, rows.Count);
        for (var i = 0; i < rows.Count; i++)
            Assert.Equal(epoch.AddDays(i % span).Date, DateTimeOffset.Parse(rows[i]["OrderDate"], CultureInfo.InvariantCulture).Date);
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-time-span-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string Hash(string root, string relative) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, relative)))).ToLowerInvariant();
}
