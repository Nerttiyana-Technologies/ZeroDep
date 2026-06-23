using Xunit;
using ZeroDep.Filters;

namespace ZeroDep.Filters.Tests;

public sealed class PredictorTests
{
    [Fact]
    public void PngUpAndNoneFiltersDecode()
    {
        // 2 rows, stride 3, 1 byte/pixel.
        // Row 0: filter 0 (None) -> [10,20,30]
        // Row 1: filter 2 (Up)   -> previous + current = [11,22,33]
        byte[] input = { 0, 10, 20, 30, 2, 1, 2, 3 };
        byte[] expected = { 10, 20, 30, 11, 22, 33 };

        byte[] actual = Predictor.Apply(input, predictor: 12, colors: 1, bitsPerComponent: 8, columns: 3);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TiffPredictor2DecodesHorizontalDifference()
    {
        // Single row, 1 color, 8-bit, 4 columns. Stored as horizontal differences.
        byte[] input = { 10, 5, 5, 5 };       // cumulative -> 10,15,20,25
        byte[] expected = { 10, 15, 20, 25 };

        byte[] actual = Predictor.Apply(input, predictor: 2, colors: 1, bitsPerComponent: 8, columns: 4);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PredictorOneReturnsInputUnchanged()
    {
        byte[] input = { 1, 2, 3, 4 };
        Assert.Same(input, Predictor.Apply(input, predictor: 1, colors: 1, bitsPerComponent: 8, columns: 4));
    }
}
