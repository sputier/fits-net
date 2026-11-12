using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Eleris.FitsNet;

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
    public CompressedImageHdu Compress(Eleris.FitsNet.Compression.CompressionOptions? options = null) => CompressedImageHdu.Compress(this, options);

    // ---- Regions ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Extracts a rectangular part of the image as a new image. Only the bytes of the region are read from a lazily opened
    /// file, so any part of an image of any size can be read. <c>CRPIXn</c> (all alternate WCS too) is shifted so that the world
    /// coordinates of the region stay correct; CHECKSUM and DATASUM are dropped.
    /// </summary>
    /// <param name="origin">Zero-based first pixel along each axis.</param>
    /// <param name="shape">Number of pixels along each axis.</param>
    public ImageHdu Subimage(long[] origin, long[] shape) => Subimage(origin, shape, CancellationToken.None);

    // The most bytes read at once by a region read that can be cancelled.
    private const int CancellableReadBlock = 1 << 20;

    /// <summary>
    /// Extracts a rectangular part of the image as a new image, as <see cref="Subimage(long[], long[])"/> does, checking
    /// <paramref name="cancellationToken"/> between the runs of pixels read, and every megabyte within a long run.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <param name="origin">Zero-based first pixel along each axis.</param>
    /// <param name="shape">Number of pixels along each axis.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    public ImageHdu Subimage(long[] origin, long[] shape, CancellationToken cancellationToken)
    {
        var region = PlanRegion(origin, shape);
        while (region.NextRun(out long source, out int target))
        {
            if (!cancellationToken.CanBeCanceled)
            {
                Source.ReadRange(source, region.Output.AsSpan(target, region.RunBytes));
                continue;
            }
            // a run can be the whole region (whole rows): read it in blocks, so that the token is checked during a long read
            for (int done = 0, part; done < region.RunBytes; done += part)
            {
                cancellationToken.ThrowIfCancellationRequested();
                part = Math.Min(CancellableReadBlock, region.RunBytes - done);
                Source.ReadRange(source + done, region.Output.AsSpan(target + done, part));
            }
        }
        return RegionImage(region);
    }

    /// <summary>Asynchronous version of <see cref="Subimage(long[], long[], CancellationToken)"/>: the runs are read asynchronously.</summary>
    /// <remarks>
    /// Short runs close to each other (a narrow cutout has one per row) are read together, with the bytes between them, in one asynchronous
    /// read of at most a megabyte: one wait per megabyte instead of one per run.
    /// </remarks>
    private async Task<ImageHdu> SubimageAsync(long[] origin, long[] shape, CancellationToken cancellationToken)
    {
        const int MaxGap = 64 * 1024;
        var region = PlanRegion(origin, shape);
        int run = region.RunBytes;
        var group = new List<(long Source, int Target)>();
        byte[]? block = null;
        try
        {
            bool more = region.NextRun(out long source, out int target);
            while (more)
            {
                group.Clear();
                group.Add((source, target));
                long start = source, end = source + run;
                while ((more = region.NextRun(out source, out target)) && source >= end && source - end <= MaxGap
                       && source + run - start <= CancellableReadBlock)
                {
                    group.Add((source, target));
                    end = source + run;
                }

                if (group.Count == 1)
                {
                    // a long run (or a lone one) is read in place, a megabyte at a time
                    var (first, at) = group[0];
                    for (int done = 0, part; done < run; done += part)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        part = Math.Min(CancellableReadBlock, run - done);
                        await Source.ReadRangeAsync(first + done, region.Output.AsMemory(at + done, part), cancellationToken).ConfigureAwait(false);
                    }
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                block ??= System.Buffers.ArrayPool<byte>.Shared.Rent(CancellableReadBlock);
                int length = (int)(end - start);
                await Source.ReadRangeAsync(start, block.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                foreach (var (from, to) in group) block.AsSpan((int)(from - start), run).CopyTo(region.Output.AsSpan(to, run));
            }
        }
        finally
        {
            if (block is not null) System.Buffers.ArrayPool<byte>.Shared.Return(block);
        }
        return RegionImage(region);
    }

    /// <summary>
    /// The runs of a region: the bytes of the region that are contiguous in the data, each with its place in the output. Axes 0..k-1 are
    /// taken whole, so axis k is the first one that cuts the data.
    /// </summary>
    private sealed class RegionPlan(long[] origin, long[] shape, long[] axes, int elementSize, byte[] output, long total)
    {
        private readonly int _n = axes.Length;
        private readonly long[] _idx = new long[axes.Length];
        private readonly long[] _strides = Strides(axes);
        private readonly int _k = FirstCutAxis(origin, shape, axes);
        private long _outOffset;
        private bool _done = total == 0;

        /// <summary>The bytes of each run.</summary>
        public int RunBytes { get; } = total == 0 ? 0 : RunLength(origin, shape, axes, elementSize);

        private static int RunLength(long[] origin, long[] shape, long[] axes, int elementSize)
        {
            int k = FirstCutAxis(origin, shape, axes);
            return checked((int)(Strides(axes)[k] * shape[k] * elementSize));
        }

        public long[] Origin => origin;

        public long[] Shape => shape;

        public byte[] Output => output;

        private static long[] Strides(long[] axes)
        {
            var strides = new long[axes.Length];
            long lower = 1;
            for (int d = 0; d < axes.Length; d++)
            {
                strides[d] = lower;
                lower *= axes[d];
            }
            return strides;
        }

        private static int FirstCutAxis(long[] origin, long[] shape, long[] axes)
        {
            int k = 0;
            while (k < axes.Length - 1 && origin[k] == 0 && shape[k] == axes[k]) k++;
            return k;
        }

        /// <summary>The next run: its first byte in the data and in <see cref="Output"/>; false after the last one.</summary>
        public bool NextRun(out long source, out int target)
        {
            source = 0;
            target = 0;
            if (_done) return false;
            long element = origin[_k] * _strides[_k];
            for (int d = _k + 1; d < _n; d++) element += (origin[d] + _idx[d]) * _strides[d];
            source = element * elementSize;
            target = (int)_outOffset;
            _outOffset += RunBytes;

            int axis = _k + 1;
            for (; axis < _n; axis++)
            {
                if (++_idx[axis] < shape[axis]) break;
                _idx[axis] = 0;
            }
            _done = axis >= _n;
            return true;
        }
    }

    /// <summary>Checks a region and allocates its pixels.</summary>
    private RegionPlan PlanRegion(long[] origin, long[] shape)
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

        return new RegionPlan(origin, shape, axes, elementSize, output, total);
    }

    /// <summary>The image of a region whose pixels have been read: the header is adapted to the region.</summary>
    private ImageHdu RegionImage(RegionPlan region)
    {
        long[] origin = region.Origin, shape = region.Shape;
        var layout = Layout;
        int n = shape.Length;
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
        return new ImageHdu(header, new MemoryDataSource(region.Output));
    }

    /// <summary>Reads physical pixel values of a region (see <see cref="Subimage(long[], long[])"/> and <see cref="ReadPixels{T}()"/>).</summary>
    /// <typeparam name="T">The type the physical values are returned as.</typeparam>
    /// <param name="origin">The zero-based position of the first pixel of the region on each axis.</param>
    /// <param name="shape">The length of the region on each axis.</param>
    public T[] ReadRegion<T>(long[] origin, long[] shape) where T : unmanaged => Subimage(origin, shape).ReadPixels<T>();

    /// <summary>
    /// Reads physical pixel values of a region, as <see cref="ReadRegion{T}(long[], long[])"/> does, checking
    /// <paramref name="cancellationToken"/> between the runs of pixels read and the blocks of values converted.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the physical values are returned as.</typeparam>
    /// <param name="origin">The zero-based position of the first pixel of the region on each axis.</param>
    /// <param name="shape">The length of the region on each axis.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    public T[] ReadRegion<T>(long[] origin, long[] shape, CancellationToken cancellationToken) where T : unmanaged =>
        Subimage(origin, shape, cancellationToken).ReadPixels<T>(cancellationToken);

    /// <summary>
    /// Asynchronous version of <see cref="ReadRegion{T}(long[], long[])"/>: the bytes of the region are read from a lazily opened file
    /// with asynchronous I/O, then converted. <paramref name="cancellationToken"/> is checked as <see cref="ReadRegion{T}(long[], long[], CancellationToken)"/> does.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the physical values are returned as.</typeparam>
    /// <param name="origin">The zero-based position of the first pixel of the region on each axis.</param>
    /// <param name="shape">The length of the region on each axis.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    public async Task<T[]> ReadRegionAsync<T>(long[] origin, long[] shape, CancellationToken cancellationToken = default) where T : unmanaged =>
        (await SubimageAsync(origin, shape, cancellationToken).ConfigureAwait(false)).ReadPixels<T>(cancellationToken);

    /// <summary>Reads stored pixel values of a region (see <see cref="Subimage(long[], long[])"/> and <see cref="ReadRawPixels{T}()"/>).</summary>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    /// <param name="origin">The zero-based position of the first pixel of the region on each axis.</param>
    /// <param name="shape">The length of the region on each axis.</param>
    public T[] ReadRawRegion<T>(long[] origin, long[] shape) where T : unmanaged => Subimage(origin, shape).ReadRawPixels<T>();

    /// <summary>
    /// Reads stored pixel values of a region, as <see cref="ReadRawRegion{T}(long[], long[])"/> does, checking
    /// <paramref name="cancellationToken"/> between the runs of pixels read and the blocks of values converted.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    /// <param name="origin">The zero-based position of the first pixel of the region on each axis.</param>
    /// <param name="shape">The length of the region on each axis.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    public T[] ReadRawRegion<T>(long[] origin, long[] shape, CancellationToken cancellationToken) where T : unmanaged =>
        Subimage(origin, shape, cancellationToken).ReadRawPixels<T>(cancellationToken);

    /// <summary>
    /// Asynchronous version of <see cref="ReadRawRegion{T}(long[], long[])"/>: the bytes of the region are read from a lazily opened file
    /// with asynchronous I/O. <paramref name="cancellationToken"/> is checked as <see cref="ReadRawRegion{T}(long[], long[], CancellationToken)"/> does.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    /// <param name="origin">The zero-based position of the first pixel of the region on each axis.</param>
    /// <param name="shape">The length of the region on each axis.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    public async Task<T[]> ReadRawRegionAsync<T>(long[] origin, long[] shape, CancellationToken cancellationToken = default) where T : unmanaged =>
        (await SubimageAsync(origin, shape, cancellationToken).ConfigureAwait(false)).ReadRawPixels<T>(cancellationToken);

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

    /// <summary>
    /// Creates an image from planes, without copying them: a plane is all the pixels that share one index on the last axis (the planes of a
    /// cube, the rows of a 2D image). The planes are read again each time the data is needed: when the file is saved, the
    /// checksums are computed or the pixels are read, and the image keeps them after a save. So they must stay valid and unchanged as long
    /// as the image uses them (until it is discarded, or <see cref="SetPixels{T}"/> has replaced its data): a pooled or native buffer must
    /// not be returned before, and a plane changed between the checksum and the write of a save would give a wrong <c>DATASUM</c>.
    /// The bytes written are those of <see cref="Create{T}"/> for the same pixels.
    /// </summary>
    /// <example>
    /// <code>
    /// float[] red = ..., green = ..., blue = ...;          // width * height pixels each
    /// var image = ImageHdu.CreateFromPlanes&lt;float&gt;([red, green, blue], width, height, 3);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="planes"/> or <paramref name="axes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There is no axis, too many axes, a negative axis, a number of planes that is not the last axis, or a plane whose length is not the product of the other axes.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not a supported pixel type.</exception>
    /// <typeparam name="T">The pixel type (see <see cref="Create{T}"/>).</typeparam>
    /// <param name="planes">The planes in file order, each with the first axis varying fastest. The list is copied, not the pixels.</param>
    /// <param name="axes">NAXIS1..NAXISn; NAXISn is the number of planes.</param>
    public static ImageHdu CreateFromPlanes<T>(IReadOnlyList<ReadOnlyMemory<T>> planes, params long[] axes) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(planes);
        ArgumentNullException.ThrowIfNull(axes);
        if (axes.Length == 0) throw new ArgumentException("An image made of planes needs at least one axis.", nameof(axes));
        if (axes.Length > HduLayout.MaxAxes) throw new ArgumentException("Too many axes.", nameof(axes));
        if (axes.Any(a => a < 0)) throw new ArgumentException("Axis lengths must not be negative.", nameof(axes));
        if (planes.Count != axes[^1])
            throw new ArgumentException($"The last axis has {axes[^1]} planes but {planes.Count} were given.", nameof(planes));
        // The pixels of one plane, capped just above what a plane (one ReadOnlyMemory) can hold: no overflow, and no plane can match it.
        const long tooLarge = int.MaxValue + 1L;
        long planeLength = 1;
        foreach (long a in axes.AsSpan(0, axes.Length - 1)) planeLength = Math.Min(planeLength * Math.Min(a, tooLarge), tooLarge);
        if (planeLength > int.MaxValue) throw new ArgumentException("A plane would hold more pixels than a ReadOnlyMemory can.", nameof(axes));
        var (bitPix, bzero, flip) = Describe<T>();
        var copy = new ReadOnlyMemory<T>[planes.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = planes[i];
            if (copy[i].Length != planeLength)
                throw new ArgumentException($"Plane {i} has {copy[i].Length} pixels but the other axes need {planeLength}.", nameof(planes));
        }
        PlaneDataSource<T> source;
        try
        {
            source = new PlaneDataSource<T>(copy, (int)planeLength, flip);
        }
        catch (OverflowException)
        {
            throw new ArgumentException("The image is too large.", nameof(axes));
        }
        var hdu = new ImageHdu();
        hdu.SetData(source, bitPix, bzero, axes);
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
        SetData(new MemoryDataSource(raw), bitPix, bzero, axes);
    }

    private void SetData(DataSource source, BitPix bitPix, object? bzero, long[] axes)
    {
        bool primary = IsPrimaryForm;
        Header.Remove("BSCALE");
        Header.Remove("BZERO");
        Header.Remove("BLANK");
        RebuildMandatory(Header, primary, ExtensionName, bitPix, axes, 0, 1, false, false);
        if (bzero is not null) Header.Set("BZERO", bzero, "offset data range to that of unsigned integer");
        Source = source;
    }

    /// <summary>
    /// How pixels of type <typeparamref name="T"/> are stored: the BITPIX, the BZERO of the unsigned conventions, and whether the top bit of
    /// each value is flipped to store it (the same as subtracting that BZERO). Must agree with <see cref="Encode{T}"/>.
    /// </summary>
    internal static (BitPix BitPix, object? BZero, bool FlipTopBit) Describe<T>() where T : unmanaged
    {
        if (typeof(T) == typeof(byte)) return (BitPix.Byte, null, false);
        if (typeof(T) == typeof(sbyte)) return (BitPix.Byte, -128L, true);
        if (typeof(T) == typeof(short)) return (BitPix.Int16, null, false);
        if (typeof(T) == typeof(ushort)) return (BitPix.Int16, 32768L, true);
        if (typeof(T) == typeof(int)) return (BitPix.Int32, null, false);
        if (typeof(T) == typeof(uint)) return (BitPix.Int32, 2147483648L, true);
        if (typeof(T) == typeof(long)) return (BitPix.Int64, null, false);
        if (typeof(T) == typeof(ulong)) return (BitPix.Int64, new BigInteger(1UL << 63), true);
        if (typeof(T) == typeof(float)) return (BitPix.Single, null, false);
        if (typeof(T) == typeof(double)) return (BitPix.Double, null, false);
        throw new NotSupportedException($"Pixel type {typeof(T).Name} is not supported.");
    }

    internal static byte[] Encode<T>(ReadOnlySpan<T> p, out BitPix bitPix, out object? bzero) where T : unmanaged
    {
        bzero = null;
        if (typeof(T) == typeof(byte)) { bitPix = BitPix.Byte; return MemoryMarshal.Cast<T, byte>(p).ToArray(); }
        if (typeof(T) == typeof(sbyte))
        {
            bitPix = BitPix.Byte;
            bzero = -128L;
            return BigEndian.FromArrayFlippingTopBit(MemoryMarshal.Cast<T, sbyte>(p));
        }
        if (typeof(T) == typeof(short)) { bitPix = BitPix.Int16; return BigEndian.FromArray(MemoryMarshal.Cast<T, short>(p)); }
        if (typeof(T) == typeof(ushort))
        {
            bitPix = BitPix.Int16;
            bzero = 32768L;
            return BigEndian.FromArrayFlippingTopBit(MemoryMarshal.Cast<T, ushort>(p));
        }
        if (typeof(T) == typeof(int)) { bitPix = BitPix.Int32; return BigEndian.FromArray(MemoryMarshal.Cast<T, int>(p)); }
        if (typeof(T) == typeof(uint))
        {
            bitPix = BitPix.Int32;
            bzero = 2147483648L;
            return BigEndian.FromArrayFlippingTopBit(MemoryMarshal.Cast<T, uint>(p));
        }
        if (typeof(T) == typeof(long)) { bitPix = BitPix.Int64; return BigEndian.FromArray(MemoryMarshal.Cast<T, long>(p)); }
        if (typeof(T) == typeof(ulong))
        {
            bitPix = BitPix.Int64;
            bzero = new BigInteger(1UL << 63);
            return BigEndian.FromArrayFlippingTopBit(MemoryMarshal.Cast<T, ulong>(p));
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
    public T[] ReadRawPixels<T>() where T : unmanaged => ReadRawPixels<T>(CancellationToken.None);

    /// <summary>
    /// Reads the stored values, as <see cref="ReadRawPixels{T}()"/> does, checking <paramref name="cancellationToken"/> between blocks
    /// of data.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    public T[] ReadRawPixels<T>(CancellationToken cancellationToken) where T : unmanaged => RawPixelsRead<T>().Run(Source, cancellationToken);

    /// <summary>
    /// Asynchronous version of <see cref="ReadRawPixels{T}()"/>: the data of a lazily opened file is read with asynchronous I/O, a block at a
    /// time, and each block is converted between two reads. <paramref name="cancellationToken"/> is checked between blocks. Data already in
    /// memory has no I/O to wait for: it is converted on the calling thread.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    public async Task<T[]> ReadRawPixelsAsync<T>(CancellationToken cancellationToken = default) where T : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await RawPixelsRead<T>().RunAsync(Source, cancellationToken).ConfigureAwait(false);
    }

    private ChunkedRead<T[]> RawPixelsRead<T>() where T : unmanaged
    {
        var layout = Layout;
        if (typeof(T) != layout.BitPix.NativeType())
            throw new InvalidOperationException($"BITPIX {(int)layout.BitPix} stores {layout.BitPix.NativeType().Name}, not {typeof(T).Name}.");
        return BigEndian.ArrayRead<T>(Source);
    }

    /// <summary>
    /// Reads physical pixel values. <c>double</c> and <c>float</c> always work (BSCALE/BZERO applied, BLANK becomes NaN).
    /// Integer types work when no scaling is needed, or for the standard unsigned conventions
    /// (<c>sbyte</c>, <c>ushort</c>, <c>uint</c>, <c>ulong</c> with the matching BZERO).
    /// BLANK is only mapped to NaN for <c>double</c>/<c>float</c>; integer reads return the sentinel as an ordinary value.
    /// Reading 64-bit integers as <c>double</c> loses precision above 2^53.
    /// </summary>
    /// <typeparam name="T">The type the physical values are returned as (<c>BSCALE</c> and <c>BZERO</c> applied), for example <c>float</c> or <c>double</c>.</typeparam>
    public T[] ReadPixels<T>() where T : unmanaged => ReadPixels<T>(CancellationToken.None);

    /// <summary>
    /// Reads physical pixel values, as <see cref="ReadPixels{T}()"/> does, checking <paramref name="cancellationToken"/> between blocks
    /// of data.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the physical values are returned as (<c>BSCALE</c> and <c>BZERO</c> applied), for example <c>float</c> or <c>double</c>.</typeparam>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    public T[] ReadPixels<T>(CancellationToken cancellationToken) where T : unmanaged
    {
        // The scaled conversions are run directly: going through ChunkedRead makes the tier-1 code of the conversion loop 2 to 3 times
        // slower (measured with ImageIoBenchmarks), although both run the same conversion.
        var layout = Layout;
        double bscale = BScale, bzero = BZero;
        bool raw = typeof(T) == layout.BitPix.NativeType() && bscale == 1 && bzero == 0;
        if (!raw && (typeof(T) == typeof(double) || typeof(T) == typeof(float)))
        {
            long? blank = !layout.BitPix.IsFloatingPoint() && Header.TryGetValue("BLANK", out long b) ? b : null;
            return typeof(T) == typeof(double)
                ? (T[])(object)PixelConvert.ToFloatingNow<double>(Source, layout.BitPix, blank, bscale, bzero, cancellationToken)
                : (T[])(object)PixelConvert.ToFloatingNow<float>(Source, layout.BitPix, blank, bscale, bzero, cancellationToken);
        }
        return PixelsRead<T>().Run(Source, cancellationToken);
    }

    /// <summary>
    /// Asynchronous version of <see cref="ReadPixels{T}()"/>: the data of a lazily opened file is read with asynchronous I/O, a block at a
    /// time, and each block is converted between two reads. <paramref name="cancellationToken"/> is checked between blocks. Data already in
    /// memory has no I/O to wait for: it is converted on the calling thread.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the physical values are returned as (<c>BSCALE</c> and <c>BZERO</c> applied), for example <c>float</c> or <c>double</c>.</typeparam>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    public async Task<T[]> ReadPixelsAsync<T>(CancellationToken cancellationToken = default) where T : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await PixelsRead<T>().RunAsync(Source, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The read of physical values as <typeparamref name="T"/>: where they go and how each chunk of data is converted.</summary>
    private ChunkedRead<T[]> PixelsRead<T>() where T : unmanaged
    {
        var layout = Layout;
        double bscale = BScale, bzero = BZero;
        Type type = typeof(T);

        if (type == layout.BitPix.NativeType() && bscale == 1 && bzero == 0) return RawPixelsRead<T>();
        if (type == typeof(double)) return (ChunkedRead<T[]>)(object)ScaledRead<double>(layout, bscale, bzero);
        if (type == typeof(float)) return (ChunkedRead<T[]>)(object)ScaledRead<float>(layout, bscale, bzero);

        if (bscale == 1)
        {
            if (type == typeof(sbyte) && layout.BitPix == BitPix.Byte && bzero == -128)
            {
                var r = new sbyte[Source.ElementCount(1)];
                return (ChunkedRead<T[]>)(object)new ChunkedRead<sbyte[]>(r, 1, (bytes, first) =>
                {
                    Span<byte> part = MemoryMarshal.AsBytes(r.AsSpan(first, bytes.Length));
                    bytes.CopyTo(part);
                    BigEndian.Xor(part, (byte)0x80);
                });
            }
            if (type == typeof(ushort) && layout.BitPix == BitPix.Int16 && bzero == 32768)
            {
                return (ChunkedRead<T[]>)(object)BigEndian.ArrayXorRead<ushort>(Source, 0x8000);
            }
            if (type == typeof(uint) && layout.BitPix == BitPix.Int32 && bzero == 2147483648.0)
            {
                return (ChunkedRead<T[]>)(object)BigEndian.ArrayXorRead<uint>(Source, 0x80000000u);
            }
            if (type == typeof(ulong) && layout.BitPix == BitPix.Int64 && bzero == 9223372036854775808.0)
            {
                return (ChunkedRead<T[]>)(object)BigEndian.ArrayXorRead<ulong>(Source, 0x8000000000000000ul);
            }
        }

        throw new InvalidOperationException(
            $"Cannot read BITPIX {(int)layout.BitPix} data with BSCALE={bscale}, BZERO={bzero} as {type.Name}; use double or float.");
    }

    private ChunkedRead<TOut[]> ScaledRead<TOut>(HduLayout layout, double bscale, double bzero)
        where TOut : unmanaged, IFloatingPointIeee754<TOut>
    {
        long? blank = !layout.BitPix.IsFloatingPoint() && Header.TryGetValue("BLANK", out long b) ? b : null;
        return PixelConvert.ToFloating<TOut>(Source, layout.BitPix, blank, bscale, bzero);
    }
}

/// <summary>
/// A read of the whole data of an HDU a chunk at a time: the array that receives the values, and what converts each chunk into it. The same
/// read runs synchronously (<see cref="Run"/>) or with asynchronous I/O (<see cref="RunAsync"/>, the conversion running between two reads),
/// so both give the same values. <paramref name="release"/> gives back the buffers the conversion borrowed; a read runs once.
/// </summary>
internal sealed class ChunkedRead<TResult>(TResult result, int elementSize, DataSource.ChunkReader reader, Action? release = null)
{
    private Action? _release = release;

    public TResult Run(DataSource source, CancellationToken cancellationToken)
    {
        try
        {
            source.ReadInChunks(elementSize, reader, cancellationToken);
            return result;
        }
        finally
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();   // once, even if the read is run again
        }
    }

    public async Task<TResult> RunAsync(DataSource source, CancellationToken cancellationToken)
    {
        try
        {
            await source.ReadInChunksAsync(elementSize, reader, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();   // once, even if the read is run again
        }
    }
}

internal static class PixelConvert
{
    // Elements converted at a time: the values swapped from a chunk of the data stay in the cache while they are converted.
    private const int ConversionChunk = 32 * 1024;

    /// <summary>
    /// Physical values (<c>bzero + bscale * stored</c>, the integers equal to <paramref name="blank"/> becoming NaN) as <c>float</c> or
    /// <c>double</c>, converted a chunk at a time without any intermediate array. The arithmetic is done in double precision
    /// whatever the result type, so that a float result is the rounding of the double one.
    /// </summary>
    /// <summary>The synchronous read of <see cref="ToFloating{TOut}"/>, with the conversion inline as in 0.1.0 (see ImageHdu.ReadPixels).</summary>
    public static TOut[] ToFloatingNow<TOut>(DataSource source, BitPix bitPix, long? blank, double bscale, double bzero,
        CancellationToken cancellationToken)
        where TOut : unmanaged, IFloatingPointIeee754<TOut>
    {
        var result = GC.AllocateUninitializedArray<TOut>(source.ElementCount(bitPix.BytesPerElement()));
        switch (bitPix)
        {
            case BitPix.Byte: ConvertNow<byte, TOut>(source, result, blank, bscale, bzero, cancellationToken); break;
            case BitPix.Int16: ConvertNow<short, TOut>(source, result, blank, bscale, bzero, cancellationToken); break;
            case BitPix.Int32: ConvertNow<int, TOut>(source, result, blank, bscale, bzero, cancellationToken); break;
            case BitPix.Int64: ConvertNow<long, TOut>(source, result, blank, bscale, bzero, cancellationToken); break;
            case BitPix.Single: ConvertNow<float, TOut>(source, result, null, bscale, bzero, cancellationToken); break;
            default: ConvertNow<double, TOut>(source, result, null, bscale, bzero, cancellationToken); break;
        }
        return result;
    }

    private static void ConvertNow<TIn, TOut>(DataSource source, TOut[] result, long? blank, double bscale, double bzero, CancellationToken cancellationToken)
        where TIn : unmanaged, INumberBase<TIn>
        where TOut : unmanaged, IFloatingPointIeee754<TOut>
    {
        int size = Unsafe.SizeOf<TIn>();
        bool scaled = bscale != 1 || bzero != 0;
        TIn[] buffer = ArrayPool<TIn>.Shared.Rent(Math.Min(result.Length, ConversionChunk));
        try
        {
            source.ReadInChunks(size, (bytes, first) =>
            {
                for (int done = 0; done < bytes.Length / size; done += ConversionChunk)
                {
                    int n = Math.Min(ConversionChunk, bytes.Length / size - done);
                    Span<TIn> values = buffer.AsSpan(0, n);
                    BigEndian.Read(bytes.Slice(done * size, n * size), values);
                    Span<TOut> output = result.AsSpan(first + done, n);
                    if (blank is { } b) ConvertBlank<TIn, TOut>(values, output, b, scaled, bscale, bzero);
                    else if (scaled) for (int i = 0; i < n; i++) output[i] = TOut.CreateTruncating(bzero + bscale * double.CreateTruncating(values[i]));
                    else for (int i = 0; i < n; i++) output[i] = TOut.CreateTruncating(double.CreateTruncating(values[i]));
                }
            }, cancellationToken);
        }
        finally
        {
            ArrayPool<TIn>.Shared.Return(buffer);
        }
    }

    /// <remarks>Returns the read to run: synchronously (<see cref="ChunkedRead{TResult}.Run"/>) or asynchronously.</remarks>
    public static ChunkedRead<TOut[]> ToFloating<TOut>(DataSource source, BitPix bitPix, long? blank, double bscale, double bzero)
        where TOut : unmanaged, IFloatingPointIeee754<TOut>
    {
        var result = GC.AllocateUninitializedArray<TOut>(source.ElementCount(bitPix.BytesPerElement()));
        return bitPix switch
        {
            BitPix.Byte => Convert<byte, TOut>(result, blank, bscale, bzero),
            BitPix.Int16 => Convert<short, TOut>(result, blank, bscale, bzero),
            BitPix.Int32 => Convert<int, TOut>(result, blank, bscale, bzero),
            BitPix.Int64 => Convert<long, TOut>(result, blank, bscale, bzero),
            BitPix.Single => Convert<float, TOut>(result, null, bscale, bzero),
            _ => Convert<double, TOut>(result, null, bscale, bzero),
        };
    }

    private static ChunkedRead<TOut[]> Convert<TIn, TOut>(TOut[] result, long? blank, double bscale, double bzero)
        where TIn : unmanaged, INumberBase<TIn>
        where TOut : unmanaged, IFloatingPointIeee754<TOut>
    {
        int size = Unsafe.SizeOf<TIn>();
        bool scaled = bscale != 1 || bzero != 0;
        TIn[] buffer = ArrayPool<TIn>.Shared.Rent(Math.Min(result.Length, ConversionChunk));
        return new ChunkedRead<TOut[]>(result, size,
            (bytes, first) => ConvertChunk(bytes, result.AsSpan(first, bytes.Length / size), buffer, blank, scaled, bscale, bzero),
            () => ArrayPool<TIn>.Shared.Return(buffer));
    }

    /// <summary>Converts one chunk of big-endian values, <see cref="ConversionChunk"/> values at a time through <paramref name="buffer"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ConvertChunk<TIn, TOut>(ReadOnlySpan<byte> bytes, Span<TOut> result, TIn[] buffer, long? blank, bool scaled, double bscale, double bzero)
        where TIn : unmanaged, INumberBase<TIn>
        where TOut : unmanaged, IFloatingPointIeee754<TOut>
    {
        int size = Unsafe.SizeOf<TIn>();
        for (int done = 0; done < result.Length; done += ConversionChunk)
        {
            int n = Math.Min(ConversionChunk, result.Length - done);
            Span<TIn> values = buffer.AsSpan(0, n);
            BigEndian.Read(bytes.Slice(done * size, n * size), values);
            Span<TOut> output = result.Slice(done, n);
            if (blank is { } b) ConvertBlank<TIn, TOut>(values, output, b, scaled, bscale, bzero);
            else if (scaled) for (int i = 0; i < n; i++) output[i] = TOut.CreateTruncating(bzero + bscale * double.CreateTruncating(values[i]));
            else for (int i = 0; i < n; i++) output[i] = TOut.CreateTruncating(double.CreateTruncating(values[i]));
        }
    }

    private static void ConvertBlank<TIn, TOut>(ReadOnlySpan<TIn> values, Span<TOut> output, long blank, bool scaled, double bscale, double bzero)
        where TIn : unmanaged, INumberBase<TIn>
        where TOut : unmanaged, IFloatingPointIeee754<TOut>
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (long.CreateTruncating(values[i]) == blank) { output[i] = TOut.NaN; continue; }
            double v = double.CreateTruncating(values[i]);
            output[i] = TOut.CreateTruncating(scaled ? bzero + bscale * v : v);
        }
    }

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
