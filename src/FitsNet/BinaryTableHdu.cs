using System.Numerics;

namespace FitsNet;

/// <summary>A binary table extension (<c>XTENSION = 'BINTABLE'</c>), including variable-length arrays stored in the heap.</summary>
public class BinaryTableHdu : TableHdu
{
    /// <summary>Creates a table from columns of equal length.</summary>
    public static BinaryTableHdu Create(IReadOnlyList<TableColumn> columns)
    {
        var hdu = new BinaryTableHdu(new FitsHeader(), DataSource.Empty);
        hdu.SetColumns(columns);
        return hdu;
    }

    internal BinaryTableHdu(FitsHeader header, DataSource source) : base(header, source) { }

    // A3DTABLE, the AIPS prototype of BINTABLE (Appendix F.2), has the same layout without variable-length arrays
    private protected override string XtensionName => Header.Get<string>("XTENSION")?.Trim() == "A3DTABLE" ? "A3DTABLE" : "BINTABLE";

    private protected override bool IsAscii => false;

    private long Theap => Header.TryGetValue("THEAP", out long t) ? t : RowCount * RowWidth;

    private protected override Array DecodeColumn(ReadOnlySpan<byte> data, long rows, TableColumn column, int index, long offset) =>
        BinaryTableCodec.DecodeColumn(data, rows, RowWidth, offset, column.BinaryFormat, column.Dimensions, Theap);

    /// <summary>Tables larger than this are read column by column in blocks instead of being loaded whole (settable for tests).</summary>
    internal static long ChunkedReadThresholdBytes = 32L << 20;

    /// <summary>Size of the blocks read from the file for one column (settable for tests).</summary>
    internal static long ChunkBytes = 8L << 20;

    private protected override Array ReadColumnData(TableColumn column, int index, long offset)
    {
        var f = column.BinaryFormat;
        long rows = RowCount, rowWidth = RowWidth;
        if (f.IsVariable || Source is not StreamDataSource || rows * rowWidth < ChunkedReadThresholdBytes)
            return base.ReadColumnData(column, index, offset);

        // A fixed-length column of a big table: read only what is needed, a block at a time, whatever the table size.
        BinaryTableCodec.CheckLayout(rows, rowWidth, offset, f, Source.Length);
        Array result = BinaryTableCodec.AllocFixed(f, column.Dimensions, rows);
        long blockBytes = Math.Max(1, ChunkBytes);
        if (rowWidth > blockBytes)
        {
            // very wide rows: read the bytes of this column only
            var cell = new byte[checked((int)f.FieldBytes)];
            for (long row = 0; row < rows; row++)
            {
                Source.ReadRange(row * rowWidth + offset, cell);
                BinaryTableCodec.DecodeFixedChunk(cell, 1, cell.Length, 0, f, column.Dimensions, result, row);
            }
            return result;
        }

        long blockRows = Math.Max(1, blockBytes / Math.Max(1, rowWidth));
        var block = new byte[blockRows * rowWidth]; // one buffer reused for every block
        for (long start = 0; start < rows; start += blockRows)
        {
            long count = Math.Min(blockRows, rows - start);
            Source.ReadRange(start * rowWidth, block.AsSpan(0, (int)(count * rowWidth)));
            BinaryTableCodec.DecodeFixedChunk(block, count, rowWidth, offset, f, column.Dimensions, result, start);
        }
        return result;
    }

    private protected override long[] ColumnOffsets(IReadOnlyList<TableColumn> columns)
    {
        var offsets = new long[columns.Count];
        long at = 0;
        bool known = true;
        for (int i = 0; i < offsets.Length; i++)
        {
            offsets[i] = known ? at : -1; // after an unreadable TFORM the positions are unknown, the columns before it stay usable
            if (!known) continue;
            try
            {
                at = checked(at + columns[i].BinaryFormat.FieldBytes);
            }
            catch (Exception e) when (e is OverflowException or FitsException)
            {
                known = false;
            }
        }
        return offsets;
    }

    private protected override (byte[] Data, long Rows, long RowWidth, long HeapSize) EncodeColumns(IReadOnlyList<TableColumn> columns, long? knownRows)
    {
        foreach (var c in columns) RefreshVariableMax(c);
        var (main, heap, rows, rowWidth) = BinaryTableCodec.Encode(columns, knownRows);
        var all = new byte[checked(main.Length + heap.Length)];
        main.CopyTo(all, 0);
        heap.CopyTo(all, main.Length);
        return (all, rows, rowWidth, heap.Length);
    }

    /// <summary>Keeps the <c>(max)</c> part of a variable-length TFORM equal to the longest row actually stored.</summary>
    private static void RefreshVariableMax(TableColumn column)
    {
        var f = column.BinaryFormat;
        if (!f.IsVariable || column.Data is null) return;
        long max = 0;
        if (f.ElementCode == 'A')
            foreach (string s in (string[])column.Data) max = Math.Max(max, s.Length);
        else
            foreach (Array row in column.Data) max = Math.Max(max, row.Length);
        column.Format = $"{f.Repeat}{f.Code}{f.ElementCode}({max})";
    }

    private protected override Type ExpectedDataType(TableColumn column)
    {
        var f = column.BinaryFormat;
        if (f.IsVariable) return f.ElementCode == 'A' ? typeof(string[]) : BinaryTableCodec.Alloc(f.ElementCode, 0).GetType().MakeArrayType();
        return f.Code == 'A' ? typeof(string[]) : BinaryTableCodec.Alloc(f.Code, 0).GetType();
    }

    private protected override long ItemsPerRow(TableColumn column) => BinaryTableCodec.ItemsPerRow(column.BinaryFormat, column.Dimensions);

    private protected override void ValidateColumns(IReadOnlyList<TableColumn> columns, long rowWidth)
    {
        long sum = 0;
        foreach (var c in columns) sum = checked(sum + c.BinaryFormat.FieldBytes);
        // Spare bytes at the end of a row are legal (cfitsio and astropy accept them); fields exceeding the row are not.
        if (sum > rowWidth)
            throw new FitsException($"The TFORM widths add up to {sum} bytes but NAXIS1 is only {rowWidth}.");
    }

    // ---- Typed access --------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a numeric column (flat, <c>repeat</c> values per row). Integer reads return the stored TNULL sentinel as an
    /// ordinary value; only <c>double</c>/<c>float</c> reads map it to NaN. <c>double</c>/<c>float</c> apply TSCAL/TZERO and turn TNULL into NaN;
    /// integer types work unscaled or with the unsigned conventions (<c>sbyte</c>, <c>ushort</c>, <c>uint</c>, <c>ulong</c>);
    /// <see cref="Complex"/> reads <c>C</c>/<c>M</c> columns.
    /// </summary>
    public T[] ReadColumn<T>(int index) where T : unmanaged
    {
        var column = ColumnAt(index);
        var f = column.BinaryFormat;
        if (f.IsVariable) throw new InvalidOperationException("Use ReadVariableColumn for variable-length columns.");
        if (f.Code is 'L' or 'X' or 'A')
            throw new InvalidOperationException($"Column {index} has type {f.Code}; use ReadLogicalColumn, ReadBitColumn or ReadStringColumn.");
        return NumericConvert.Convert<T>(ReadRaw(index), f.Code, column.Scale, column.Zero, column.Null as long?);
    }

    public T[] ReadColumn<T>(string name) where T : unmanaged => ReadColumn<T>(RequireIndex(name));

    /// <summary>Reads an <c>L</c> column: true, false, or null for undefined.</summary>
    public bool?[] ReadLogicalColumn(int index) => (bool?[])ReadTyped(index, 'L', false);

    /// <summary>Reads an <c>X</c> column, one bool per bit.</summary>
    public bool[] ReadBitColumn(int index) => (bool[])ReadTyped(index, 'X', false);

    /// <summary>Reads an <c>A</c> column (or a variable-length character column): one string per cell/row.</summary>
    public string[] ReadStringColumn(int index) => (string[])ReadTyped(index, 'A', null);

    public string[] ReadStringColumn(string name) => ReadStringColumn(RequireIndex(name));

    /// <summary>Reads a variable-length column (<c>P</c>/<c>Q</c>): one array per row, of the element type's native representation.</summary>
    public T[][] ReadVariableColumn<T>(int index) where T : unmanaged
    {
        var column = ColumnAt(index);
        var f = column.BinaryFormat;
        if (!f.IsVariable) throw new InvalidOperationException("Use ReadColumn for fixed-length columns.");
        if (f.ElementCode is 'L' or 'X' or 'A')
            throw new InvalidOperationException($"Element type {f.ElementCode} is read with ReadVariableLogicalColumn, ReadVariableBitColumn or ReadStringColumn.");
        var rows = (Array)ReadRaw(index);
        var result = new T[rows.Length][];
        for (int r = 0; r < result.Length; r++)
            result[r] = NumericConvert.Convert<T>((Array)rows.GetValue(r)!, f.ElementCode, column.Scale, column.Zero, column.Null as long?);
        return result;
    }

    public T[][] ReadVariableColumn<T>(string name) where T : unmanaged => ReadVariableColumn<T>(RequireIndex(name));

    public bool?[][] ReadVariableLogicalColumn(int index) => (bool?[][])ReadTyped(index, 'L', true);

    public bool[][] ReadVariableBitColumn(int index) => (bool[][])ReadTyped(index, 'X', true);

    private Array ReadTyped(int index, char code, bool? variable)
    {
        var column = ColumnAt(index);
        var f = column.BinaryFormat;
        if (f.DataCode != code || (variable is { } v && v != f.IsVariable))
            throw new InvalidOperationException($"Column {index} has format '{column.Format}', not a {(variable == true ? "variable-length " : "")}{code} column.");
        return ReadRaw(index);
    }
}

/// <summary>Conversion of stored numeric column values (TSCAL, TZERO, TNULL, unsigned conventions).</summary>
internal static class NumericConvert
{
    public static Type NativeType(char code) => code switch
    {
        'B' => typeof(byte),
        'I' => typeof(short),
        'J' => typeof(int),
        'K' => typeof(long),
        'E' => typeof(float),
        'D' => typeof(double),
        'C' or 'M' => typeof(Complex),
        _ => throw new FitsException($"Type {code} is not numeric."),
    };

    public static T[] Convert<T>(Array raw, char code, double? scale, double? zero, long? tnull) where T : unmanaged
    {
        double s = scale ?? 1.0, z = zero ?? 0.0;
        Type t = typeof(T);

        if (code is 'C' or 'M')
        {
            if (t != typeof(Complex)) throw new InvalidOperationException("Complex columns can only be read as Complex.");
            if (s == 1 && z == 0) return (T[])(object)raw;
            // TSCAL / TZERO apply to both the real and the imaginary part, as cfitsio does.
            var source = (Complex[])raw;
            var scaled = new Complex[source.Length];
            for (int i = 0; i < scaled.Length; i++) scaled[i] = new Complex(z + s * source[i].Real, z + s * source[i].Imaginary);
            return (T[])(object)scaled;
        }
        if (t == NativeType(code) && s == 1 && z == 0) return (T[])(object)raw;

        if (t == typeof(double) || t == typeof(float))
        {
            double[] d = ToDoubles(raw, code, tnull);
            if (s != 1 || z != 0)
                for (int i = 0; i < d.Length; i++) d[i] = z + s * d[i];
            if (t == typeof(double)) return (T[])(object)d;
            var f = new float[d.Length];
            for (int i = 0; i < f.Length; i++) f[i] = (float)d[i];
            return (T[])(object)f;
        }

        if (s == 1)
        {
            if (t == typeof(sbyte) && code == 'B' && z == -128)
            {
                var b = (byte[])raw;
                var r = new sbyte[b.Length];
                for (int i = 0; i < r.Length; i++) r[i] = unchecked((sbyte)(b[i] ^ 0x80));
                return (T[])(object)r;
            }
            if (t == typeof(ushort) && code == 'I' && z == 32768)
            {
                var b = (short[])raw;
                var r = new ushort[b.Length];
                for (int i = 0; i < r.Length; i++) r[i] = unchecked((ushort)(b[i] ^ 0x8000));
                return (T[])(object)r;
            }
            if (t == typeof(uint) && code == 'J' && z == 2147483648.0)
            {
                var b = (int[])raw;
                var r = new uint[b.Length];
                for (int i = 0; i < r.Length; i++) r[i] = unchecked((uint)(b[i] ^ int.MinValue));
                return (T[])(object)r;
            }
            if (t == typeof(ulong) && code == 'K' && z == 9223372036854775808.0)
            {
                var b = (long[])raw;
                var r = new ulong[b.Length];
                for (int i = 0; i < r.Length; i++) r[i] = unchecked((ulong)(b[i] ^ long.MinValue));
                return (T[])(object)r;
            }
        }

        throw new InvalidOperationException(
            $"Cannot read a '{code}' column with TSCAL={s}, TZERO={z} as {t.Name}; use double or float.");
    }

    public static double[] ToDoubles(Array raw, char code, long? tnull)
    {
        var result = new double[raw.Length];
        switch (code)
        {
            case 'B': { var a = (byte[])raw; for (int i = 0; i < a.Length; i++) result[i] = tnull == a[i] ? double.NaN : a[i]; break; }
            case 'I': { var a = (short[])raw; for (int i = 0; i < a.Length; i++) result[i] = tnull == a[i] ? double.NaN : a[i]; break; }
            case 'J': { var a = (int[])raw; for (int i = 0; i < a.Length; i++) result[i] = tnull == a[i] ? double.NaN : a[i]; break; }
            case 'K': { var a = (long[])raw; for (int i = 0; i < a.Length; i++) result[i] = tnull == a[i] ? double.NaN : a[i]; break; }
            case 'E': { var a = (float[])raw; for (int i = 0; i < a.Length; i++) result[i] = a[i]; break; }
            case 'D': { var a = (double[])raw; a.CopyTo(result, 0); break; }
            default: throw new InvalidOperationException($"Column type {code} cannot be converted to numbers.");
        }
        return result;
    }
}
