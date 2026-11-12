using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>
/// The values given back by the dequantization are bit for bit those of the first version of the loop, which looked at the null, the zero
/// and the end of the dither sequence for each value.
/// </summary>
public class DequantizeTests
{
    /// <summary>The first version: one value at a time.</summary>
    private static double[] Reference(int[] ints, double scale, double zero, int? blank, QuantizationMethod method, int tileIndex, int ditherSeed)
    {
        bool dither = method is QuantizationMethod.SubtractiveDither1 or QuantizationMethod.SubtractiveDither2;
        int seedIndex = (int)(((long)tileIndex + ditherSeed - 1) % Quantizer.RandomCount);
        int next = (int)(Quantizer.RandomValue(seedIndex) * 500);
        var result = new double[ints.Length];
        for (int i = 0; i < ints.Length; i++)
        {
            int v = ints[i];
            double r = 0;
            if (dither)
            {
                r = Quantizer.RandomValue(next);
                if (++next == Quantizer.RandomCount)
                {
                    if (++seedIndex == Quantizer.RandomCount) seedIndex = 0;
                    next = (int)(Quantizer.RandomValue(seedIndex) * 500);
                }
            }
            if (blank is { } b && v == b) result[i] = double.NaN;
            else if (method == QuantizationMethod.SubtractiveDither2 && v == Quantizer.ZeroValue) result[i] = 0.0;
            else if (dither) result[i] = (v - r + 0.5) * scale + zero;
            else result[i] = v * scale + zero;
        }
        return result;
    }

    public static TheoryData<QuantizationMethod, bool> Cases() =>
        new()
        {
            { QuantizationMethod.None, false }, { QuantizationMethod.None, true },
            { QuantizationMethod.SubtractiveDither1, false }, { QuantizationMethod.SubtractiveDither1, true },
            { QuantizationMethod.SubtractiveDither2, false }, { QuantizationMethod.SubtractiveDither2, true },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Values_are_those_of_the_first_version(QuantizationMethod method, bool withBlank)
    {
        var rng = new Random(11);
        // lengths below, on and above the 10000 values of the sequence (it starts again from another position), and up to several times it
        foreach (int length in new[] { 0, 1, 7, 100, 9999, 10000, 10001, 25_000, 41_000 })
        {
            foreach ((int tile, int seed) in new[] { (1, 1), (9999, 1), (3, 5000), (777, 10000) })
            {
                int[] ints = Enumerable.Range(0, length).Select(i => rng.Next(12) switch
                {
                    0 when withBlank => Quantizer.NullValue,
                    1 when method == QuantizationMethod.SubtractiveDither2 => Quantizer.ZeroValue,
                    _ => rng.Next(-5_000_000, 5_000_000),
                }).ToArray();
                int? blank = withBlank ? Quantizer.NullValue : null;
                double scale = 0.0137 + rng.NextDouble(), zero = rng.NextDouble() * 1000 - 500;

                double[] expected = Reference(ints, scale, zero, blank, method, tile, seed);
                double[] actual = Quantizer.Dequantize(ints, scale, zero, blank, method, tile, seed);
                Assert.Equal(expected.Select(BitConverter.DoubleToInt64Bits), actual.Select(BitConverter.DoubleToInt64Bits));

                float[] single = Quantizer.DequantizeToSingle(ints, scale, zero, blank, method, tile, seed);
                Assert.Equal(expected.Select(v => BitConverter.SingleToInt32Bits((float)v)), single.Select(BitConverter.SingleToInt32Bits));
            }
        }
    }
}
