namespace Eleris.FitsNet.Compression;

/// <summary>How floating point pixels are mapped to integers before compression (<c>ZQUANTIZ</c>).</summary>
public enum QuantizationMethod
{
    /// <summary>No quantization: floating point tiles are stored losslessly (gzip or uncompressed).</summary>
    None,

    /// <summary><c>NO_DITHER</c>.</summary>
    NoDither,

    /// <summary><c>SUBTRACTIVE_DITHER_1</c>.</summary>
    SubtractiveDither1,

    /// <summary><c>SUBTRACTIVE_DITHER_2</c>: like dither 1 but zero pixels are preserved exactly.</summary>
    SubtractiveDither2,
}

/// <summary>Quantization of floating point pixels (standard section 10.2), including the reference dithering sequence.</summary>
internal static class Quantizer
{
    public const int RandomCount = 10000;

    /// <summary>The integer that stands for NaN (ZBLANK).</summary>
    public const int NullValue = -2147483647;

    /// <summary>The integer that stands for an exact zero with SUBTRACTIVE_DITHER_2.</summary>
    public const int ZeroValue = -2147483646;

    private const int ReservedValues = 10;

    /// <summary>The Park-Miller random sequence of the specification (seed 1, 10000 values stored as single precision).</summary>
    private static readonly float[] Random = BuildRandom();

    private static float[] BuildRandom()
    {
        const double a = 16807.0, m = 2147483647.0;
        var values = new float[RandomCount];
        double seed = 1;
        for (int i = 0; i < values.Length; i++)
        {
            double temp = a * seed;
            seed = temp - m * (int)(temp / m);
            values[i] = (float)(seed / m);
        }
        return values;
    }

    public static float RandomValue(int index) => Random[index];

    private static bool UsesDither(QuantizationMethod m) => m is QuantizationMethod.SubtractiveDither1 or QuantizationMethod.SubtractiveDither2;

    /// <summary>Tracks the position in the dither sequence the same way for encoding and decoding.</summary>
    private struct DitherState
    {
        private int _seedIndex;
        private int _next;

        public DitherState(int tileIndex, int ditherSeed)
        {
            _seedIndex = (int)(((long)tileIndex + ditherSeed - 1) % RandomCount);
            _next = (int)(Random[_seedIndex] * 500);
        }

        public double Take()
        {
            double r = Random[_next];
            if (++_next == RandomCount)
            {
                if (++_seedIndex == RandomCount) _seedIndex = 0;
                _next = (int)(Random[_seedIndex] * 500);
            }
            return r;
        }

        /// <summary>
        /// The next values of the sequence, as many as there are before it starts again from another position (at most
        /// <paramref name="max"/>): the same values as that many calls to <see cref="Take"/>, without a test for each.
        /// </summary>
        public ReadOnlySpan<float> TakeRun(int max)
        {
            int run = Math.Min(max, RandomCount - _next);
            var values = Random.AsSpan(_next, run);
            _next += run;
            if (_next == RandomCount)
            {
                if (++_seedIndex == RandomCount) _seedIndex = 0;
                _next = (int)(Random[_seedIndex] * 500);
            }
            return values;
        }
    }

    /// <summary>Converts quantized integers back to floating point values.</summary>
    public static double[] Dequantize(int[] ints, double scale, double zero, int? blank, QuantizationMethod method, int tileIndex, int ditherSeed)
    {
        var result = new double[ints.Length];
        Dequantize(ints, result, scale, zero, blank, method, tileIndex, ditherSeed);
        return result;
    }

    /// <summary>Like the array version, writing into <paramref name="result"/> (as long as <paramref name="ints"/>).</summary>
    public static void Dequantize(ReadOnlySpan<int> ints, Span<double> result, double scale, double zero, int? blank, QuantizationMethod method, int tileIndex, int ditherSeed)
    {
        bool dither = UsesDither(method);
        bool special = blank is not null || method == QuantizationMethod.SubtractiveDither2;   // values that are not quantized (NaN, an exact zero)
        if (!dither)
        {
            if (special) DequantizeChecked(ints, result, scale, zero, blank, method, default, 0);
            else for (int i = 0; i < ints.Length; i++) result[i] = ints[i] * scale + zero;
            return;
        }
        var state = new DitherState(tileIndex, ditherSeed);
        for (int start = 0; start < ints.Length;)
        {
            ReadOnlySpan<float> random = state.TakeRun(ints.Length - start);
            int n = random.Length;
            if (special)
            {
                DequantizeChecked(ints.Slice(start, n), result.Slice(start, n), scale, zero, blank, method, random, n);
            }
            else
            {
                ReadOnlySpan<int> source = ints.Slice(start, n);
                Span<double> destination = result.Slice(start, n);
                for (int j = 0; j < source.Length; j++) destination[j] = (source[j] - (double)random[j] + 0.5) * scale + zero;
            }
            start += n;
        }
    }

    /// <summary>The values with a test for the null and the zero of each one (a run of the dither sequence, or none: <paramref name="count"/> 0).</summary>
    private static void DequantizeChecked(ReadOnlySpan<int> ints, Span<double> result, double scale, double zero, int? blank, QuantizationMethod method,
        ReadOnlySpan<float> random, int count)
    {
        bool dither = count > 0;
        for (int i = 0; i < ints.Length; i++)
        {
            int v = ints[i];
            if (blank is { } b && v == b) result[i] = double.NaN;
            else if (method == QuantizationMethod.SubtractiveDither2 && v == ZeroValue) result[i] = 0.0;
            else if (dither) result[i] = (v - (double)random[i] + 0.5) * scale + zero;
            else result[i] = v * scale + zero;
        }
    }

    /// <summary>Like <see cref="Dequantize(int[], double, double, int?, QuantizationMethod, int, int)"/> for single precision output.</summary>
    public static float[] DequantizeToSingle(int[] ints, double scale, double zero, int? blank, QuantizationMethod method, int tileIndex, int ditherSeed)
    {
        var result = new float[ints.Length];
        DequantizeToSingle(ints, result, scale, zero, blank, method, tileIndex, ditherSeed);
        return result;
    }

    /// <summary>Like the array version, writing into <paramref name="result"/> (as long as <paramref name="ints"/>).</summary>
    public static void DequantizeToSingle(ReadOnlySpan<int> ints, Span<float> result, double scale, double zero, int? blank, QuantizationMethod method, int tileIndex, int ditherSeed)
    {
        bool dither = UsesDither(method);
        bool special = blank is not null || method == QuantizationMethod.SubtractiveDither2;   // values that are not quantized (NaN, an exact zero)
        if (!dither)
        {
            if (special) DequantizeToSingleChecked(ints, result, scale, zero, blank, method, default, 0);
            else for (int i = 0; i < ints.Length; i++) result[i] = (float)(ints[i] * scale + zero);
            return;
        }
        var state = new DitherState(tileIndex, ditherSeed);
        for (int start = 0; start < ints.Length;)
        {
            ReadOnlySpan<float> random = state.TakeRun(ints.Length - start);
            int n = random.Length;
            if (special)
            {
                DequantizeToSingleChecked(ints.Slice(start, n), result.Slice(start, n), scale, zero, blank, method, random, n);
            }
            else
            {
                ReadOnlySpan<int> source = ints.Slice(start, n);
                Span<float> destination = result.Slice(start, n);
                for (int j = 0; j < source.Length; j++) destination[j] = (float)((source[j] - (double)random[j] + 0.5) * scale + zero);
            }
            start += n;
        }
    }

    private static void DequantizeToSingleChecked(ReadOnlySpan<int> ints, Span<float> result, double scale, double zero, int? blank, QuantizationMethod method,
        ReadOnlySpan<float> random, int count)
    {
        bool dither = count > 0;
        for (int i = 0; i < ints.Length; i++)
        {
            int v = ints[i];
            if (blank is { } b && v == b) result[i] = float.NaN;
            else if (method == QuantizationMethod.SubtractiveDither2 && v == ZeroValue) result[i] = 0f;
            else if (dither) result[i] = (float)((v - (double)random[i] + 0.5) * scale + zero);
            else result[i] = (float)(v * scale + zero);
        }
    }

    /// <summary>
    /// Quantizes a tile (<paramref name="width"/> pixels per row) into <paramref name="ints"/>, which holds as many values as the tile.
    /// Returns false when the tile cannot be quantized reliably (no measurable noise, or a dynamic range too large for 32 bits);
    /// the caller then stores it losslessly.
    /// </summary>
    public static bool TryQuantize(ReadOnlySpan<double> values, Span<int> ints, int width, double qlevel, QuantizationMethod method, int tileIndex, int ditherSeed,
        out double scale, out double zero, out bool hasNaN)
    {
        if (ints.Length != values.Length) throw new ArgumentException("There must be one integer for each value.", nameof(ints));
        scale = 1;
        zero = 0;
        hasNaN = false;

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (double v in values)
        {
            if (double.IsNaN(v)) { hasNaN = true; continue; }
            if (double.IsInfinity(v)) return false;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        double delta;
        if (min > max) // only NaNs
        {
            delta = 1e-12;
            min = 0;
        }
        else if (max == min)
        {
            // A constant tile: use a step far below single precision so the dither (up to half a step) is invisible.
            delta = Math.Max(Math.Abs(min) * 6e-8, 1e-12);
        }
        else if (qlevel < 0)
        {
            delta = -qlevel; // an absolute step
        }
        else
        {
            double noise = EstimateNoise(values, width);
            // No measurable noise (a smooth ramp, flat integer-valued data): use a step near single precision rather than
            // giving up on quantization.
            delta = noise > 0 ? noise / qlevel : (max - min) / 8388608.0;
        }
        if (!(delta > 0) || double.IsInfinity(delta)) return false;
        if ((max - min) / delta > 2147483647.0 - ReservedValues - 1) return false;

        scale = delta;
        zero = min;
        bool dither = UsesDither(method);
        var state = dither ? new DitherState(tileIndex, ditherSeed) : default;
        for (int i = 0; i < values.Length; i++)
        {
            double v = values[i];
            double r = dither ? state.Take() : 0;
            if (double.IsNaN(v)) ints[i] = NullValue;
            else if (method == QuantizationMethod.SubtractiveDither2 && v == 0.0) ints[i] = ZeroValue;
            else
            {
                double t = (v - zero) / delta + (dither ? r - 0.5 : 0);
                ints[i] = (int)(t >= 0 ? t + 0.5 : t - 0.5); // round half away from zero
            }
        }
        return true;
    }

    /// <summary>
    /// Robust noise estimate from the second differences <c>2 x[i] - x[i-2] - x[i+2]</c> along each row (median of the non-zero
    /// values, scaled so that it estimates the standard deviation of Gaussian noise). Tiles narrower than 5 pixels use the first
    /// differences of consecutive pixels instead.
    /// </summary>
    internal static double EstimateNoise(ReadOnlySpan<double> values, int width)
    {
        double[] diffs = System.Buffers.ArrayPool<double>.Shared.Rent(values.Length);
        try
        {
            return EstimateNoise(values, width, diffs);
        }
        finally
        {
            System.Buffers.ArrayPool<double>.Shared.Return(diffs);
        }
    }

    private static double EstimateNoise(ReadOnlySpan<double> values, int width, double[] diffs)
    {
        int count = 0;
        double scale = 0.6052697;
        if (width < 5)
        {
            // too narrow for the second differences: median |x[i+1] - x[i]| = 0.6745 * sqrt(2) * sigma
            for (int i = 1; i < values.Length; i++)
            {
                double d = Math.Abs(values[i] - values[i - 1]);
                if (d > 0) diffs[count++] = d;
            }
            scale = 1.0483;
            return count == 0 ? 0 : scale * MedianOf(diffs, count);
        }
        for (int start = 0; start + width <= values.Length; start += width)
        {
            int end = start + width - 2;
            for (int i = start + 2; i < end; i++)
            {
                double d = Math.Abs(2 * values[i] - values[i - 2] - values[i + 2]);
                if (d > 0) diffs[count++] = d; // NaN comparisons are false, so undefined pixels are skipped
            }
        }
        return count == 0 ? 0 : scale * MedianOf(diffs, count);
    }

    private static double MedianOf(double[] diffs, int count)
    {
        int mid = count / 2;
        double upper = Select(diffs, count, mid);
        if (count % 2 == 1) return upper;
        double lower = double.NegativeInfinity;
        for (int i = 0; i < mid; i++) lower = Math.Max(lower, diffs[i]); // after Select, a[0..mid) <= a[mid]
        return 0.5 * (lower + upper);
    }

    /// <summary>Rearranges <paramref name="a"/> so that element <paramref name="k"/> is the one a full sort would put there (Wirth).</summary>
    private static double Select(double[] a, int n, int k)
    {
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            double pivot = a[k];
            int i = lo, j = hi;
            do
            {
                while (a[i] < pivot) i++;
                while (pivot < a[j]) j--;
                if (i <= j)
                {
                    (a[i], a[j]) = (a[j], a[i]);
                    i++;
                    j--;
                }
            } while (i <= j);
            if (j < k) lo = i;
            if (k < i) hi = j;
        }
        return a[k];
    }
}
