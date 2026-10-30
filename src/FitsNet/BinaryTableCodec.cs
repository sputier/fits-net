using System.Buffers.Binary;
using System.Numerics;

namespace FitsNet;

/// <summary>Encodes and decodes the cells of a binary table (standard section 7.3).</summary>
internal static class BinaryTableCodec
{
    public static Array Alloc(char code, long count) => code switch
    {
        'L' => new bool?[count],
        'X' => new bool[count],
        'B' => new byte[count],
        'I' => new short[count],
        'J' => new int[count],
        'K' => new long[count],
        'E' => new float[count],
        'D' => new double[count],
        'C' or 'M' => new Complex[count],
        _ => throw new FitsException($"Unsupported element type '{code}'."),
    };

    /// <summary>Decodes <paramref name="count"/> elements of <paramref name="code"/> from big-endian bytes into <paramref name="dest"/>.</summary>
    public static void Decode(char code, ReadOnlySpan<byte> src, Array dest, long at, long count)
    {
        int n = checked((int)count), a = checked((int)at);
        switch (code)
        {
            case 'B': src[..n].CopyTo(((byte[])dest).AsSpan(a)); break;
            case 'L':
            {
                var d = (bool?[])dest;
                for (int i = 0; i < n; i++) d[a + i] = src[i] switch { (byte)'T' => true, (byte)'F' => false, _ => null };
                break;
            }
            case 'X':
            {
                var d = (bool[])dest;
                for (int i = 0; i < n; i++) d[a + i] = (src[i >> 3] & (0x80 >> (i & 7))) != 0;
                break;
            }
            case 'I': { var d = (short[])dest; for (int i = 0; i < n; i++) d[a + i] = BinaryPrimitives.ReadInt16BigEndian(src[(i * 2)..]); break; }
            case 'J': { var d = (int[])dest; for (int i = 0; i < n; i++) d[a + i] = BinaryPrimitives.ReadInt32BigEndian(src[(i * 4)..]); break; }
            case 'K': { var d = (long[])dest; for (int i = 0; i < n; i++) d[a + i] = BinaryPrimitives.ReadInt64BigEndian(src[(i * 8)..]); break; }
            case 'E': { var d = (float[])dest; for (int i = 0; i < n; i++) d[a + i] = BinaryPrimitives.ReadSingleBigEndian(src[(i * 4)..]); break; }
            case 'D': { var d = (double[])dest; for (int i = 0; i < n; i++) d[a + i] = BinaryPrimitives.ReadDoubleBigEndian(src[(i * 8)..]); break; }
            case 'C':
            {
                var d = (Complex[])dest;
                for (int i = 0; i < n; i++)
                    d[a + i] = new Complex(BinaryPrimitives.ReadSingleBigEndian(src[(i * 8)..]), BinaryPrimitives.ReadSingleBigEndian(src[(i * 8 + 4)..]));
                break;
            }
            case 'M':
            {
                var d = (Complex[])dest;
                for (int i = 0; i < n; i++)
                    d[a + i] = new Complex(BinaryPrimitives.ReadDoubleBigEndian(src[(i * 16)..]), BinaryPrimitives.ReadDoubleBigEndian(src[(i * 16 + 8)..]));
                break;
            }
            default: throw new FitsException($"Unsupported element type '{code}'.");
        }
    }

    /// <summary>Encodes <paramref name="count"/> elements of <paramref name="src"/> starting at <paramref name="at"/> into <paramref name="dest"/>.</summary>
    public static void Encode(char code, Array src, long at, Span<byte> dest, long count)
    {
        int n = checked((int)count), a = checked((int)at);
        switch (code)
        {
            case 'B': ((byte[])src).AsSpan(a, n).CopyTo(dest); break;
            case 'L':
            {
                var s = (bool?[])src;
                for (int i = 0; i < n; i++) dest[i] = s[a + i] switch { true => (byte)'T', false => (byte)'F', null => 0 };
                break;
            }
            case 'X':
            {
                var s = (bool[])src;
                dest[..(int)FitsBytes('X', n)].Clear();
                for (int i = 0; i < n; i++)
                    if (s[a + i]) dest[i >> 3] |= (byte)(0x80 >> (i & 7));
                break;
            }
            case 'I': { var s = (short[])src; for (int i = 0; i < n; i++) BinaryPrimitives.WriteInt16BigEndian(dest[(i * 2)..], s[a + i]); break; }
            case 'J': { var s = (int[])src; for (int i = 0; i < n; i++) BinaryPrimitives.WriteInt32BigEndian(dest[(i * 4)..], s[a + i]); break; }
            case 'K': { var s = (long[])src; for (int i = 0; i < n; i++) BinaryPrimitives.WriteInt64BigEndian(dest[(i * 8)..], s[a + i]); break; }
            case 'E': { var s = (float[])src; for (int i = 0; i < n; i++) BinaryPrimitives.WriteSingleBigEndian(dest[(i * 4)..], s[a + i]); break; }
            case 'D': { var s = (double[])src; for (int i = 0; i < n; i++) BinaryPrimitives.WriteDoubleBigEndian(dest[(i * 8)..], s[a + i]); break; }
            case 'C':
            {
                var s = (Complex[])src;
                for (int i = 0; i < n; i++)
                {
                    BinaryPrimitives.WriteSingleBigEndian(dest[(i * 8)..], (float)s[a + i].Real);
                    BinaryPrimitives.WriteSingleBigEndian(dest[(i * 8 + 4)..], (float)s[a + i].Imaginary);
                }
                break;
            }
            case 'M':
            {
                var s = (Complex[])src;
                for (int i = 0; i < n; i++)
                {
                    BinaryPrimitives.WriteDoubleBigEndian(dest[(i * 16)..], s[a + i].Real);
                    BinaryPrimitives.WriteDoubleBigEndian(dest[(i * 16 + 8)..], s[a + i].Imaginary);
                }
                break;
            }
            default: throw new FitsException($"Unsupported element type '{code}'.");
        }
    }

    private static long FitsBytes(char code, long count) => BinaryFormat.ByteLength(code, count);

    /// <summary>Width in characters of the strings of a fixed-length character column.</summary>
    public static long StringWidth(BinaryFormat f, long[]? dim)
    {
        // TDIM only counts when it is consistent with the repeat count; otherwise fall back to the TFORM width.
        if (dim is { Length: > 0 } && dim[0] > 0 && dim[0] <= f.Repeat && f.Repeat % dim[0] == 0) return dim[0];
        return f.CharWidth > 0 ? f.CharWidth : f.Repeat;
    }

    private static string DecodeString(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        if (end >= 0) bytes = bytes[..end];
        return TableColumn.Latin1(bytes).TrimEnd(' ');
    }

    /// <summary>Number of strings (character columns) or values per row of a fixed-length column.</summary>
    public static long ItemsPerRow(BinaryFormat f, long[]? dim)
    {
        if (f.IsVariable) return 1;
        if (f.Code != 'A') return f.Repeat;
        long w = StringWidth(f, dim);
        return w == 0 ? 0 : f.Repeat / w;
    }

    /// <summary>Bytes the decoded array uses per element, for the memory budget of variable-length columns.</summary>
    private static int OutputBytesPerElement(char code) => code switch
    {
        'L' => 2, // bool?
        'I' => 2,
        'J' or 'E' => 4,
        'K' or 'D' => 8,
        'C' or 'M' => 16,
        _ => 1,
    };

    /// <summary>Largest number of table rows decoded for a table whose rows have no bytes.</summary>
    private const long MaxZeroWidthRows = 1 << 24;

    /// <summary>Validates the position of a column against the table size before anything is allocated.</summary>
    public static void CheckLayout(long rows, long rowWidth, long offset, BinaryFormat f, long dataLength)
    {
        // Everything is bounded by the real data length: never trust NAXIS1/NAXIS2/TFORM on their own.
        long tableBytes;
        try { tableBytes = checked(rows * rowWidth); }
        catch (OverflowException) { throw new FitsException("The table size overflows."); }
        if (tableBytes > dataLength) throw new FitsException("The table is larger than its data.");
        long fieldBytes = f.FieldBytes;
        if (offset < 0 || fieldBytes > rowWidth - offset) throw new FitsException("A column does not fit in the row width (NAXIS1).");
        if (rowWidth == 0 && rows > MaxZeroWidthRows) throw new FitsException("Too many rows of zero width.");
    }

    /// <summary>Allocates the native array for all the rows of a fixed-length column.</summary>
    public static Array AllocFixed(BinaryFormat f, long[]? dim, long rows)
    {
        long elements;
        try
        {
            elements = f.Code == 'A' ? checked(rows * ItemsPerRow(f, dim)) : checked(rows * f.Repeat);
        }
        catch (OverflowException)
        {
            throw new FitsException("The column size overflows.");
        }
        if (elements > Array.MaxLength) throw new FitsException("The column has too many values to be read as one array.");
        return f.Code == 'A' ? new string[elements] : Alloc(f.Code, elements);
    }

    /// <summary>Decodes <paramref name="chunkRows"/> consecutive rows held in <paramref name="chunk"/> into rows starting at <paramref name="rowStart"/> of <paramref name="dest"/>.</summary>
    public static void DecodeFixedChunk(ReadOnlySpan<byte> chunk, long chunkRows, long rowWidth, long offset, BinaryFormat f, long[]? dim, Array dest, long rowStart)
    {
        if (f.Code == 'A')
        {
            long width = StringWidth(f, dim), perRow = ItemsPerRow(f, dim);
            var strings = (string[])dest;
            for (long r = 0; r < chunkRows; r++)
                for (long k = 0; k < perRow; k++)
                    strings[(rowStart + r) * perRow + k] = DecodeString(chunk.Slice(checked((int)(r * rowWidth + offset + k * width)), (int)width));
            return;
        }
        if (f.Repeat == 1)
        {
            // the common case of one value per row: a strided loop without per-row dispatch
            switch (f.Code)
            {
                case 'B':
                {
                    var d = (byte[])dest;
                    for (long r = 0; r < chunkRows; r++) d[rowStart + r] = chunk[(int)(r * rowWidth + offset)];
                    return;
                }
                case 'I':
                {
                    var d = (short[])dest;
                    for (long r = 0; r < chunkRows; r++) d[rowStart + r] = BinaryPrimitives.ReadInt16BigEndian(chunk.Slice((int)(r * rowWidth + offset)));
                    return;
                }
                case 'J':
                {
                    var d = (int[])dest;
                    for (long r = 0; r < chunkRows; r++) d[rowStart + r] = BinaryPrimitives.ReadInt32BigEndian(chunk.Slice((int)(r * rowWidth + offset)));
                    return;
                }
                case 'K':
                {
                    var d = (long[])dest;
                    for (long r = 0; r < chunkRows; r++) d[rowStart + r] = BinaryPrimitives.ReadInt64BigEndian(chunk.Slice((int)(r * rowWidth + offset)));
                    return;
                }
                case 'E':
                {
                    var d = (float[])dest;
                    for (long r = 0; r < chunkRows; r++) d[rowStart + r] = BinaryPrimitives.ReadSingleBigEndian(chunk.Slice((int)(r * rowWidth + offset)));
                    return;
                }
                case 'D':
                {
                    var d = (double[])dest;
                    for (long r = 0; r < chunkRows; r++) d[rowStart + r] = BinaryPrimitives.ReadDoubleBigEndian(chunk.Slice((int)(r * rowWidth + offset)));
                    return;
                }
            }
        }
        int fieldBytes = checked((int)f.FieldBytes);
        for (long r = 0; r < chunkRows; r++)
            Decode(f.Code, chunk.Slice(checked((int)(r * rowWidth + offset)), fieldBytes), dest, (rowStart + r) * f.Repeat, f.Repeat);
    }

    /// <summary>Decodes one column of the whole table into its native array (see <see cref="TableColumn"/>).</summary>
    public static Array DecodeColumn(ReadOnlySpan<byte> data, long rows, long rowWidth, long offset, BinaryFormat f, long[]? dim, long theap)
    {
        CheckLayout(rows, rowWidth, offset, f, data.Length);
        long tableBytes = rows * rowWidth;

        if (!f.IsVariable)
        {
            Array dest = AllocFixed(f, dim, rows);
            DecodeFixedChunk(data, rows, rowWidth, offset, f, dim, dest, 0);
            return dest;
        }

        char e = f.ElementCode;
        bool wide = f.Code == 'Q';
        Array result = e == 'A' ? new string[rows] : Array.CreateInstance(Alloc(e, 0).GetType(), rows);
        if (f.Repeat == 0)
        {
            // r = 0: no descriptor is stored, every row is empty.
            for (long r = 0; r < rows; r++)
                if (e == 'A') ((string[])result)[r] = ""; else result.SetValue(Alloc(e, 0), r);
            return result;
        }

        long budget = Math.Max(64L << 20, 16L * data.Length); // overlapping descriptors may legally alias, but not without bound
        long decoded = 0;
        for (long r = 0; r < rows; r++)
        {
            var desc = data.Slice(checked((int)(r * rowWidth + offset)), wide ? 16 : 8);
            long count = wide ? BinaryPrimitives.ReadInt64BigEndian(desc) : BinaryPrimitives.ReadInt32BigEndian(desc);
            long off = wide ? BinaryPrimitives.ReadInt64BigEndian(desc[8..]) : BinaryPrimitives.ReadInt32BigEndian(desc[4..]);
            if (count < 0 || off < 0) throw new FitsException($"Invalid variable-length descriptor in row {r}.");
            long length = BinaryFormat.ByteLength(e, count);
            if (count > 0 && (theap < tableBytes || theap > data.Length))
                throw new FitsException("THEAP lies outside the data or inside the main table.");
            if (off > data.Length || length > data.Length || theap + off + length > data.Length)
                throw new FitsException($"Variable-length data of row {r} lies outside the heap.");
            decoded += count * OutputBytesPerElement(e); // the budget counts what is allocated: a bit becomes a bool, a float a Complex
            if (decoded > budget) throw new FitsException("The variable-length descriptors reference an implausible amount of data.");
            var cell = data.Slice((int)(theap + off), (int)length);
            if (e == 'A')
            {
                ((string[])result)[r] = DecodeString(cell);
            }
            else
            {
                Array row = Alloc(e, count);
                Decode(e, cell, row, 0, count);
                result.SetValue(row, r);
            }
        }
        return result;
    }

    /// <summary>Number of rows described by the data of a column.</summary>
    public static long RowsOf(TableColumn c, BinaryFormat f)
    {
        var data = c.Data ?? throw new FitsException($"Column '{c.Name}' has no data.");
        if (f.IsVariable) return data.Length;
        long per = ItemsPerRow(f, c.Dimensions);
        if (per == 0) return -1; // zero-width column: any row count
        if (data.Length % per != 0) throw new FitsException($"Column '{c.Name}' holds {data.Length} values, not a multiple of {per} per row.");
        return data.Length / per;
    }

    public static (byte[] Main, byte[] Heap, long Rows, long RowWidth) Encode(IReadOnlyList<TableColumn> columns, long? knownRows = null)
    {
        var formats = columns.Select(c => c.BinaryFormat).ToArray();
        long rows = -1;
        for (int i = 0; i < columns.Count; i++)
        {
            long r = RowsOf(columns[i], formats[i]);
            if (r < 0) continue;
            if (rows >= 0 && r != rows)
                throw new FitsException($"Column '{columns[i].Name}' has {r} rows but the previous columns have {rows}.");
            rows = r;
        }
        if (knownRows is { } known)
        {
            if (rows >= 0 && rows != known) throw new FitsException($"The columns hold {rows} rows but the table has {known}.");
            rows = known;
        }
        if (rows < 0) rows = 0;

        var offsets = new long[columns.Count];
        long rowWidth = 0;
        for (int i = 0; i < columns.Count; i++)
        {
            offsets[i] = rowWidth;
            rowWidth = checked(rowWidth + formats[i].FieldBytes);
        }

        var main = new byte[checked(rows * rowWidth)];
        var heap = new MemoryStream();
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            var f = formats[i];
            var data = c.Data!;
            if (f.IsVariable && f.Repeat == 0) continue; // no descriptor is stored
            if (!f.IsVariable)
            {
                if (f.Code == 'A')
                {
                    long width = StringWidth(f, c.Dimensions), per = ItemsPerRow(f, c.Dimensions);
                    var strings = (string[])data;
                    for (long r = 0; r < rows; r++)
                        for (long k = 0; k < per; k++)
                            WriteString(strings[r * per + k], main.AsSpan((int)(r * rowWidth + offsets[i] + k * width), (int)width));
                }
                else
                {
                    long bytes = f.FieldBytes;
                    for (long r = 0; r < rows; r++)
                        Encode(f.Code, data, r * f.Repeat, main.AsSpan((int)(r * rowWidth + offsets[i]), (int)bytes), f.Repeat);
                }
                continue;
            }

            char e = f.ElementCode;
            bool wide = f.Code == 'Q';
            for (long r = 0; r < rows; r++)
            {
                long count;
                byte[] cell;
                if (e == 'A')
                {
                    string s = ((string[])data)[r];
                    count = s.Length;
                    cell = new byte[count];
                    WriteString(s, cell);
                }
                else
                {
                    var row = (Array)data.GetValue(r)!;
                    count = row.Length;
                    cell = new byte[BinaryFormat.ByteLength(e, count)];
                    Encode(e, row, 0, cell, count);
                }
                long heapOffset = heap.Length;
                heap.Write(cell);
                var desc = main.AsSpan((int)(r * rowWidth + offsets[i]), wide ? 16 : 8);
                if (wide)
                {
                    BinaryPrimitives.WriteInt64BigEndian(desc, count);
                    BinaryPrimitives.WriteInt64BigEndian(desc[8..], heapOffset);
                }
                else
                {
                    if (heap.Length > int.MaxValue || count > int.MaxValue)
                        throw new FitsException("The heap is too large for 32-bit descriptors; use 64-bit (Q) descriptors.");
                    BinaryPrimitives.WriteInt32BigEndian(desc, (int)count);
                    BinaryPrimitives.WriteInt32BigEndian(desc[4..], (int)heapOffset);
                }
            }
        }
        return (main, heap.ToArray(), rows, rowWidth);
    }

    /// <summary>Writes an ASCII string into a fixed-width cell, padding with NUL.</summary>
    private static void WriteString(string s, Span<byte> cell)
    {
        if (s.Length > cell.Length) throw new FitsException($"String '{s}' does not fit in {cell.Length} characters.");
        for (int i = 0; i < s.Length; i++)
        {
            // Latin-1 is accepted so that tables read from real files (e.g. a degree sign) can be written back unchanged.
            if (s[i] is < ' ' or > '\u00FF') throw new FitsException("Table strings must be single-byte characters without control codes.");
            cell[i] = (byte)s[i];
        }
        cell[s.Length..].Clear();
    }

    /// <summary>Copies rows <c>[start, start+count)</c> of a column's data into a new array.</summary>
    public static Array SliceRows(TableColumn c, long start, long count)
    {
        var f = c.BinaryFormat;
        return SliceRows(c.Data!, ItemsPerRow(f, c.Dimensions), start, count);
    }

    public static Array SliceRows(Array data, long perRow, long start, long count)
    {
        var result = Array.CreateInstance(data.GetType().GetElementType()!, count * perRow);
        Array.Copy(data, start * perRow, result, 0, count * perRow);
        return result;
    }

    public static Array Concat(Array a, Array b)
    {
        var result = Array.CreateInstance(a.GetType().GetElementType()!, a.Length + b.Length);
        Array.Copy(a, result, a.Length);
        Array.Copy(b, 0, result, a.Length, b.Length);
        return result;
    }
}
