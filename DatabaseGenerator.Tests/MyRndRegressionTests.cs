using DatabaseGenerator;

namespace DatabaseGenerator.Tests;

public class MyRndRegressionTests
{
    private static readonly double[] Weights = [1d, 3d, 6d, 2d];

    private static readonly int[] ExpectedSeededSequence =
    [
        2, 3, 2, 3, 2, 0, 1, 2,
        2, 3, 1, 2, 3, 2, 1, 2
    ];

    [Fact]
    public void ArrayOverload_PreservesSeededSelectionSequence()
    {
        var random = new Random(8_675_309);

        var actual = Enumerable.Range(0, ExpectedSeededSequence.Length)
            .Select(_ => MyRnd.RandomIndexFromWeigthedDistribution(random, Weights))
            .ToArray();

        Assert.Equal(ExpectedSeededSequence, actual);
    }

    [Fact]
    public void DoublesArrayOverload_PreservesSeededSelectionSequence()
    {
        var random = new Random(8_675_309);
        var weights = new DoublesArray(Weights);

        var actual = Enumerable.Range(0, ExpectedSeededSequence.Length)
            .Select(_ => MyRnd.RandomIndexFromWeigthedDistribution(random, weights))
            .ToArray();

        Assert.Equal(ExpectedSeededSequence, actual);
    }

    [Fact]
    public void DoublesArray_ExposesLegacyAggregateContract()
    {
        var values = new[] { 1.25d, 0.75d, 3d, 2d };

        var weights = new DoublesArray(values);

        Assert.Equal(4, weights.Length);
        Assert.Equal(7d, weights.Sum);
        Assert.Same(values, weights.Values);
        Assert.Equal(values, Enumerable.Range(0, weights.Length).Select(i => weights[i]));
        Assert.Equal(new[] { 1.25d, 2d, 5d, 7d }, weights.ProgressiveSum);
    }
}
