using System.Buffers;
using System.Globalization;
using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet;

/// <summary>
/// A tile-compressed image (standard section 10): a binary table with <c>ZIMAGE = T</c> whose rows hold the compressed tiles.
/// It is a <see cref="BinaryTableHdu"/>, so the table API still works, and it adds the image API: <see cref="Decompress(int)"/>,
/// <see cref="ReadPixels{T}()"/> and <see cref="Compress"/>.
/// </summary>
public sealed class CompressedImageHdu : BinaryTableHdu
{
    private const string CompressedData = "COMPRESSED_DATA";
    private const string UncompressedData = "UNCOMPRESSED_DATA";
    private const string GzipData = "GZIP_COMPRESSED_DATA";

    /// <summary>Largest number of pixels in one tile (bounds the memory a forged header can make us allocate).</summary>
    private const double MaxTilePixels = 1 << 27;

    internal CompressedImageHdu(FitsHeader header, DataSource source) : base(header, source) { }

    /// <summary>Whether a binary table header describes a tile-compressed image.</summary>
    /// <param name="header">The header of a binary table extension.</param>
    public static bool IsCompressedImage(FitsHeader header) => header.TryGetValue("ZIMAGE", out bool z) && z;

    // ---- Parameters ----------------------------------------------------------------------------------------------

    private sealed record Params(
        string Algorithm, BitPix BitPix, long[] Axes, long[] Tile, int BlockSize, int BytePix, int HScale, bool HSmooth,
        QuantizationMethod Quant, int DitherSeed);

    private static int ToInt(double value, string name) =>
        value is >= int.MinValue and <= int.MaxValue ? (int)value : throw new FitsException($"The compression parameter {name} is out of range.");

    private Params ReadParams()
    {
        long Require(string key) => Header.TryGetValue(key, out long v) ? v : throw new FitsException($"Compressed image keyword {key} is missing.");

        string algorithm = (Header.Get<string>("ZCMPTYPE") ?? throw new FitsException("ZCMPTYPE is missing.")).Trim().ToUpperInvariant();
        long zbitpix = Require("ZBITPIX");
        if (!BitPixExtensions.IsValid(zbitpix)) throw new FitsException($"Invalid ZBITPIX {zbitpix}.");
        long naxis = Require("ZNAXIS");
        if (naxis is < 0 or > HduLayout.MaxAxes) throw new FitsException($"Invalid ZNAXIS {naxis}.");
        var axes = new long[naxis];
        var tile = new long[naxis];
        for (int i = 0; i < naxis; i++)
        {
            axes[i] = Require("ZNAXIS" + (i + 1));
            tile[i] = Header.TryGetValue("ZTILE" + (i + 1), out long t) ? t : i == 0 ? axes[0] : 1;
            if (axes[i] < 0 || tile[i] < 1) throw new FitsException("Invalid compressed image dimensions.");
        }

        int block = 32, bytePix = 4, hscale = 0;
        bool smooth = false;
        for (int i = 1; Header.Get<string>("ZNAME" + i) is { } name; i++)
        {
            if (!Header.TryGetValue("ZVAL" + i, out double value)) continue; // keep the default of the algorithm
            switch (name.Trim().ToUpperInvariant())
            {
                case "BLOCKSIZE": block = ToInt(value, "BLOCKSIZE"); break;
                case "BYTEPIX": bytePix = ToInt(value, "BYTEPIX"); break;
                case "SCALE": hscale = ToInt(Math.Round(value), "SCALE"); break;
                case "SMOOTH": smooth = value != 0; break;
            }
        }

        // ZQUANTIZ only matters for floating point images; an empty value means the default
        string method = (Header.Get<string>("ZQUANTIZ") ?? "").Trim().ToUpperInvariant();
        var quant = method switch
        {
            "" or "NO_DITHER" => QuantizationMethod.NoDither,
            "NONE" => QuantizationMethod.None,
            "SUBTRACTIVE_DITHER_1" => QuantizationMethod.SubtractiveDither1,
            "SUBTRACTIVE_DITHER_2" => QuantizationMethod.SubtractiveDither2,
            _ when zbitpix > 0 => QuantizationMethod.NoDither,
            _ => throw new FitsException($"The quantization method '{method}' (ZQUANTIZ) is not supported."),
        };
        int seed = Header.TryGetValue("ZDITHER0", out int s) ? s : 1;
        if (quant is QuantizationMethod.SubtractiveDither1 or QuantizationMethod.SubtractiveDither2 && seed is < 1 or > Quantizer.RandomCount)
            throw new FitsException($"ZDITHER0 must be between 1 and {Quantizer.RandomCount}, not {seed}.");
        if (zbitpix == 64 && algorithm is "RICE_1" or "RICE_ONE" or "PLIO_1" or "HCOMPRESS_1")
            throw new FitsException($"{algorithm} cannot compress 64-bit integers.");
        double tilePixels = 1;
        for (int i = 0; i < naxis; i++) tilePixels *= Math.Min(tile[i], Math.Max(axes[i], 1));
        if (tilePixels > MaxTilePixels) throw new FitsException($"A tile of {tilePixels.ToString("N0", CultureInfo.InvariantCulture)} pixels is larger than the supported maximum.");
        return new Params(algorithm, (BitPix)zbitpix, axes, tile, block, bytePix, hscale, smooth, quant, seed);
    }

    /// <summary>BITPIX of the uncompressed image (ZBITPIX).</summary>
    public BitPix BitPix => ReadParams().BitPix;

    /// <summary>The axes of the uncompressed image (ZNAXISn).</summary>
    public IReadOnlyList<long> Axes => ReadParams().Axes;

    /// <summary>The tile size along each axis (ZTILEn).</summary>
    public IReadOnlyList<long> TileShape => ReadParams().Tile;

    /// <summary>The ZCMPTYPE value, e.g. <c>RICE_1</c>.</summary>
    public string Algorithm => ReadParams().Algorithm;

    // ---- Decompression -------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs the independent tiles on several cores. If tiles fail, the error of the lowest tile index is rethrown (unwrapped), so
    /// the outcome does not depend on thread timing. The degree is reduced for very large tiles to bound the memory in flight.
    /// <paramref name="cancellationToken"/> is checked before each tile; a cancelled run stops starting tiles and throws.
    /// </summary>
    internal static void RunTiles(long count, int maxDegreeOfParallelism, double tilePixels, Action<long> body,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.CanBeCanceled)
        {
            var run = body;
            body = t =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                run(t);
            };
        }
        if (maxDegreeOfParallelism == 0 || maxDegreeOfParallelism < -1)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Use -1 (all cores) or a positive number.");
        int degree = maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : maxDegreeOfParallelism;
        const double memoryBudget = 1.5e9, bytesPerPixelInFlight = 40; // typed tile, ints, doubles, codec output
        degree = (int)Math.Max(1, Math.Min(degree, memoryBudget / (Math.Max(1, tilePixels) * bytesPerPixelInFlight)));

        if (count <= 1 || degree == 1)
        {
            for (long t = 0; t < count; t++) body(t);
            return;
        }

        long failedTile = long.MaxValue;
        Exception? failure = null;
        var gate = new object();
        Parallel.For(0L, count, new ParallelOptions { MaxDegreeOfParallelism = degree }, t =>
        {
            if (Volatile.Read(ref failedTile) < t) return; // a lower tile already failed: its error is the one reported
            try
            {
                body(t);
            }
            catch (Exception e)
            {
                lock (gate)
                {
                    if (t < failedTile)
                    {
                        failedTile = t;
                        failure = e;
                    }
                }
            }
        });
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Decompresses every tile into an ordinary image (BSCALE, BZERO, BLANK and other cards are carried over).</summary>
    /// <param name="maxDegreeOfParallelism">Tiles decoded at the same time (default: one per processor core).</param>
    public ImageHdu Decompress(int maxDegreeOfParallelism = -1) => Decompress(maxDegreeOfParallelism, CancellationToken.None);

    /// <summary>
    /// Decompresses every tile into an ordinary image, as <see cref="Decompress(int)"/> does, checking <paramref name="cancellationToken"/>
    /// before each tile.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <param name="maxDegreeOfParallelism">Tiles decoded at the same time: -1 for one per processor core.</param>
    /// <param name="cancellationToken">The token that cancels the decompression.</param>
    public ImageHdu Decompress(int maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxDegreeOfParallelism == 0 || maxDegreeOfParallelism < -1)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Use -1 (all cores) or a positive number.");
        var p = ReadParams();
        var grid = new TileGrid(p.Axes, p.Tile);
        if (RowCount != grid.TileCount)
            throw new FitsException($"The table has {RowCount} rows but the tiling needs {grid.TileCount}.");
        long pixels = grid.PixelCount;
        int elementSize = p.BitPix.BytesPerElement();
        if (pixels > Array.MaxLength || pixels * elementSize > Array.MaxLength)
            throw new FitsException("The decompressed image is larger than 2 GB.");

        var columns = ReadColumns(); // one pass over the data for all the columns
        cancellationToken.ThrowIfCancellationRequested();
        Array? Data(string name) => columns.FirstOrDefault(c => string.Equals(c.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase))?.Data;
        double[]? Doubles(string name)
        {
            var c = columns.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            return c?.Data is { } d ? NumericConvert.ToDoubles(d, c.BinaryFormat.Code, null) : null;
        }
        // The columns of a compressed image have a fixed shape; anything else is a damaged or forged file.
        void RequireVariableBytes(string name, string elements)
        {
            var c = columns.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (c is null) return;
            var f = c.BinaryFormat;
            if (!f.IsVariable || !elements.Contains(f.ElementCode))
                throw new FitsException($"Column {name} of a compressed image must be a variable-length array of element type {string.Join(" or ", elements.ToCharArray())}, not '{c.Format}'.");
        }

        void RequireNumberPerTile(string name)
        {
            var c = columns.FirstOrDefault(x => string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (c is null) return;
            var f = c.BinaryFormat;
            if (f.IsVariable || f.Repeat != 1 || f.Code is not ('B' or 'I' or 'J' or 'K' or 'E' or 'D'))
                throw new FitsException($"Column {name} of a compressed image must hold one number per tile, not '{c.Format}'.");
        }

        RequireVariableBytes(CompressedData, "BIJ");
        RequireVariableBytes(UncompressedData, "BIJKED"); // stored with the type of the image
        RequireVariableBytes(GzipData, "B");
        RequireNumberPerTile("ZSCALE");
        RequireNumberPerTile("ZZERO");
        RequireNumberPerTile("ZBLANK");
        Array? comp = Data(CompressedData), raw = Data(UncompressedData), gz = Data(GzipData);
        double[]? scales = Doubles("ZSCALE"), zeros = Doubles("ZZERO"), blanks = Doubles("ZBLANK");
        double hScale = Header.TryGetValue("ZSCALE", out double hs) ? hs : 1.0;
        double hZero = Header.TryGetValue("ZZERO", out double hz) ? hz : 0.0;
        int? hBlank = Header.TryGetValue("ZBLANK", out int hb) ? hb : null;
        bool quantized = p.BitPix.IsFloatingPoint() && p.Quant != QuantizationMethod.None
                         && (scales is not null || Header.Contains("ZSCALE"));

        byte[] image;
        try
        {
        // Every tile writes its own pixels, big-endian, straight into the bytes of the result: no array of the whole image to swap afterwards.
        image = GC.AllocateUninitializedArray<byte>((int)(pixels * elementSize));
        RunTiles(grid.TileCount, maxDegreeOfParallelism, grid.MaxTilePixels, t =>
        {
            grid.GetTile(t, out var origin, out var extent);
            int n = TileGrid.CountInt(extent);
            byte[] cell = CellBytes(comp, (int)t);
            if (cell.Length == 0 && raw is not null && CellBytes(raw, (int)t) is { Length: > 0 } uncompressed)
            {
                WriteBytes(grid, image, uncompressed, elementSize, n, origin, extent);
            }
            else if (p.BitPix.IsFloatingPoint() && cell.Length == 0 && gz is not null && CellBytes(gz, (int)t) is { Length: > 0 } gzCell)
            {
                WriteBytes(grid, image, GzipCodec.Decompress(gzCell, (long)n * elementSize), elementSize, n, origin, extent);
            }
            else if (quantized)
            {
                int[] ints = DecodeIntsPooled(p, cell, comp, (int)t, n, extent, 4, out bool pooled);
                try
                {
                    double scale = scales is not null ? scales[t] : hScale;
                    double zero = zeros is not null ? zeros[t] : hZero;
                    int? blank = blanks is not null ? (int)blanks[t] : hBlank; // null checking only when ZBLANK is defined
                    if (p.BitPix == BitPix.Double)
                    {
                        double[] output = ArrayPool<double>.Shared.Rent(n);
                        try
                        {
                            Quantizer.Dequantize(ints.AsSpan(0, n), output.AsSpan(0, n), scale, zero, blank, p.Quant, (int)t, p.DitherSeed);
                            grid.WriteTile<double>(image, output.AsSpan(0, n), origin, extent);
                        }
                        finally
                        {
                            ArrayPool<double>.Shared.Return(output);
                        }
                    }
                    else
                    {
                        float[] output = ArrayPool<float>.Shared.Rent(n);
                        try
                        {
                            Quantizer.DequantizeToSingle(ints.AsSpan(0, n), output.AsSpan(0, n), scale, zero, blank, p.Quant, (int)t, p.DitherSeed);
                            grid.WriteTile<float>(image, output.AsSpan(0, n), origin, extent);
                        }
                        finally
                        {
                            ArrayPool<float>.Shared.Return(output);
                        }
                    }
                }
                finally
                {
                    if (pooled) ArrayPool<int>.Shared.Return(ints);
                }
            }
            else if (p.BitPix.IsFloatingPoint() || p.Algorithm is "GZIP_1" or "GZIP_2" or "NOCOMPRESS" && p.BitPix == BitPix.Int64)
            {
                WriteBytes(grid, image, RawBytes(p, cell, n, elementSize), elementSize, n, origin, extent);
            }
            else
            {
                int[] ints = DecodeIntsPooled(p, cell, comp, (int)t, n, extent, elementSize, out bool pooled);
                try
                {
                    WriteInts(grid, image, ints.AsSpan(0, n), p.BitPix, origin, extent);
                }
                finally
                {
                    if (pooled) ArrayPool<int>.Shared.Return(ints);
                }
            }
        }, cancellationToken);
        }
        catch (OutOfMemoryException e)
        {
            throw new FitsException("There is not enough memory to decompress this image (or its header announces an implausible size).", e);
        }

        // The decompressed header: every card that describes the image rather than the table that stores it.
        var header = new FitsHeader();
        header.ReplaceAll(Header.Where(c => c.Kind != FitsCardKind.Value || KeepOnDecompress(c.Keyword)).ToList());
        RebuildMandatory(header, true, null, p.BitPix, p.Axes, 0, 1, false, false);
        if (header.Get<string>("EXTNAME") == "COMPRESSED_IMAGE") header.Remove("EXTNAME"); // the default name given on compression
        return new ImageHdu(header, new MemoryDataSource(image));
    }

    private static readonly string[] CompressionKeys =
    [
        "ZIMAGE", "ZTENSION", "ZSIMPLE", "ZEXTEND", "ZBITPIX", "ZPCOUNT", "ZGCOUNT", "ZCMPTYPE", "ZQUANTIZ", "ZDITHER0",
        "ZMASKCMP", "ZBLANK", "ZSCALE", "ZZERO", "ZNAXIS", "ZHECKSUM", "ZDATASUM", "CHECKSUM", "DATASUM", "TFIELDS", "SIMPLE",
        "XTENSION", "BITPIX", "NAXIS", "PCOUNT", "GCOUNT", "EXTEND", "GROUPS",
    ];

    internal static bool IsIndexed(string key, string prefix) =>
        key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal) && key.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;

    private static bool KeepOnDecompress(string key) =>
        !CompressionKeys.Contains(key) && !TableColumn.IsColumnKeyword(key)
        && !IsIndexed(key, "NAXIS") && !IsIndexed(key, "ZNAXIS") && !IsIndexed(key, "ZTILE")
        && !IsIndexed(key, "ZNAME") && !IsIndexed(key, "ZVAL");

    /// <summary>Raw bytes of a lossless GZIP/NOCOMPRESS tile.</summary>
    private static byte[] RawBytes(Params p, byte[] cell, int n, int elementSize) => p.Algorithm switch
    {
        "GZIP_1" => GzipCodec.Decompress(cell, (long)n * elementSize),
        "GZIP_2" => GzipCodec.Unshuffle(GzipCodec.Decompress(cell, (long)n * elementSize), elementSize),
        "NOCOMPRESS" => cell,
        _ => throw new FitsException($"Floating point data compressed with {p.Algorithm} must be quantized (ZSCALE is missing)."),
    };

    /// <summary>
    /// Decodes a tile into 32-bit integers. A Rice tile goes into an array rented from the pool (longer than the tile, to be given
    /// back when <paramref name="pooled"/> is true): the tiles of a big image would otherwise allocate one array each.
    /// </summary>
    private static int[] DecodeIntsPooled(Params p, byte[] cell, Array? column, int row, int n, long[] extent, int elementSize, out bool pooled)
    {
        pooled = false;
        if (p.Algorithm is not ("RICE_1" or "RICE_ONE")) return DecodeInts(p, cell, column, row, n, extent, elementSize);
        CheckRiceCell(p, cell, n);
        int[] ints = ArrayPool<int>.Shared.Rent(n);
        try
        {
            RiceCodec.Decode(cell, ints.AsSpan(0, n), p.BlockSize, p.BytePix);
        }
        catch
        {
            ArrayPool<int>.Shared.Return(ints);
            throw;
        }
        pooled = true;
        return ints;
    }

    // Every block needs at least its FS code: a tiny cell cannot describe a big tile.
    private static void CheckRiceCell(Params p, byte[] cell, int n)
    {
        int fsbits = p.BytePix switch { 1 => 3, 2 => 4, _ => 5 };
        if (p.BlockSize >= 1 && cell.Length < p.BytePix + (long)(n / p.BlockSize) * fsbits / 8)
            throw new FitsException("The Rice compressed data is too short for the announced tile size.");
    }

    /// <summary>Converts decoded integers to the type of the image and writes them into the result.</summary>
    private static void WriteInts(TileGrid grid, byte[] image, ReadOnlySpan<int> ints, BitPix bp, long[] origin, long[] extent)
    {
        switch (bp)
        {
            case BitPix.Int32:
                grid.WriteTile(image, ints, origin, extent);
                break;
            case BitPix.Byte:
            {
                byte[] r = ArrayPool<byte>.Shared.Rent(ints.Length);
                try
                {
                    for (int i = 0; i < ints.Length; i++) r[i] = (byte)Math.Clamp(ints[i], 0, 255); // lossy data can overshoot the range
                    grid.WriteTile<byte>(image, r.AsSpan(0, ints.Length), origin, extent);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(r);
                }
                break;
            }
            case BitPix.Int16:
            {
                short[] r = ArrayPool<short>.Shared.Rent(ints.Length);
                try
                {
                    for (int i = 0; i < ints.Length; i++) r[i] = (short)Math.Clamp(ints[i], short.MinValue, short.MaxValue);
                    grid.WriteTile<short>(image, r.AsSpan(0, ints.Length), origin, extent);
                }
                finally
                {
                    ArrayPool<short>.Shared.Return(r);
                }
                break;
            }
            case BitPix.Int64:
            {
                long[] r = ArrayPool<long>.Shared.Rent(ints.Length);
                try
                {
                    for (int i = 0; i < ints.Length; i++) r[i] = ints[i];
                    grid.WriteTile<long>(image, r.AsSpan(0, ints.Length), origin, extent);
                }
                finally
                {
                    ArrayPool<long>.Shared.Return(r);
                }
                break;
            }
            default:
                throw new FitsException("Integer tiles cannot describe a floating point image without quantization.");
        }
    }

    /// <summary>Decodes a tile into 32-bit integers with the algorithm of the table.</summary>
    private static int[] DecodeInts(Params p, byte[] cell, Array? column, int row, int n, long[] extent, int elementSize)
    {
        switch (p.Algorithm)
        {
            case "RICE_1" or "RICE_ONE":
            {
                CheckRiceCell(p, cell, n);
                return RiceCodec.Decode(cell, n, p.BlockSize, p.BytePix);
            }
            case "GZIP_1" or "GZIP_2" or "NOCOMPRESS":
            {
                byte[] bytes = RawBytes(p, cell, n, elementSize);
                if (bytes.Length != (long)n * elementSize) throw new FitsException("A tile holds the wrong number of bytes.");
                var ints = new int[n];
                for (int i = 0; i < n; i++)
                    ints[i] = elementSize switch
                    {
                        1 => bytes[i],
                        2 => (short)((bytes[2 * i] << 8) | bytes[2 * i + 1]),
                        4 => (bytes[4 * i] << 24) | (bytes[4 * i + 1] << 16) | (bytes[4 * i + 2] << 8) | bytes[4 * i + 3],
                        _ => throw new FitsException("Unsupported integer width for this algorithm."),
                    };
                return ints;
            }
            case "PLIO_1":
                return PlioCodec.Decode(CellShorts(column, row), n);
            case "HCOMPRESS_1":
            {
                long ny = 1;
                for (int i = 1; i < extent.Length; i++) ny *= extent[i];
                return HcompressCodec.Decode(cell, (int)extent[0], (int)ny, p.HSmooth);
            }
            default:
                throw new FitsException($"Unknown compression algorithm '{p.Algorithm}'.");
        }
    }

    // ---- Cell and array helpers ----------------------------------------------------------------------------------

    private static byte[] CellBytes(Array? column, int row)
    {
        if (column is null) return [];
        return column.GetValue(row) switch
        {
            byte[] b => b,
            short[] s => BigEndian.FromArray<short>(s),
            int[] i => BigEndian.FromArray<int>(i),
            long[] l => BigEndian.FromArray<long>(l),
            float[] f => BigEndian.FromArray<float>(f),
            double[] d => BigEndian.FromArray<double>(d),
            _ => [],
        };
    }

    private static short[] CellShorts(Array? column, int row)
    {
        if (column is null) return [];
        return column.GetValue(row) switch
        {
            short[] s => s,
            byte[] b => BigEndian.ToArray<short>(b),
            int[] i => i.Select(x => (short)x).ToArray(),
            _ => [],
        };
    }

    /// <summary>Writes the big-endian bytes of a tile into the result as they are.</summary>
    private static void WriteBytes(TileGrid grid, byte[] image, byte[] tile, int elementSize, int n, long[] origin, long[] extent)
    {
        if (tile.Length != (long)n * elementSize) throw new FitsException("A tile holds the wrong number of bytes.");
        grid.WriteTileBytes(image, tile, elementSize, origin, extent);
    }

    // ---- Table editing is not offered: the Z* keywords would no longer describe the table ------------------------------

    private const string EditMessage = "A compressed image cannot be edited as a table: call Decompress(), edit the image and Compress() it again.";

    /// <summary>Always throws <see cref="NotSupportedException"/>: the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.</summary>
    /// <param name="columns">The columns that would replace the table.</param>
    public override void SetColumns(IReadOnlyList<TableColumn> columns) => throw new NotSupportedException(EditMessage);

    /// <summary>Always throws <see cref="NotSupportedException"/>: the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.</summary>
    /// <param name="column">The column to add; its data must have one entry for each row of the table.</param>
    public override void AddColumn(TableColumn column) => throw new NotSupportedException(EditMessage);

    /// <summary>Always throws <see cref="NotSupportedException"/>: the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.</summary>
    /// <param name="index">The zero-based index of the column.</param>
    public override void RemoveColumn(int index) => throw new NotSupportedException(EditMessage);

    /// <summary>Always throws <see cref="NotSupportedException"/>: the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.</summary>
    /// <param name="start">The zero-based index of the first row to remove.</param>
    /// <param name="count">The number of rows to remove.</param>
    public override void RemoveRows(long start, long count) => throw new NotSupportedException(EditMessage);

    /// <summary>Always throws <see cref="NotSupportedException"/>: the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.</summary>
    /// <param name="rows">One column of data for each column of the table, in the same order, holding the rows to add.</param>
    public override void AppendRows(IReadOnlyList<TableColumn> rows) => throw new NotSupportedException(EditMessage);

    /// <summary>Not available: use <see cref="Create{T}"/> or <see cref="Compress"/> to build a compressed image.</summary>
    /// <param name="columns">The columns, each with its data; they must all have the same number of rows.</param>
    public static new BinaryTableHdu Create(IReadOnlyList<TableColumn> columns) => throw new NotSupportedException(
        "Use CompressedImageHdu.Create<T>(pixels, axes, options) or ImageHdu.Compress() to build a compressed image.");

    // ---- Pixel access (through the decompressed image) --------------------------------------------------------------

    /// <summary>Decompresses and reads the stored pixel values, like <see cref="ImageHdu.ReadRawPixels{T}()"/>.</summary>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    public T[] ReadRawPixels<T>() where T : unmanaged => Decompress().ReadRawPixels<T>();

    /// <summary>
    /// Decompresses and reads the stored pixel values, as <see cref="ReadRawPixels{T}()"/> does, checking
    /// <paramref name="cancellationToken"/> before each tile.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    public T[] ReadRawPixels<T>(CancellationToken cancellationToken) where T : unmanaged =>
        Decompress(-1, cancellationToken).ReadRawPixels<T>(cancellationToken);

    /// <summary>Decompresses and reads physical pixel values, like <see cref="ImageHdu.ReadPixels{T}()"/>.</summary>
    /// <typeparam name="T">The type the physical values are returned as, for example <c>float</c> or <c>double</c>.</typeparam>
    public T[] ReadPixels<T>() where T : unmanaged => Decompress().ReadPixels<T>();

    /// <summary>
    /// Decompresses and reads physical pixel values, as <see cref="ReadPixels{T}()"/> does, checking <paramref name="cancellationToken"/>
    /// before each tile.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the physical values are returned as, for example <c>float</c> or <c>double</c>.</typeparam>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    public T[] ReadPixels<T>(CancellationToken cancellationToken) where T : unmanaged =>
        Decompress(-1, cancellationToken).ReadPixels<T>(cancellationToken);

    /// <summary>
    /// Asynchronous version of <see cref="ReadPixels{T}()"/>: the compressed data of a lazily opened file is read with asynchronous I/O, then
    /// the tiles are decompressed on all the cores as <see cref="ReadPixels{T}(CancellationToken)"/> does, checking
    /// <paramref name="cancellationToken"/> between blocks of the read and before each tile.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the physical values are returned as, for example <c>float</c> or <c>double</c>.</typeparam>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    public async Task<T[]> ReadPixelsAsync<T>(CancellationToken cancellationToken = default) where T : unmanaged =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).ReadPixels<T>(cancellationToken);

    /// <summary>
    /// Asynchronous version of <see cref="ReadRawPixels{T}()"/>: the compressed data of a lazily opened file is read with asynchronous I/O,
    /// then decompressed as <see cref="ReadRawPixels{T}(CancellationToken)"/> does.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    public async Task<T[]> ReadRawPixelsAsync<T>(CancellationToken cancellationToken = default) where T : unmanaged =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).ReadRawPixels<T>(cancellationToken);

    /// <summary>
    /// This image with its compressed data in memory, read asynchronously from a lazily opened file (the synchronous decompression
    /// loads it too). Data already in memory is used as it is.
    /// </summary>
    private async Task<CompressedImageHdu> LoadAsync(CancellationToken cancellationToken)
    {
        if (Source.IsInMemory) return this;
        byte[] data = await Source.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return new CompressedImageHdu(Header, new MemoryDataSource(data));
    }

    // ---- Compression ---------------------------------------------------------------------------------------------

    /// <summary>Compresses pixels into a tile-compressed image (supported types: those of <see cref="ImageHdu.Create{T}"/>).</summary>
    /// <typeparam name="T">The pixel type: <c>byte</c>, <c>short</c>, <c>int</c>, <c>long</c>, <c>float</c> or <c>double</c>, or an unsigned type.</typeparam>
    /// <param name="pixels">The pixels, the first axis varying fastest.</param>
    /// <param name="axes">The length of each axis.</param>
    /// <param name="options">The compression options, or null for Rice compression with default settings.</param>
    public static CompressedImageHdu Create<T>(ReadOnlySpan<T> pixels, long[] axes, CompressionOptions? options = null) where T : unmanaged =>
        Compress(ImageHdu.Create(pixels, axes), options);

    /// <summary>Compresses an image. The image is left untouched; its header cards (except structural ones) are copied.</summary>
    /// <param name="image">The image to compress; it is not modified.</param>
    /// <param name="options">The compression options, or null for Rice compression with default settings.</param>
    public static CompressedImageHdu Compress(ImageHdu image, CompressionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new CompressionOptions();
        options.Validate();

        BitPix bp = image.BitPix;
        long[] axes = image.Axes.ToArray();
        if (axes.Length == 0 || image.PixelCount == 0) throw new FitsException("An image without pixels cannot be compressed.");
        bool floating = bp.IsFloatingPoint();
        var algo = options.Algorithm;
        if (!floating && bp == BitPix.Int64 && algo is CompressionAlgorithm.Rice or CompressionAlgorithm.Plio or CompressionAlgorithm.HCompress)
            throw new NotSupportedException("64-bit integer images can only use GZIP_1, GZIP_2 or NOCOMPRESS.");
        if (algo == CompressionAlgorithm.Plio && floating) throw new NotSupportedException("PLIO_1 is for integer masks.");
        bool quantize = floating && options.Quantization != QuantizationMethod.None;
        if (floating && !quantize && algo is CompressionAlgorithm.Rice or CompressionAlgorithm.Plio or CompressionAlgorithm.HCompress)
            throw new InvalidOperationException($"{algo} needs the floating point pixels to be quantized; choose a quantization method or GZIP.");

        long[] tileShape = options.TileShape ?? DefaultTile(axes, algo);
        if (tileShape.Length != axes.Length) throw new ArgumentException("TileShape needs one entry per axis.", nameof(options));
        var grid = new TileGrid(axes, tileShape);
        byte[] pixels = image.Source.ReadAll();   // big-endian, as stored: each tile converts its own part
        if (pixels.Length != grid.PixelCount * bp.BytesPerElement()) throw new FitsException("A tile holds the wrong number of bytes.");

        int bytePix = quantize ? 4 : Math.Min(4, bp.BytesPerElement());
        var rows = new byte[grid.TileCount][];
        var plioRows = new short[grid.TileCount][];
        var gzipRows = new byte[grid.TileCount][];
        var scales = new double[grid.TileCount];
        var zeros = new double[grid.TileCount];
        int anyFallbackFlag = 0, anyNaNFlag = 0;

        if (algo == CompressionAlgorithm.HCompress)
        {
            for (long tile = 0; tile < grid.TileCount; tile++)
            {
                grid.GetTile(tile, out _, out var shape);
                long rest = 1;
                for (int d = 1; d < shape.Length; d++) rest *= shape[d];
                if (shape[0] < 2 || rest < 2)
                    throw new ArgumentException(
                        $"HCOMPRESS needs tiles of at least 2 x 2 pixels but tile {tile} is {string.Join(" x ", shape)}; change TileShape (a final row or column of one pixel is not allowed).",
                        nameof(options));
            }
        }

        RunTiles(grid.TileCount, options.MaxDegreeOfParallelism, grid.MaxTilePixels, t =>
        {
            grid.GetTile(t, out var origin, out var extent);
            int n = TileGrid.CountInt(extent);
            rows[t] = [];
            plioRows[t] = [];
            gzipRows[t] = [];
            int elementSize = bp.BytesPerElement();

            // The algorithms that compress bytes take the big-endian bytes of the tile as they are in the image.
            byte[] TileBytes()
            {
                var bytes = new byte[n * elementSize];
                grid.ReadTileBytes(pixels, bytes, elementSize, origin, extent);
                return bytes;
            }

            if (floating)
            {
                if (quantize)
                {
                    double[] values = ArrayPool<double>.Shared.Rent(n);
                    int[] ints = ArrayPool<int>.Shared.Rent(n);
                    try
                    {
                        ReadTileAsDoubles(grid, pixels, bp, values.AsSpan(0, n), origin, extent);
                        if (Quantizer.TryQuantize(values.AsSpan(0, n), ints.AsSpan(0, n), (int)extent[0], options.QuantizeLevel, options.Quantization, (int)t,
                                options.DitherSeed, out double scale, out double zero, out bool nan))
                        {
                            if (nan) Volatile.Write(ref anyNaNFlag, 1);
                            scales[t] = scale;
                            zeros[t] = zero;
                            EncodeInts(options, ints.AsSpan(0, n), extent, bytePix, 4, rows, plioRows, t);
                        }
                        else
                        {
                            Volatile.Write(ref anyFallbackFlag, 1); // not quantizable: store the floats losslessly
                            gzipRows[t] = GzipCodec.Compress(TileBytes());
                        }
                    }
                    finally
                    {
                        ArrayPool<double>.Shared.Return(values);
                        ArrayPool<int>.Shared.Return(ints);
                    }
                }
                else
                {
                    rows[t] = EncodeRaw(algo, TileBytes(), elementSize);
                }
            }
            else if (algo is CompressionAlgorithm.Gzip1 or CompressionAlgorithm.Gzip2 or CompressionAlgorithm.NoCompress)
            {
                rows[t] = EncodeRaw(algo, TileBytes(), elementSize);
            }
            else
            {
                int[] ints = ArrayPool<int>.Shared.Rent(n);
                try
                {
                    ReadTileAsInts(grid, pixels, bp, ints.AsSpan(0, n), origin, extent);
                    EncodeInts(options, ints.AsSpan(0, n), extent, bytePix, elementSize, rows, plioRows, t);
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(ints);
                }
            }
        });
        bool anyFallback = anyFallbackFlag != 0, anyNaN = anyNaNFlag != 0;

        var columns = new List<TableColumn>
        {
            algo == CompressionAlgorithm.Plio
                ? TableColumn.CreateVariable<short>(CompressedData, plioRows)
                : TableColumn.CreateVariable<byte>(CompressedData, rows),
        };
        if (anyFallback) columns.Add(TableColumn.CreateVariable<byte>(GzipData, gzipRows));
        if (quantize)
        {
            columns.Add(TableColumn.Create<double>("ZSCALE", scales));
            columns.Add(TableColumn.Create<double>("ZZERO", zeros));
        }
        var table = BinaryTableHdu.Create(columns);

        var h = table.Header;
        h.Set("ZIMAGE", true, "extension contains a compressed image");
        h.Set("ZTENSION", "IMAGE", "uncompressed extension type");
        h.Set("ZBITPIX", (int)bp, "data type of the original image");
        h.Set("ZNAXIS", axes.Length, "number of axes of the original image");
        for (int i = 0; i < axes.Length; i++) h.Set("ZNAXIS" + (i + 1), axes[i]);
        h.Set("ZPCOUNT", 0);
        h.Set("ZGCOUNT", 1);
        for (int i = 0; i < axes.Length; i++) h.Set("ZTILE" + (i + 1), tileShape[i]);
        h.Set("ZCMPTYPE", AlgorithmName(algo), "compression algorithm");
        int name = 1;
        void Param(string key, object value)
        {
            h.Set("ZNAME" + name, key);
            h.Set("ZVAL" + name, value);
            name++;
        }
        switch (algo)
        {
            case CompressionAlgorithm.Rice:
                Param("BLOCKSIZE", options.RiceBlockSize);
                Param("BYTEPIX", bytePix);
                break;
            case CompressionAlgorithm.HCompress:
                // a negative request means "relative to the noise of each tile": the real scale is then in each stream
                Param("SCALE", options.HCompressScale > 0 ? (long)Math.Round(options.HCompressScale) : 0L);
                Param("SMOOTH", 0);
                break;
        }
        if (floating)
        {
            h.Set("ZQUANTIZ", options.Quantization switch
            {
                QuantizationMethod.None => "NONE",
                QuantizationMethod.NoDither => "NO_DITHER",
                QuantizationMethod.SubtractiveDither1 => "SUBTRACTIVE_DITHER_1",
                _ => "SUBTRACTIVE_DITHER_2",
            }, "quantization method");
            if (quantize && options.Quantization != QuantizationMethod.NoDither) h.Set("ZDITHER0", options.DitherSeed, "dithering seed");
            if (anyNaN) h.Set("ZBLANK", Quantizer.NullValue, "integer value that stands for NaN");
        }

        foreach (var card in image.Header)
        {
            if (card.Kind == FitsCardKind.Value
                && (CompressionKeys.Contains(card.Keyword) || IsIndexed(card.Keyword, "NAXIS") || TableColumn.IsColumnKeyword(card.Keyword) || h.Contains(card.Keyword)))
                continue;
            h.Add(card);
        }

        return new CompressedImageHdu(h, table.Source);
    }

    private static double NoiseOf(ReadOnlySpan<int> ints, int width)
    {
        double[] values = ArrayPool<double>.Shared.Rent(ints.Length);
        try
        {
            for (int i = 0; i < ints.Length; i++) values[i] = ints[i];
            return Quantizer.EstimateNoise(values.AsSpan(0, ints.Length), width);
        }
        finally
        {
            ArrayPool<double>.Shared.Return(values);
        }
    }

    /// <summary>A floating point tile as doubles.</summary>
    private static void ReadTileAsDoubles(TileGrid grid, byte[] pixels, BitPix bp, Span<double> values, long[] origin, long[] extent)
    {
        if (bp == BitPix.Double)
        {
            grid.ReadTile(pixels, values, origin, extent);
            return;
        }
        float[] floats = ArrayPool<float>.Shared.Rent(values.Length);
        try
        {
            grid.ReadTile<float>(pixels, floats.AsSpan(0, values.Length), origin, extent);
            for (int i = 0; i < values.Length; i++) values[i] = floats[i];
        }
        finally
        {
            ArrayPool<float>.Shared.Return(floats);
        }
    }

    /// <summary>An integer tile as the 32-bit integers the Rice, PLIO and HCOMPRESS encoders take.</summary>
    private static void ReadTileAsInts(TileGrid grid, byte[] pixels, BitPix bp, Span<int> ints, long[] origin, long[] extent)
    {
        switch (bp)
        {
            case BitPix.Int32:
                grid.ReadTile(pixels, ints, origin, extent);
                break;
            case BitPix.Byte:
            {
                byte[] bytes = ArrayPool<byte>.Shared.Rent(ints.Length);
                try
                {
                    grid.ReadTile<byte>(pixels, bytes.AsSpan(0, ints.Length), origin, extent);
                    for (int i = 0; i < ints.Length; i++) ints[i] = bytes[i];
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bytes);
                }
                break;
            }
            case BitPix.Int16:
            {
                short[] shorts = ArrayPool<short>.Shared.Rent(ints.Length);
                try
                {
                    grid.ReadTile<short>(pixels, shorts.AsSpan(0, ints.Length), origin, extent);
                    for (int i = 0; i < ints.Length; i++) ints[i] = shorts[i];
                }
                finally
                {
                    ArrayPool<short>.Shared.Return(shorts);
                }
                break;
            }
            default:
                throw new FitsException("Unsupported pixel array.");
        }
    }

    private static long[] DefaultTile(long[] axes, CompressionAlgorithm algo)
    {
        var tile = new long[axes.Length];
        for (int i = 0; i < tile.Length; i++) tile[i] = 1;
        tile[0] = axes[0];
        if (algo == CompressionAlgorithm.HCompress && axes.Length > 1)
        {
            // About 16 rows per tile, but never leave a final tile one row thin: HCOMPRESS needs at least 2 pixels each way.
            long rows = axes[1], height = Math.Min(rows, 16);
            while (height < rows && rows % height == 1) height++;
            tile[1] = height;
        }
        return tile;
    }

    private static string AlgorithmName(CompressionAlgorithm a) => a switch
    {
        CompressionAlgorithm.Rice => "RICE_1",
        CompressionAlgorithm.Gzip1 => "GZIP_1",
        CompressionAlgorithm.Gzip2 => "GZIP_2",
        CompressionAlgorithm.Plio => "PLIO_1",
        CompressionAlgorithm.HCompress => "HCOMPRESS_1",
        _ => "NOCOMPRESS",
    };

    private static byte[] EncodeRaw(CompressionAlgorithm algo, byte[] bigEndian, int elementSize) => algo switch
    {
        CompressionAlgorithm.Gzip1 => GzipCodec.Compress(bigEndian),
        CompressionAlgorithm.Gzip2 => GzipCodec.Compress(GzipCodec.Shuffle(bigEndian, elementSize)),
        _ => bigEndian,
    };

    /// <summary>Encodes integer tile data with the chosen algorithm into the row arrays.</summary>
    private static void EncodeInts(CompressionOptions o, ReadOnlySpan<int> ints, long[] extent, int bytePix, int elementSize,
        byte[][] rows, short[][] plioRows, long tile)
    {
        switch (o.Algorithm)
        {
            case CompressionAlgorithm.Rice:
                rows[tile] = RiceCodec.Encode(ints, o.RiceBlockSize, bytePix);
                break;
            case CompressionAlgorithm.Plio:
                plioRows[tile] = PlioCodec.Encode(ints);
                break;
            case CompressionAlgorithm.HCompress:
            {
                long ny = 1;
                for (int i = 1; i < extent.Length; i++) ny *= extent[i];
                // A negative scale means "this many times the noise of the tile".
                double requested = o.HCompressScale;
                int scale = requested >= 0
                    ? (int)Math.Round(requested)
                    : Math.Max(1, (int)Math.Round(-requested * NoiseOf(ints, (int)extent[0])));
                rows[tile] = HcompressCodec.Encode(ints, (int)extent[0], (int)ny, scale);
                break;
            }
            default:
            {
                // GZIP / NOCOMPRESS of integers: big-endian values of the element size
                var bytes = new byte[ints.Length * elementSize];
                for (int i = 0; i < ints.Length; i++)
                    for (int b = 0; b < elementSize; b++)
                        bytes[i * elementSize + b] = (byte)(ints[i] >> (8 * (elementSize - 1 - b)));
                rows[tile] = EncodeRaw(o.Algorithm, bytes, elementSize);
                break;
            }
        }
    }
}
