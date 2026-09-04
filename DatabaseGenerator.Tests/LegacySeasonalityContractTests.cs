using System.Reflection;

namespace DatabaseGenerator.Tests;

public class LegacySeasonalityContractTests
{
    [Fact]
    public void SeasonalCurve_PreservesTheLegacyCubicPeak()
    {
        var config = CreateConfig();
        config.DaysWeight.DaysWeightPoints = [0, 2, 4];
        config.DaysWeight.DaysWeightValues = [1, 3, 1];

        var actual = CalculateDaysWeight(config, daysCount: 5);

        var expected = new[] { 1d, 2.375d, 3d, 2.375d, 1d };
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], actual[index], precision: 10);
    }

    [Fact]
    public void AnnualAndOneTimeSpikes_PreserveShapeAndWeekdayWeighting()
    {
        var config = CreateConfig();
        config.DaysWeight.DaysWeightConstant = true;
        config.DaysWeight.DaysWeightAddSpikes = true;
        config.DaysWeight.WeekDaysFactor = [0.5, 1, 1, 1, 1, 1, 0.75];
        config.AnnualSpikes = [new AnnualSpike { StartDay = 1, EndDay = 4, Factor = 3 }];
        config.OneTimeSpikes =
        [
            new OneTimeSpike
            {
                DT1 = new DateTime(2024, 1, 5),
                DT2 = new DateTime(2024, 1, 5),
                Factor = 4
            }
        ];

        var actual = CalculateDaysWeight(config, daysCount: 7);

        Assert.Equal(1d, actual[0], precision: 10);
        Assert.Equal(1d, actual[1], precision: 10);
        Assert.Equal(1 + (2 * Math.Sin(Math.PI / 3)), actual[2], precision: 10);
        Assert.Equal(1 + (2 * Math.Sin(2 * Math.PI / 3)), actual[3], precision: 10);
        Assert.Equal(4d, actual[4], precision: 10);
        Assert.Equal(0.75d, actual[5], precision: 10);
        Assert.Equal(0.5d, actual[6], precision: 10);
    }

    private static Config CreateConfig() =>
        new()
        {
            OrdersCount = 1_000,
            StartDT = new DateTime(2024, 1, 1),
            YearsCount = 1,
            DaysWeight = new DaysWeight
            {
                DaysWeightPoints = [0, 6],
                DaysWeightValues = [1, 1],
                WeekDaysFactor = [1, 1, 1, 1, 1, 1, 1]
            }
        };

    private static double[] CalculateDaysWeight(Config config, int daysCount)
    {
        var root = Path.Combine(Path.GetTempPath(), $"contoso-forge-seasonality-{Guid.NewGuid():N}");
        var output = Path.Combine(root, "out");
        var cache = Path.Combine(root, "cache");
        var input = Path.Combine(root, "input.xlsx");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(input, []);

        try
        {
            var engine = new Engine(input, output, cache, config);
            var calculate = typeof(Engine).GetMethod(
                "CalculateDaysWeight",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(Engine).FullName, "CalculateDaysWeight");
            calculate.Invoke(engine, [config.YearsCount, daysCount, config.StartDT]);

            var field = typeof(Engine).GetField("_daysWeight", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(Engine).FullName, "_daysWeight");
            return Assert.IsType<double[]>(field.GetValue(engine));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
