using System.Numerics;
using System.Runtime.InteropServices;

namespace FitsNet;

/// <summary>An image: the primary array or an <c>XTENSION = 'IMAGE'</c> extension.</summary>
public sealed class ImageHdu : Hdu
{
    /// <summary>Creates an empty image (NAXIS = 0).</summary>
    public ImageHdu() : base(new FitsHeader(), DataSource.Empty)
    {
        RebuildMandatory(Header, true, null, BitPix.Byte, [], 0, 1, false, false);
    }

    internal ImageHdu(FitsHeader header, DataSource source) : base(header, source) { }

    internal override bool CanBePrimary => true;

    internal override HduLayout GetLayout(bool primary) => HduLayout.Parse(Header, primary);

    private HduLayout Layout => GetLayout(IsPrimaryForm);

    /// <summary>The data representation (<c>BITPIX</c>).</summary>
    public BitPix BitPix => Layout.BitPix;

    /// <summary>The length of each axis (<c>NAXIS1</c>, <c>NAXIS2</c>, ...), the fastest varying axis first.</summary>
    public IReadOnlyList<long> Axes => Layout.Axes;

    /// <summary>Total number of pixels (0 when there are no axes).</summary>
    public long PixelCount => Layout.ElementsPerGroup;

    /// <summary>The XTENSION value: <c>IMAGE</c>, or <c>IUEIMAGE</c> for the prototype extension of Appendix F.2, which has the same format.</summary>
    private string ExtensionName => Header.Get<string>("XTENSION")?.Trim() == "IUEIMAGE" ? "IUEIMAGE" : "IMAGE";

    internal override void Normalize(bool primary, bool hasExtensions)
    {
        var l = Layout;
        if (l.IsRandomGroups) throw new FitsException("An image HDU cannot use the random groups structure.");
        RebuildMandatory(Header, primary, ExtensionName, l.BitPix, l.Axes, l.ParameterCount, l.GroupCount, false, hasExtensions);
    }

    /// <summary>Tile-compresses this image (see <see cref="CompressedImageHdu.Compress"/>).</summary>
    /// <param name="options">The compression options, or null for Rice compression with default settings.</param>
    public CompressedImageHdu Compress(FitsNet.Compression.CompressionOptions? options = null) => CompressedImageHdu.Compress(this, options);

    // ---- Regions ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Extracts a rectangular part of the image as a new image. Only the bytes of the region are read from a lazily opened
    /// file, so any part of an image of any size can be read. <c>CRPIXn</c> (all alternate WCS too) is shifted so that the world
    /// coordinates of the region stay correct; CHECKSUM and DATASUM are dropped.
    /// </summary>
    /// <param name="origin">Zero-based first pixel along each axis.</param>
    /// <param name="shape">Number of pixels along each axis.</param>
    public ImageHdu Subimage(long[] origin, long[] shape)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(shape);
        var layout = Layout;
        long[] axes = layout.Axes;
        int n = axes.Length;
        if (origin.Length != n || shape.Length != n) throw new ArgumentException($"The image has {n} axes: origin and shape need {n} values.");
        for (int d = 0; d < n; d++)
        {
            if (origin[d] < 0 || origin[d] > axes[d])
                throw new ArgumentOutOfRangeException(nameof(origin), $"The origin is outside axis {d + 1} (length {axes[d]}).");
            if (shape[d] < 0 || shape[d] > axes[d] - origin[d])
                throw new ArgumentOutOfRangeException(nameof(shape), $"The region exceeds axis {d + 1} (length {axes[d]}).");
        }

        int elementSize = layout.BitPix.BytesPerElement();
        long total = n == 0 ? 0 : 1, byteCount;
        try
        {
            foreach (long s in shape) total = checked(total * s);
            byteCount = checked(total * elementSize);
        }
        catch (OverflowException)
        {
            throw new ArgumentException("The region is too large.", nameof(shape));
        }
        if (byteCount > Array.MaxLength) throw new ArgumentException("The region is larger than 2 GB; read it in several parts.", nameof(shape));
        var output = new byte[byteCount];

        if (total > 0)
        {
            // Axes 0..k-1 are taken whole, so axis k is the first one that cuts the data: one run is contiguous in the file.
            int k = 0;
            while (k < n - 1 && origin[k] == 0 && shape[k] == axes[k]) k++;
            long lower = 1;
            var strides = new long[n];
            for (int d = 0; d < n; d++)
            {
                strides[d] = lower;
                lower *= axes[d];
            }
            long runElements = strides[k] * shape[k];
            int runBytes = checked((int)(runElements * elementSize));

            var idx = new long[n];
            long outOffset = 0;
            while (true)
            {
                long source = origin[k] * strides[k];
                for (int d = k + 1; d < n; d++) source += (origin[d] + idx[d]) * strides[d];
                Source.ReadRange(source * elementSize, output.AsSpan((int)outOffset, runBytes));
                outOffset += runBytes;

                int axis = k + 1;
                for (; axis < n; axis++)
                {
                    if (++idx[axis] < shape[axis]) break;
                    idx[axis] = 0;
                }
                if (axis >= n) break;
            }
        }

        var header = new FitsHeader();
        header.ReplaceAll(Header.Where(c => c.Kind != FitsCardKind.Value || c.Keyword is not ("CHECKSUM" or "DATASUM")).ToList());
        RebuildMandatory(header, true, null, layout.BitPix, shape, 0, 1, false, false);
        foreach (var card in header.ToList())
        {
            if (card.Kind != FitsCardKind.Value || !card.Keyword.StartsWith("CRPIX", StringComparison.Ordinal)) continue;
            string rest = card.Keyword[5..].TrimEnd('A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z');
            if (rest.Length == 0 || card.Keyword.Length - 5 - rest.Length > 1 || !int.TryParse(rest, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int axisNumber) || axisNumber < 1 || axisNumber > n) continue;
            if (card.Value is long or double) header.Set(card.Keyword, Convert.ToDouble(card.Value) - origin[axisNumber - 1]);
        }
        // IRAF logical-to-physical offsets move with the region; other pixel-position keywords (DATASEC, TRIMSEC, BIASSEC, ...) are kept as they are.
        for (int d = 0; d < n; d++)
            if (header.TryGetValue("LTV" + (d + 1), out double ltv)) header.Set("LTV" + (d + 1), ltv - origin[d]);
        return new ImageHdu(header, new MemoryDataSource(output));
    }

    /// <summary>Reads physical pixel values of a region (see <see cref="Subimage"/> and <see cref="ReadPixels{T}"/>).</summary>
    /// <typeparam name="T">The type the physical values are returned as.</typeparam>
    /// <param name="origin">The zero-based position of the first pixel of the region on each axis.</param>
    /// <param name="shape">The length of the region on each axis.</param>
    public T[] ReadRegion<T>(long[] origin, long[] shape) where T : unmanaged => Subimage(origin, shape).ReadPixels<T>();

    /// <summary>Reads stored pixel values of a region (see <see cref="Subimage"/> and <see cref="ReadRawPixels{T}"/>).</summary>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    /// <param name="origin">The zero-based position of the first pixel of the region on each axis.</param>
    /// <param name="shape">The length of the region on each axis.</param>
    public T[] ReadRawRegion<T>(long[] origin, long[] shape) where T : unmanaged => Subimage(origin, shape).ReadRawPixels<T>();

    // ---- Writing -------------------------------------------------------------------------------------------------

    /// <summary>Creates an image from pixels. Supported types: byte, sbyte, short, ushort, int, uint, long, ulong, float, double.</summary>
    /// <param name="pixels">Pixels with the first axis varying fastest (FITS order).</param>
    /// <param name="axes">NAXIS1..NAXISn.</param>
    /// <typeparam name="T">The pixel type: <c>byte</c>, <c>short</c>, <c>int</c>, <c>long</c>, <c>float</c> or <c>double</c>; <c>sbyte</c>, <c>ushort</c>, <c>uint</c> and <c>ulong</c> use the <c>BZERO</c> convention.</typeparam>
    public static ImageHdu Create<T>(ReadOnlySpan<T> pixels, params long[] axes) where T : unmanaged
    {
        var hdu = new ImageHdu();
        hdu.SetPixels(pixels, axes);
        return hdu;
    }

    /// <summary>Replaces the pixel data, BITPIX and axes; BSCALE, BZERO and BLANK are reset (BZERO is set for unsigned types).</summary>
    /// <typeparam name="T">The pixel type (see <see cref="Create{T}"/>).</typeparam>
    /// <param name="pixels">The pixels, the first axis varying fastest.</param>
    /// <param name="axes">The length of each axis.</param>
    public void SetPixels<T>(ReadOnlySpan<T> pixels, params long[] axes) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(axes);
        if (axes.Length > HduLayout.MaxAxes) throw new ArgumentException("Too many axes.", nameof(axes));
        long count = 0;
        if (axes.Length > 0)
        {
            count = 1;
            foreach (long a in axes)
                if (a < 0) throw new ArgumentException("Axis lengths must not be negative.", nameof(axes));
            if (axes.Contains(0)) count = 0;
            else
            {
                try
                {
                    foreach (long a in axes) count = checked(count * a);
                }
                catch (OverflowException)
                {
                    throw new ArgumentException("The image is too large.", nameof(axes));
                }
            }
        }
        if (pixels.Length != count)
            throw new ArgumentException($"Expected {count} pixels for the given axes but got {pixels.Length}.", nameof(pixels));

        byte[] raw = Encode(pixels, out BitPix bitPix, out object? bzero);

        bool primary = IsPrimaryForm;
        Header.Remove("BSCALE");
        Header.Remove("BZERO");
        Header.Remove("BLANK");
        RebuildMandatory(Header, primary, ExtensionName, bitPix, axes, 0, 1, false, false);
        if (bzero is not null) Header.Set("BZERO", bzero, "offset data range to that of unsigned integer");
        Source = new MemoryDataSource(raw);
    }

    internal static byte[] Encode<T>(ReadOnlySpan<T> p, out BitPix bitPix, out object? bzero) where T : unmanaged
    {
        bzero = null;
        if (typeof(T) == typeof(byte)) { bitPix = BitPix.Byte; return MemoryMarshal.Cast<T, byte>(p).ToArray(); }
        if (typeof(T) == typeof(sbyte))
        {
            bitPix = BitPix.Byte;
            bzero = -128L;
            var s = MemoryMarshal.Cast<T, sbyte>(p);
            var r = new byte[s.Length];
            for (int i = 0; i < r.Length; i++) r[i] = unchecked((byte)((byte)s[i] ^ 0x80));
            return r;
        }
        if (typeof(T) == typeof(short)) { bitPix = BitPix.Int16; return BigEndian.FromArray(MemoryMarshal.Cast<T, short>(p)); }
        if (typeof(T) == typeof(ushort))
        {
            bitPix = BitPix.Int16;
            bzero = 32768L;
            var u = MemoryMarshal.Cast<T, ushort>(p);
            var t = new short[u.Length];
            for (int i = 0; i < t.Length; i++) t[i] = unchecked((short)(u[i] ^ 0x8000));
            return BigEndian.FromArray<short>(t);
        }
        if (typeof(T) == typeof(int)) { bitPix = BitPix.Int32; return BigEndian.FromArray(MemoryMarshal.Cast<T, int>(p)); }
        if (typeof(T) == typeof(uint))
        {
            bitPix = BitPix.Int32;
            bzero = 2147483648L;
            var u = MemoryMarshal.Cast<T, uint>(p);
            var t = new int[u.Length];
            for (int i = 0; i < t.Length; i++) t[i] = unchecked((int)(u[i] ^ 0x80000000u));
            return BigEndian.FromArray<int>(t);
        }
        if (typeof(T) == typeof(long)) { bitPix = BitPix.Int64; return BigEndian.FromArray(MemoryMarshal.Cast<T, long>(p)); }
        if (typeof(T) == typeof(ulong))
        {
            bitPix = BitPix.Int64;
            bzero = new BigInteger(1UL << 63);
            var u = MemoryMarshal.Cast<T, ulong>(p);
            var t = new long[u.Length];
            for (int i = 0; i < t.Length; i++) t[i] = unchecked((long)(u[i] ^ 0x8000000000000000UL));
            return BigEndian.FromArray<long>(t);
        }
        if (typeof(T) == typeof(float)) { bitPix = BitPix.Single; return BigEndian.FromArray(MemoryMarshal.Cast<T, float>(p)); }
        if (typeof(T) == typeof(double)) { bitPix = BitPix.Double; return BigEndian.FromArray(MemoryMarshal.Cast<T, double>(p)); }
        throw new NotSupportedException($"Pixel type {typeof(T).Name} is not supported.");
    }

    // ---- Reading -------------------------------------------------------------------------------------------------

    private double BScale => Header.TryGetValue("BSCALE", out double v) ? v : 1.0;

    private double BZero => Header.TryGetValue("BZERO", out double v) ? v : 0.0;

    /// <summary>
    /// Reads the stored values without applying BSCALE/BZERO or BLANK. <typeparamref name="T"/> must match BITPIX
    /// (otherwise <see cref="InvalidOperationException"/>).
    /// </summary>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    public T[] ReadRawPixels<T>() where T : unmanaged
    {
        var layout = Layout;
        if (typeof(T) != layout.BitPix.NativeType())
            throw new InvalidOperationException($"BITPIX {(int)layout.BitPix} stores {layout.BitPix.NativeType().Name}, not {typeof(T).Name}.");
        return BigEndian.ToArray<T>(Source.ReadMemory().Span);
    }

    /// <summary>
    /// Reads physical pixel values. <c>double</c> and <c>float</c> always work (BSCALE/BZERO applied, BLANK becomes NaN).
    /// Integer types work when no scaling is needed, or for the standard unsigned conventions
    /// (<c>sbyte</c>, <c>ushort</c>, <c>uint</c>, <c>ulong</c> with the matching BZERO).
    /// BLANK is only mapped to NaN for <c>double</c>/<c>float</c>; integer reads return the sentinel as an ordinary value.
    /// Reading 64-bit integers as <c>double</c> loses precision above 2^53.
    /// </summary>
    /// <typeparam name="T">The type the physical values are returned as (<c>BSCALE</c> and <c>BZERO</c> applied), for example <c>float</c> or <c>double</c>.</typeparam>
    public T[] ReadPixels<T>() where T : unmanaged
    {
        var layout = Layout;
        double bscale = BScale, bzero = BZero;
        Type type = typeof(T);

        if (type == layout.BitPix.NativeType() && bscale == 1 && bzero == 0) return ReadRawPixels<T>();
        if (type == typeof(double)) return (T[])(object)ReadScaled(layout, bscale, bzero);
        if (type == typeof(float))
        {
            double[] d = ReadScaled(layout, bscale, bzero);
            var f = new float[d.Length];
            for (int i = 0; i < f.Length; i++) f[i] = (float)d[i];
            return (T[])(object)f;
        }

        if (bscale == 1)
        {
            if (type == typeof(sbyte) && layout.BitPix == BitPix.Byte && bzero == -128)
            {
                ReadOnlySpan<byte> raw = Source.ReadMemory().Span;
                var r = new sbyte[raw.Length];
                for (int i = 0; i < r.Length; i++) r[i] = unchecked((sbyte)(raw[i] ^ 0x80));
                return (T[])(object)r;
            }
            if (type == typeof(ushort) && layout.BitPix == BitPix.Int16 && bzero == 32768)
            {
                short[] raw = BigEndian.ToArray<short>(Source.ReadMemory().Span);
                var r = new ushort[raw.Length];
                for (int i = 0; i < r.Length; i++) r[i] = unchecked((ushort)(raw[i] ^ 0x8000));
                return (T[])(object)r;
            }
            if (type == typeof(uint) && layout.BitPix == BitPix.Int32 && bzero == 2147483648.0)
            {
                int[] raw = BigEndian.ToArray<int>(Source.ReadMemory().Span);
                var r = new uint[raw.Length];
                for (int i = 0; i < r.Length; i++) r[i] = unchecked((uint)(raw[i] ^ int.MinValue));
                return (T[])(object)r;
            }
            if (type == typeof(ulong) && layout.BitPix == BitPix.Int64 && bzero == 9223372036854775808.0)
            {
                long[] raw = BigEndian.ToArray<long>(Source.ReadMemory().Span);
                var r = new ulong[raw.Length];
                for (int i = 0; i < r.Length; i++) r[i] = unchecked((ulong)(raw[i] ^ long.MinValue));
                return (T[])(object)r;
            }
        }

        throw new InvalidOperationException(
            $"Cannot read BITPIX {(int)layout.BitPix} data with BSCALE={bscale}, BZERO={bzero} as {type.Name}; use double or float.");
    }

    private double[] ReadScaled(HduLayout layout, double bscale, double bzero)
    {
        long? blank = !layout.BitPix.IsFloatingPoint() && Header.TryGetValue("BLANK", out long b) ? b : null;
        double[] values = PixelConvert.ToDoubles(Source.ReadMemory().Span, layout.BitPix, blank);
        if (bscale != 1 || bzero != 0)
            for (int i = 0; i < values.Length; i++) values[i] = bzero + bscale * values[i];
        return values;
    }
}

internal static class PixelConvert
{
    /// <summary>Raw values as doubles; integers equal to <paramref name="blank"/> become NaN.</summary>
    public static double[] ToDoubles(ReadOnlySpan<byte> data, BitPix bitPix, long? blank)
    {
        switch (bitPix)
        {
            case BitPix.Byte:
            {
                var r = new double[data.Length];
                for (int i = 0; i < r.Length; i++) r[i] = blank == data[i] ? double.NaN : data[i];
                return r;
            }
            case BitPix.Int16:
            {
                short[] s = BigEndian.ToArray<short>(data);
                var r = new double[s.Length];
                for (int i = 0; i < r.Length; i++) r[i] = blank == s[i] ? double.NaN : s[i];
                return r;
            }
            case BitPix.Int32:
            {
                int[] s = BigEndian.ToArray<int>(data);
                var r = new double[s.Length];
                for (int i = 0; i < r.Length; i++) r[i] = blank == s[i] ? double.NaN : s[i];
                return r;
            }
            case BitPix.Int64:
            {
                long[] s = BigEndian.ToArray<long>(data);
                var r = new double[s.Length];
                for (int i = 0; i < r.Length; i++) r[i] = blank == s[i] ? double.NaN : s[i];
                return r;
            }
            case BitPix.Single:
            {
                float[] s = BigEndian.ToArray<float>(data);
                var r = new double[s.Length];
                for (int i = 0; i < r.Length; i++) r[i] = s[i];
                return r;
            }
            default:
                return BigEndian.ToArray<double>(data);
        }
    }
}
