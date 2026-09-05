using DatabaseGenerator.Forge.Generation;
using DatabaseGenerator.Forge.Specs;
using System.Globalization;
using System.Text.Json;

namespace DatabaseGenerator.Tests;

public sealed class MlGenerationTests
{
    [Fact]
    public void ControlsRequireAnExplicitKnownProfileAndFiniteUnitIntervals()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MlGenerationSpec>("{\"positiveOutcomeRate\":0.2}", options));
        var spec = ForgeTestProject.CreateSmallSpec();
        spec.Generation.Ml = new() { Profile = "unknown" };
        Assert.Throws<ArgumentException>(spec.Validate);
        foreach (var bad in new[] { -0.01, 1.01, double.NaN, double.PositiveInfinity })
        {
            spec.Generation.Ml = new() { PositiveOutcomeRate = bad };
            Assert.Throws<ArgumentException>(spec.Validate);
            spec.Generation.Ml = new() { SignalStrength = bad };
            Assert.Throws<ArgumentException>(spec.Validate);
            spec.Generation.Ml = new() { NoiseLevel = bad };
            Assert.Throws<ArgumentException>(spec.Validate);
        }
    }

    [Fact]
    public async Task ControlsChangeCausalOutcomesDeterministicallyWithoutChangingPredictionInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "forge-ml-controls-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profiles = new Dictionary<string, MlGenerationSpec?>
            {
                ["legacy"] = null, ["signal"] = new() { PositiveOutcomeRate = .4, SignalStrength = 1, NoiseLevel = 0 },
                ["repeat"] = new() { PositiveOutcomeRate = .4, SignalStrength = 1, NoiseLevel = 0 },
                ["no-signal"] = new() { PositiveOutcomeRate = .4, SignalStrength = 0, NoiseLevel = 0 },
                ["noise"] = new() { PositiveOutcomeRate = .4, SignalStrength = 1, NoiseLevel = 1 },
                ["zero"] = new() { PositiveOutcomeRate = 0 }, ["one"] = new() { PositiveOutcomeRate = 1 }
            };
            foreach (var (name, profile) in profiles)
            {
                var spec = ForgeTestProject.CreateSmallSpec();
                spec.Generation.Orders = 1200;
                spec.Generation.TimeSpanDays = 365;
                spec.Generation.Ml = profile;
                await new ForgeProjectGenerator().GenerateAsync(spec, Path.Combine(root, name));
            }
            string Source(string name, string file) => Path.Combine(root, name, "data/source", file);
            foreach (var path in Directory.GetFiles(Path.Combine(root, "legacy/data/source")))
            {
                var file = Path.GetFileName(path);
                if (file is "reviews.csv" or "support_tickets.csv") continue;
                foreach (var name in profiles.Keys)
                    Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(Source(name, file)));
            }
            foreach (var file in Directory.GetFiles(Path.Combine(root, "signal"), "*", SearchOption.AllDirectories))
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(root, "repeat", Path.GetRelativePath(Path.Combine(root, "signal"), file))));
            Dictionary<long, int> Ratings(string name) => File.ReadLines(Source(name, "reviews.csv")).Skip(1)
                .Select(line => line.Split(',')).ToDictionary(row => long.Parse(row[1]), row => int.Parse(row[5]));
            Assert.All(Ratings("zero").Values, r => Assert.True(r is 4 or 7));
            Assert.All(Ratings("one").Values, r => Assert.True(r is 2 or 7));
            Assert.Equal(File.ReadAllBytes(Source("no-signal", "reviews.csv")), File.ReadAllBytes(Source("noise", "reviews.csv")));
            Assert.NotEqual(File.ReadAllText(Source("signal", "reviews.csv")), File.ReadAllText(Source("noise", "reviews.csv")));
            var delays = File.ReadLines(Source("signal", "shipments.csv")).Skip(1).Select(line => line.Split(','))
                .ToDictionary(r => long.Parse(r[1]), r => (DateTimeOffset.Parse(r[6], CultureInfo.InvariantCulture) - DateTimeOffset.Parse(r[5], CultureInfo.InvariantCulture)).TotalDays);
            var ratings = Ratings("signal");
            double Rate(bool late) => ratings.Where(r => r.Value != 7 && (delays[r.Key] > 0) == late).Average(r => r.Value == 2 ? 1.0 : 0.0);
            Assert.True(Rate(true) > Rate(false) + .3, "Delivery delay should cause a measurable adverse-outcome signal.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
