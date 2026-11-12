namespace Eleris.FitsNet.Compression;

/// <summary>The compression algorithms of the tile compression convention (<c>ZCMPTYPE</c>).</summary>
public enum CompressionAlgorithm
{
    /// <summary><c>RICE_1</c>: lossless, fast, best for integer data (8, 16 or 32 bits).</summary>
    Rice,

    /// <summary><c>GZIP_1</c>: gzip of the big-endian pixels (any BITPIX).</summary>
    Gzip1,

    /// <summary><c>GZIP_2</c>: gzip after grouping the bytes of equal significance (usually smaller than GZIP_1).</summary>
    Gzip2,

    /// <summary><c>PLIO_1</c>: IRAF pixel list, for masks (integer values 0 to 2^24).</summary>
    Plio,

    /// <summary><c>HCOMPRESS_1</c>: H-transform; lossy when the scale is not 0.</summary>
    HCompress,

    /// <summary><c>NOCOMPRESS</c>: tiles are stored as they are.</summary>
    NoCompress,
}

/// <summary>Parameters for creating a tile-compressed image.</summary>
public sealed class CompressionOptions
{
    /// <summary>Creates options with the default settings (Rice compression, blocks of 32 pixels, tiles of one row).</summary>
    public CompressionOptions() { }

    /// <summary>The algorithm (default <see cref="CompressionAlgorithm.Rice"/>).</summary>
    public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.Rice;

    /// <summary>
    /// The tile size along each axis (ZTILEn). Default: one row of the image per tile, except for HCOMPRESS where
    /// tiles of 16 rows are used (it needs two dimensions).
    /// </summary>
    public long[]? TileShape { get; set; }

    /// <summary>Rice block size (default 32).</summary>
    public int RiceBlockSize { get; set; } = 32;

    /// <summary>
    /// HCOMPRESS scale: 0 for lossless integer compression, a positive value to divide the transformed coefficients by that
    /// number, a negative value <c>-n</c> for a scale of <c>n</c> times the noise of the tile (floating point data).
    /// </summary>
    public double HCompressScale { get; set; }

    /// <summary>How floating point pixels are quantized (default subtractive dithering, as cfitsio does).</summary>
    public QuantizationMethod Quantization { get; set; } = QuantizationMethod.SubtractiveDither1;

    /// <summary>
    /// Quantization step as a fraction of the measured noise: the step is <c>noise / QuantizeLevel</c> (default 4). A negative
    /// value means an absolute step of <c>-QuantizeLevel</c>.
    /// </summary>
    public double QuantizeLevel { get; set; } = 4.0;

    /// <summary>Maximum number of tiles compressed at the same time: -1 (default) for one per processor core, 1 for sequential, or a positive number.</summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>Seed of the dithering sequence, 1 to 10000 (ZDITHER0).</summary>
    public int DitherSeed { get; set; } = 1;

    /// <summary>
    /// The zlib level of <see cref="CompressionAlgorithm.Gzip1"/> and <see cref="CompressionAlgorithm.Gzip2"/>, 0 (stored) to 9 (smallest):
    /// -1 (default) for the default level of the runtime, which compresses well and takes its time. The files are valid at every level
    /// and are read the same way. From .NET 9 every level can be chosen: level 2 gives files about as large as cfitsio's (whose level is
    /// 6, another implementation of it) in about the same time. On .NET 8 only 0, 1 and 9 differ from the default. The level is
    /// ignored by the other algorithms, and by the compression of tables (<see cref="CompressedTableOptions"/>), which uses the default.
    /// </summary>
    public int GzipLevel { get; set; } = -1;

    internal void Validate()
    {
        if (MaxDegreeOfParallelism == 0 || MaxDegreeOfParallelism < -1)
            throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism), "Use -1 (all cores) or a positive number.");
        if (RiceBlockSize < 1) throw new ArgumentException("The Rice block size must be positive.");
        if (GzipLevel is < -1 or > 9) throw new ArgumentOutOfRangeException(nameof(GzipLevel), "Use -1 (the default) or a level from 0 to 9.");
        if (DitherSeed is < 1 or > Quantizer.RandomCount) throw new ArgumentException("The dither seed must be between 1 and 10000.");
        if (QuantizeLevel == 0 && Quantization != QuantizationMethod.None) throw new ArgumentException("QuantizeLevel must not be 0.");
        if (TileShape is { } t && t.Any(x => x < 1)) throw new ArgumentException("Tile dimensions must be positive.");
    }
}

/// <summary>Parameters for creating a tile-compressed table (<see cref="CompressedTableHdu.Compress"/>).</summary>
public sealed class CompressedTableOptions
{
    /// <summary>Creates options with the default settings (the algorithm chosen for each column, tiles of about 2 MB).</summary>
    public CompressedTableOptions() { }

    /// <summary>
    /// The algorithm of every column, or null to choose per column: Rice for 8, 16 and 32 bit integers, GZIP_1 for logical, bit and
    /// character data, GZIP_2 for the rest. Rice requested here falls back to GZIP_2 for the columns it cannot compress.
    /// </summary>
    public CompressionAlgorithm? Algorithm { get; set; }

    /// <summary>Algorithm of single columns (zero-based index); it wins over <see cref="Algorithm"/>. Rice is refused for non-integer columns.</summary>
    public IDictionary<int, CompressionAlgorithm> ColumnAlgorithms { get; } = new Dictionary<int, CompressionAlgorithm>();

    /// <summary>Rows of the table in each tile; 0 chooses tiles of about 2 MB (or the FZTILELN directive of the header).</summary>
    public long TileRows { get; set; }

    /// <summary>Tiles compressed at the same time (-1: one per processor core).</summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;
}
