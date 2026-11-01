using System.Buffers.Binary;
using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet;

/// <summary>
/// A tile-compressed binary table (standard section 10.3): a binary table with <c>ZTABLE = T</c> whose rows hold, for each tile of
/// rows of the original table, one compressed byte stream per column. It is a <see cref="BinaryTableHdu"/>, so the table API still
/// describes the stored form; <see cref="Decompress"/> rebuilds the original table and <see cref="Compress"/> builds this kind of
/// table from an ordinary one. Fixed and variable-length columns are supported with <c>RICE_1</c>, <c>GZIP_1</c>, <c>GZIP_2</c>
/// and <c>NOCOMPRESS</c>.
/// </summary>
public sealed class CompressedTableHdu : BinaryTableHdu
{
    private const int RiceBlockSize = 32;

    /// <summary>Tiles are cut so that one holds about this many bytes of the original table when no tile length is requested.</summary>
    private const long AutoTileBytes = 2L << 20;

    internal CompressedTableHdu(FitsHeader header, DataSource source) : base(header, source) { }

    /// <summary>Whether a binary table header describes a tile-compressed table.</summary>
    /// <param name="header">The header of a binary table extension.</param>
    public static bool IsCompressedTable(FitsHeader header) => header.TryGetValue("ZTABLE", out bool z) && z;

    // ---- Parameters ----------------------------------------------------------------------------------------------

    private sealed record ColumnPlan(BinaryFormat Format, string Algorithm, long Offset, long Bytes);

    private sealed record Params(long Rows, long RowWidth, long PCount, long? Heap, long TileRows, ColumnPlan[] Columns);

    private static string? TryNormalizeAlgorithm(string? name)
    {
        string a = (name ?? "").Trim().ToUpperInvariant();
        return a switch
        {
            "RICE_1" or "RICE_ONE" => "RICE_1",
            "GZIP_1" or "GZIP_2" or "NOCOMPRESS" => a,
            _ => null,
        };
    }

    private static string NormalizeAlgorithm(string? name, int column) =>
        name is null
            ? throw new FitsException($"ZCTYP{column} is missing.")
            : TryNormalizeAlgorithm(name) ?? throw new FitsException($"ZCTYP{column} '{name}' is not a table compression algorithm (RICE_1, GZIP_1, GZIP_2 or NOCOMPRESS).");

    /// <summary>A shared allowance of bytes, so that descriptors forged to point many times at the same data cannot multiply the work.</summary>
    private sealed class Budget(long bytes)
    {
        private long _remaining = bytes;

        public void Take(long amount)
        {
            if (Interlocked.Add(ref _remaining, -amount) < 0)
                throw new FitsException("The descriptors of the compressed table ask for far more data than the file can hold.");
        }
    }

    private Params ReadParams()
    {
        long Require(string key) => Header.TryGetValue(key, out long v) ? v : throw new FitsException($"Compressed table keyword {key} is missing.");

        long rows = Require("ZNAXIS2"), width = Require("ZNAXIS1"), tileLen = Require("ZTILELEN");
        long pcount = Header.TryGetValue("ZPCOUNT", out long pc) ? pc : 0;
        long? heap = Header.TryGetValue("ZTHEAP", out long zh) ? zh : null;
        if (rows < 0 || width < 0 || pcount < 0 || tileLen < 1) throw new FitsException("Invalid dimensions of the compressed table.");
        double main = (double)rows * width;
        if (main + pcount > Array.MaxLength) throw new FitsException("The decompressed table is larger than 2 GB.");
        if (heap is { } h && (h < main || h > main + pcount)) throw new FitsException($"ZTHEAP {h} is outside the decompressed table.");

        int count = ColumnCount;
        var plans = new ColumnPlan[count];
        long offset = 0;
        for (int i = 1; i <= count; i++)
        {
            string zform = Header.Get<string>("ZFORM" + i) ?? throw new FitsException($"ZFORM{i} is missing.");
            var format = BinaryFormat.Parse(zform);
            long bytes = format.FieldBytes;
            plans[i - 1] = new ColumnPlan(format, NormalizeAlgorithm(Header.Get<string>("ZCTYP" + i), i), offset, bytes);
            offset += bytes;
            if (offset > width) throw new FitsException("The ZFORM widths of the columns exceed ZNAXIS1.");
        }
        return new Params(rows, width, pcount, heap, tileLen, plans);
    }

    /// <summary>Number of rows of the original table (ZNAXIS2).</summary>
    public long UncompressedRowCount => ReadParams().Rows;

    /// <summary>Width in bytes of a row of the original table (ZNAXIS1).</summary>
    public long UncompressedRowWidth => ReadParams().RowWidth;

    /// <summary>Number of rows of the original table in each tile (ZTILELEN).</summary>
    public long TileRows => ReadParams().TileRows;

    /// <summary>The compression algorithm of each column (ZCTYPn), e.g. <c>RICE_1</c>.</summary>
    public IReadOnlyList<string> ColumnAlgorithms => ReadParams().Columns.Select(c => c.Algorithm).ToList();

    // ---- Column bytes --------------------------------------------------------------------------------------------

    /// <summary>Bytes of one element for the byte shuffling of GZIP_2 (logical, bit, character and complex values are not shuffled).</summary>
    private static int ShuffleSize(char code) => code switch { 'I' => 2, 'J' or 'E' => 4, 'K' or 'D' => 8, _ => 1 };

    private static bool IsInteger(char code) => code is 'B' or 'I' or 'J';

    private static byte[] DecodeBytes(string algorithm, byte[] cell, char code, long elementCount, long byteLength)
    {
        switch (algorithm)
        {
            case "NOCOMPRESS":
                if (cell.Length != byteLength) throw new FitsException("An uncompressed table cell has the wrong length.");
                return cell;
            case "GZIP_1":
                return GzipCodec.Decompress(cell, byteLength);
            case "GZIP_2":
                return GzipCodec.Unshuffle(GzipCodec.Decompress(cell, byteLength), ShuffleSize(code));
            case "RICE_1":
            {
                if (!IsInteger(code)) throw new FitsException($"RICE_1 cannot decompress a column of type {code}.");
                int size = BinaryFormat.ElementSize(code);
                int fsBits = size switch { 1 => 3, 2 => 4, _ => 5 };
                if (cell.Length < size + elementCount / RiceBlockSize * fsBits / 8)
                    throw new FitsException("The Rice compressed data is too short for the announced number of values.");
                int[] ints = RiceCodec.Decode(cell, checked((int)elementCount), RiceBlockSize, size);
                var bytes = new byte[byteLength];
                for (int i = 0; i < ints.Length; i++)
                    for (int b = 0; b < size; b++)
                        bytes[i * size + b] = (byte)(ints[i] >> (8 * (size - 1 - b)));
                return bytes;
            }
            default:
                throw new FitsException($"Unknown compression algorithm '{algorithm}'.");
        }
    }

    private static byte[] EncodeBytes(string algorithm, ReadOnlySpan<byte> raw, char code)
    {
        switch (algorithm)
        {
            case "NOCOMPRESS":
                return raw.ToArray();
            case "GZIP_1":
                return GzipCodec.Compress(raw);
            case "GZIP_2":
                return GzipCodec.Compress(GzipCodec.Shuffle(raw, ShuffleSize(code)));
            default:
            {
                int size = BinaryFormat.ElementSize(code);
                var ints = new int[raw.Length / size];
                for (int i = 0; i < ints.Length; i++)
                {
                    int v = 0;
                    for (int b = 0; b < size; b++) v = (v << 8) | raw[i * size + b];
                    ints[i] = size switch { 1 => v, 2 => (short)v, _ => v };
                }
                return RiceCodec.Encode(ints, RiceBlockSize, size);
            }
        }
    }

    private static (long Count, long Offset) ReadDescriptor(ReadOnlySpan<byte> bytes, bool wide)
    {
        long count = wide ? BinaryPrimitives.ReadInt64BigEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes);
        long offset = wide ? BinaryPrimitives.ReadInt64BigEndian(bytes[8..]) : BinaryPrimitives.ReadInt32BigEndian(bytes[4..]);
        if (count < 0 || offset < 0) throw new FitsException("A heap descriptor has a negative length or offset.");
        return (count, offset);
    }

    private static void WriteDescriptor(Span<byte> destination, long count, long offset)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination, count);
        BinaryPrimitives.WriteInt64BigEndian(destination[8..], offset);
    }

    private static byte[] Slice(ReadOnlyMemory<byte> data, long heapStart, long heapLength, long count, long offset, Budget budget)
    {
        if (offset > heapLength || count > heapLength - offset) throw new FitsException("A descriptor points outside the heap.");
        budget.Take(count);
        return data.Slice((int)(heapStart + offset), (int)count).ToArray();
    }

    // ---- Decompression -------------------------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the original table: tiles are decompressed in parallel, every header card that describes the table rather than its
    /// compressed form is restored (NAXIS1, NAXIS2, PCOUNT, TFORMn, THEAP) and the Z* keywords are removed.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">Tiles decoded at the same time (default: one per processor core).</param>
    public BinaryTableHdu Decompress(int maxDegreeOfParallelism = -1)
    {
        if (maxDegreeOfParallelism == 0 || maxDegreeOfParallelism < -1)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Use -1 (all cores) or a positive number.");
        try
        {
            return DecompressCore(maxDegreeOfParallelism);
        }
        catch (OverflowException e)
        {
            throw new FitsException("A size in the compressed table overflows.", e);
        }
        catch (OutOfMemoryException e)
        {
            throw new FitsException("There is not enough memory to decompress this table (or its header announces an implausible size).", e);
        }
    }

    private BinaryTableHdu DecompressCore(int maxDegreeOfParallelism)
    {
        var p = ReadParams();
        long tiles = p.Rows == 0 ? 0 : (p.Rows - 1) / p.TileRows + 1;
        long storedRowWidth = RowWidth;
        if (RowCount != tiles) throw new FitsException($"The table has {RowCount} rows but {p.Rows} rows in tiles of {p.TileRows} need {tiles}.");

        var compressed = Columns;
        var compOffsets = new long[compressed.Count];
        var compWide = new bool[compressed.Count];
        long compRowWidth = 0;
        for (int i = 0; i < compressed.Count; i++)
        {
            var f = compressed[i].BinaryFormat;
            if (!f.IsVariable || f.ElementCode != 'B' || f.Repeat != 1)
                throw new FitsException($"Column {i + 1} of a compressed table must be a variable-length array of bytes, not '{compressed[i].Format}'.");
            compOffsets[i] = compRowWidth;
            compWide[i] = f.Code == 'Q';
            compRowWidth += f.FieldBytes;
        }

        ReadOnlyMemory<byte> data = Source.ReadMemory();
        long compMain = tiles * storedRowWidth;
        long compHeapStart = Header.TryGetValue("THEAP", out long th) ? th : compMain;
        if (compMain > data.Length || compHeapStart < compMain || compHeapStart > data.Length)
            throw new FitsException("The data of the compressed table is shorter than its header announces.");
        long compHeapLength = data.Length - compHeapStart;
        if (compRowWidth > storedRowWidth) throw new FitsException("The columns of the compressed table exceed NAXIS1.");

        long mainSize = p.Rows * p.RowWidth;
        // Compression cannot expand data more than about 1100 to 1 (deflate: 1032, Rice: 200): a header announcing more is forged and
        // would only make us allocate memory. A megabyte of slack covers the gap that may precede the heap.
        // The gap before the heap and the unused bytes of the rows are not stored: they do not count.
        double unstored = (double)(mainSize - p.Rows * p.Columns.Sum(c => c.Bytes)) + ((p.Heap ?? mainSize) - mainSize);
        if (mainSize + p.PCount - unstored > (double)data.Length * 1100 + (1 << 20))
            throw new FitsException("The announced size of the decompressed table is implausible for the amount of compressed data.");
        long heapStart = p.Heap ?? mainSize;
        long heapRegion = mainSize + p.PCount - heapStart;
        var output = new byte[mainSize + p.PCount];
        var reads = new Budget((long)data.Length * 4 + (1 << 20));
        var decoded = new Budget(2 * (mainSize + p.PCount - (p.Heap ?? mainSize)) + (1 << 20));
        var pending = new List<(long Dest, byte[] Bytes)>?[tiles];

        CompressedImageHdu.RunTiles(tiles, maxDegreeOfParallelism, Math.Min(p.TileRows, p.Rows) * (double)p.RowWidth / 4, t =>
        {
            long first = t * p.TileRows;
            int rows = (int)Math.Min(p.TileRows, p.Rows - first);
            long rowBase = t * storedRowWidth;
            List<(long, byte[])>? writes = null;
            for (int c = 0; c < p.Columns.Length; c++)
            {
                var plan = p.Columns[c];
                if (plan.Bytes == 0) continue;
                var (count, offset) = ReadDescriptor(data.Span.Slice((int)(rowBase + compOffsets[c]), compWide[c] ? 16 : 8), compWide[c]);
                byte[] cell = Slice(data, compHeapStart, compHeapLength, count, offset, reads);
                var f = plan.Format;
                if (!f.IsVariable)
                {
                    byte[] raw = DecodeBytes(plan.Algorithm, cell, f.Code, (long)rows * f.Repeat, (long)rows * plan.Bytes);
                    for (int r = 0; r < rows; r++)
                        Buffer.BlockCopy(raw, (int)(r * plan.Bytes), output, (int)((first + r) * p.RowWidth + plan.Offset), (int)plan.Bytes);
                    continue;
                }

                // Two stages: the combined descriptors (those of the compressed arrays, then the original ones), then each array.
                bool wide = f.Code == 'Q';
                int size = wide ? 16 : 8;
                byte[] combined = GzipCodec.Decompress(cell, (long)rows * (16 + size));
                for (int r = 0; r < rows; r++)
                {
                    var original = combined.AsSpan(rows * 16 + r * size, size);
                    original.CopyTo(output.AsSpan((int)((first + r) * p.RowWidth + plan.Offset), size));
                    var (elements, position) = ReadDescriptor(original, wide);
                    if (elements == 0) continue;
                    long length = BinaryFormat.ByteLength(f.ElementCode, elements);
                    if (position > heapRegion || length > heapRegion - position)
                        throw new FitsException("A variable-length array of the original table lies outside its heap.");
                    var (storedLength, storedOffset) = ReadDescriptor(combined.AsSpan(r * 16, 16), wide: true);
                    byte[] stored = Slice(data, compHeapStart, compHeapLength, storedLength, storedOffset, reads);
                    decoded.Take(length);
                    (writes ??= []).Add((heapStart + position, DecodeBytes(plan.Algorithm, stored, f.ElementCode, elements, length)));
                }
            }
            pending[t] = writes;
        });

        // Arrays that overlap in a damaged file are resolved in tile order, whatever the number of threads.
        foreach (var writes in pending)
            if (writes is not null)
                foreach (var (destination, bytes) in writes)
                    bytes.CopyTo(output, destination);

        var cards = new List<FitsCard>(Header.Count);
        foreach (var card in Header)
        {
            if (card.Kind != FitsCardKind.Value) { cards.Add(card); continue; }
            string key = card.Keyword;
            if (key is "ZTABLE" or "ZNAXIS1" or "ZNAXIS2" or "ZPCOUNT" or "ZTILELEN" or "ZTHEAP" or "ZHECKSUM" or "ZDATASUM"
                or "CHECKSUM" or "DATASUM" or "THEAP"
                || CompressedImageHdu.IsIndexed(key, "ZFORM") || CompressedImageHdu.IsIndexed(key, "ZCTYP"))
                continue;
            if (key == "NAXIS1") cards.Add(card.WithValue(p.RowWidth, card.Comment));
            else if (key == "NAXIS2") cards.Add(card.WithValue(p.Rows, card.Comment));
            else if (key == "PCOUNT") cards.Add(card.WithValue(p.PCount, card.Comment));
            else if (CompressedImageHdu.IsIndexed(key, "TFORM") && int.TryParse(key.AsSpan(5), out int n) && n >= 1 && n <= p.Columns.Length)
                cards.Add(card.WithValue(Header.Get<string>("ZFORM" + n), card.Comment));
            else cards.Add(card);
        }
        if (p.Heap is { } zheap) cards.Add(new FitsCard("THEAP", zheap));
        var header = new FitsHeader();
        header.ReplaceAll(cards);
        return new BinaryTableHdu(header, new MemoryDataSource(output));
    }

    // ---- Table editing is not offered: the Z* keywords would no longer describe the table ------------------------------

    private const string EditMessage = "A compressed table cannot be edited: call Decompress(), edit the table and CompressedTableHdu.Compress() it again.";

    /// <summary>Always throws <see cref="NotSupportedException"/>: the keywords that describe the compressed form would no longer match. Decompress, edit, and compress again.</summary>
    /// <param name="columns">The new columns, each with its data; they must all have the same number of rows.</param>
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

    /// <summary>Not available: use <see cref="Compress"/> to build a compressed table.</summary>
    /// <param name="columns">The columns, each with its data; they must all have the same number of rows.</param>
    public static new BinaryTableHdu Create(IReadOnlyList<TableColumn> columns) => throw new NotSupportedException(
        "Use CompressedTableHdu.Compress(table, options) to build a compressed table.");

    // ---- Compression ---------------------------------------------------------------------------------------------

    private static string AlgorithmName(CompressionAlgorithm a, string what) => a switch
    {
        CompressionAlgorithm.Rice => "RICE_1",
        CompressionAlgorithm.Gzip1 => "GZIP_1",
        CompressionAlgorithm.Gzip2 => "GZIP_2",
        CompressionAlgorithm.NoCompress => "NOCOMPRESS",
        _ => throw new ArgumentException($"{a} cannot compress {what}: tables use Rice, Gzip1, Gzip2 or NoCompress."),
    };

    private sealed class TilePart
    {
        public byte[]? Cell;
        public byte[][]? Arrays;
        public byte[]? Descriptors;
    }

    /// <summary>
    /// Compresses a table. The table is left untouched; its header cards are copied, except the structural ones that now describe the
    /// compressed table (NAXIS1, NAXIS2, PCOUNT, TFORMn, THEAP, CHECKSUM, DATASUM), whose original values go to the Z* keywords.
    /// The compression directives FZTILELN, FZALGOR and FZALGn of the header are obeyed when <paramref name="options"/> says nothing.
    /// </summary>
    /// <exception cref="NotSupportedException">The rows have bytes that belong to no column (they would be lost).</exception>
    /// <param name="table">The table to compress; it is not modified.</param>
    /// <param name="options">The compression options, or null for the automatic choice of algorithm and tile size.</param>
    public static CompressedTableHdu Compress(BinaryTableHdu table, CompressedTableOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table is CompressedTableHdu or CompressedImageHdu) throw new ArgumentException("The table is already compressed.", nameof(table));
        options ??= new CompressedTableOptions();
        try
        {
            return CompressCore(table, options);
        }
        catch (OverflowException e)
        {
            throw new FitsException("A size in the table overflows.", e);
        }
    }

    /// <summary>
    /// The compressed form stores the columns and the arrays the descriptors point at, nothing else: bytes of the gap before the
    /// heap and heap bytes that no descriptor reaches would come back as zeros, so a table with anything but zeros there is refused.
    /// </summary>
    private static void RequireNothingLost(ReadOnlySpan<byte> data, BinaryFormat[] formats, long[] offsets, long rows, long width,
        long mainSize, long heapStart, long heapRegion)
    {
        if (data[(int)mainSize..(int)heapStart].IndexOfAnyExcept((byte)0) >= 0)
            throw new NotSupportedException("The gap between the table and its heap holds data that a compressed table cannot keep.");
        var covered = new bool[heapRegion];
        for (int c = 0; c < formats.Length; c++)
        {
            var f = formats[c];
            if (!f.IsVariable || f.FieldBytes == 0) continue;
            bool wide = f.Code == 'Q';
            for (long r = 0; r < rows; r++)
            {
                var (elements, position) = ReadDescriptor(data.Slice((int)(r * width + offsets[c]), wide ? 16 : 8), wide);
                long length = BinaryFormat.ByteLength(f.ElementCode, elements);
                if (position > heapRegion || length > heapRegion - position)
                    throw new FitsException($"A variable-length array of row {r} lies outside the heap.");
                Array.Fill(covered, true, (int)position, (int)length);
            }
        }
        var heap = data[(int)heapStart..(int)(heapStart + heapRegion)];
        for (int i = 0; i < covered.Length; i++)
            if (!covered[i] && heap[i] != 0)
                throw new NotSupportedException("The heap holds bytes that no variable-length array uses; a compressed table cannot keep them.");
    }

    private static CompressedTableHdu CompressCore(BinaryTableHdu table, CompressedTableOptions options)
    {
        long rows = table.RowCount, width = table.RowWidth;
        var columns = table.Columns;
        int count = columns.Count;
        if (rows == 0 || count == 0) throw new ArgumentException("A table without rows or columns cannot be compressed.", nameof(table));
        var header = table.Header;

        var formats = new BinaryFormat[count];
        var offsets = new long[count];
        long sum = 0;
        for (int i = 0; i < count; i++)
        {
            formats[i] = columns[i].BinaryFormat;
            offsets[i] = sum;
            sum += formats[i].FieldBytes;
        }
        if (sum != width)
            throw new NotSupportedException($"The rows are {width} bytes wide but the columns only describe {sum}: the extra bytes would be lost.");

        long pcount = header.TryGetValue("PCOUNT", out long pc) ? pc : 0;
        long mainSize = rows * width;
        ReadOnlyMemory<byte> data = table.Source.ReadMemory();
        long heapStart = header.TryGetValue("THEAP", out long th) ? th : mainSize;
        if (pcount < 0 || mainSize + pcount > data.Length || heapStart < mainSize || heapStart > mainSize + pcount)
            throw new FitsException("The data of the table is shorter than its header announces.");
        long heapRegion = mainSize + pcount - heapStart;
        RequireNothingLost(data.Span, formats, offsets, rows, width, mainSize, heapStart, heapRegion);

        // Tile length and algorithms: the options, then the directives of the header, then automatic choices.
        long tileRows = options.TileRows;
        if (tileRows < 0) throw new ArgumentOutOfRangeException(nameof(options), "TileRows cannot be negative.");
        if (tileRows == 0 && header.TryGetValue("FZTILELN", out long fz) && fz > 0) tileRows = fz;
        if (tileRows == 0) tileRows = Math.Max(1, AutoTileBytes / Math.Max(1, width));
        tileRows = Math.Min(tileRows, rows);
        long tiles = (rows - 1) / tileRows + 1;

        foreach (int index in options.ColumnAlgorithms.Keys)
            if (index < 0 || index >= count) throw new ArgumentException($"ColumnAlgorithms has an entry for column {index} but the table has {count}.", nameof(options));
        string? defaultName = options.Algorithm is { } global ? AlgorithmName(global, "a table") : null;
        // The directives are advice: one this table cannot follow (PLIO_1, an unknown name) is ignored.
        string? headerDefault = TryNormalizeAlgorithm(header.Get<string>("FZALGOR"));
        string? FromHeader(int column) => TryNormalizeAlgorithm(header.Get<string>("FZALG" + column)) ?? headerDefault;
        var algorithms = new string[count];
        for (int i = 0; i < count; i++)
        {
            char code = formats[i].DataCode;
            string? chosen = null;
            if (options.ColumnAlgorithms.TryGetValue(i, out var perColumn))
            {
                chosen = AlgorithmName(perColumn, $"column {i}");
                if (chosen == "RICE_1" && !IsInteger(code)) throw new ArgumentException($"Rice cannot compress column {i}, which holds {code} values.", nameof(options));
            }
            else if (defaultName is not null || (options.Algorithm is null && FromHeader(i + 1) is not null))
            {
                chosen = defaultName ?? FromHeader(i + 1);
                if (chosen == "RICE_1" && !IsInteger(code)) chosen = "GZIP_2"; // Rice is for integers: the others keep the best general method
            }
            chosen ??= IsInteger(code) ? "RICE_1" : code is 'L' or 'X' or 'A' ? "GZIP_1" : "GZIP_2";
            algorithms[i] = chosen;
        }

        var parts = new TilePart[tiles * count];
        CompressedImageHdu.RunTiles(tiles, options.MaxDegreeOfParallelism, tileRows * (double)width / 4, t =>
        {
            long first = t * tileRows;
            int n = (int)Math.Min(tileRows, rows - first);
            var span = data.Span;
            for (int c = 0; c < count; c++)
            {
                var f = formats[c];
                long bytes = f.FieldBytes;
                var part = new TilePart();
                parts[t * count + c] = part;
                if (bytes == 0) { part.Cell = []; continue; }
                if (!f.IsVariable)
                {
                    var column = new byte[n * bytes];
                    for (int r = 0; r < n; r++)
                        span.Slice((int)((first + r) * width + offsets[c]), (int)bytes).CopyTo(column.AsSpan((int)(r * bytes)));
                    part.Cell = EncodeBytes(algorithms[c], column, f.Code);
                    continue;
                }

                bool wide = f.Code == 'Q';
                int size = wide ? 16 : 8;
                part.Descriptors = new byte[n * size];
                part.Arrays = new byte[n][];
                for (int r = 0; r < n; r++)
                {
                    var descriptor = span.Slice((int)((first + r) * width + offsets[c]), size);
                    descriptor.CopyTo(part.Descriptors.AsSpan(r * size));
                    var (elements, position) = ReadDescriptor(descriptor, wide);
                    long length = BinaryFormat.ByteLength(f.ElementCode, elements);
                    if (position > heapRegion || length > heapRegion - position)
                        throw new FitsException($"A variable-length array of row {first + r} lies outside the heap.");
                    part.Arrays[r] = elements == 0 ? [] : EncodeBytes(algorithms[c], span.Slice((int)(heapStart + position), (int)length), f.ElementCode);
                }
            }
        });

        // Assembly in tile and column order: the heap does not depend on the number of threads.
        int compRowWidth = 16 * count;
        var main = new byte[tiles * compRowWidth];
        var heap = new MemoryStream();
        void Append(byte[] bytes, Span<byte> descriptor)
        {
            if (heap.Length + bytes.Length > Array.MaxLength) throw new FitsException("The compressed table is larger than 2 GB.");
            WriteDescriptor(descriptor, bytes.Length, heap.Length);
            heap.Write(bytes);
        }

        for (long t = 0; t < tiles; t++)
        {
            for (int c = 0; c < count; c++)
            {
                var part = parts[t * count + c];
                var slot = main.AsSpan((int)(t * compRowWidth + c * 16), 16);
                if (part.Arrays is null)
                {
                    Append(part.Cell!, slot);
                    continue;
                }
                int n = part.Arrays.Length;
                var combined = new byte[n * 16 + part.Descriptors!.Length];
                for (int r = 0; r < n; r++) Append(part.Arrays[r], combined.AsSpan(r * 16, 16));
                part.Descriptors.CopyTo(combined, n * 16);
                Append(GzipCodec.Compress(combined), slot); // the descriptors always use GZIP_1
            }
        }

        var h = new FitsHeader();
        RebuildMandatory(h, false, "BINTABLE", BitPix.Byte, [compRowWidth, tiles], heap.Length, 1, false, false);
        h.Set("TFIELDS", count);
        foreach (var card in header)
        {
            if (card.Kind != FitsCardKind.Value) { h.Add(card); continue; }
            string key = card.Keyword;
            if (key is "XTENSION" or "BITPIX" or "NAXIS" or "PCOUNT" or "GCOUNT" or "TFIELDS" or "THEAP" or "CHECKSUM" or "DATASUM"
                or "FZTILELN" or "FZALGOR" or "ZTABLE" || CompressedImageHdu.IsIndexed(key, "NAXIS") || CompressedImageHdu.IsIndexed(key, "FZALG"))
                continue;
            if (CompressedImageHdu.IsIndexed(key, "TFORM")) h.Add(card.WithValue("1QB", card.Comment));
            else h.Add(card);
        }
        h.Set("ZTABLE", true, "this is a compressed table");
        h.Set("ZNAXIS1", width, "width of a row of the original table");
        h.Set("ZNAXIS2", rows, "number of rows of the original table");
        h.Set("ZPCOUNT", pcount, "heap size of the original table");
        h.Set("ZTILELEN", tileRows, "rows in each tile");
        if (header.TryGetValue("THEAP", out long zTheap)) h.Set("ZTHEAP", zTheap, "THEAP of the original table");
        if (header.Get<string>("CHECKSUM") is { } sumText) h.Set("ZHECKSUM", sumText, "CHECKSUM of the original table");
        if (header.Get<string>("DATASUM") is { } dataText) h.Set("ZDATASUM", dataText, "DATASUM of the original table");
        for (int i = 0; i < count; i++)
        {
            h.Set("ZFORM" + (i + 1), columns[i].Format, "format of the original column");
            h.Set("ZCTYP" + (i + 1), algorithms[i], "compression algorithm of the column");
        }

        if ((long)main.Length + heap.Length > Array.MaxLength) throw new FitsException("The compressed table is larger than 2 GB.");
        var all = new byte[main.Length + heap.Length];
        main.CopyTo(all, 0);
        heap.GetBuffer().AsSpan(0, (int)heap.Length).CopyTo(all.AsSpan(main.Length));
        return new CompressedTableHdu(h, new MemoryDataSource(all));
    }
}
