using System.Globalization;
using System.Text;

namespace Eleris.FitsNet;

/// <summary>An ASCII table extension (<c>XTENSION = 'TABLE'</c>). Columns hold string[] (A), long?[] (I) or double[] (F/E/D, NaN = null).</summary>
public sealed class AsciiTableHdu : TableHdu
{
    /// <summary>Creates a table from columns of equal length (see the <c>CreateAscii*</c> factories of <see cref="TableColumn"/>).</summary>
    /// <param name="columns">The columns, each with its data; they must all have the same number of rows.</param>
    public static AsciiTableHdu Create(IReadOnlyList<TableColumn> columns)
    {
        var hdu = new AsciiTableHdu(new FitsHeader(), DataSource.Empty);
        hdu.SetColumns(columns);
        return hdu;
    }

    internal AsciiTableHdu(FitsHeader header, DataSource source) : base(header, source) { }

    private protected override string XtensionName => "TABLE";

    private protected override bool IsAscii => true;

    /// <summary>ASCII tables are padded with blanks.</summary>
    internal override byte PaddingByte => (byte)' ';

    private readonly record struct AsciiFormat(char Code, int Width, int Decimals)
    {
        public static AsciiFormat Parse(string tform)
        {
            string s = tform.Trim().ToUpperInvariant();
            int blank = s.IndexOf(' ');
            if (blank > 0) s = s[..blank]; // trailing text after the format is ignored
            if (s.Length < 2 || s[0] is not ('A' or 'I' or 'F' or 'E' or 'D'))
                throw new FitsException($"Invalid ASCII table TFORM '{tform}'.");
            string rest = s[1..];
            int dot = rest.IndexOf('.');
            string w = dot < 0 ? rest : rest[..dot];
            string d = dot < 0 || dot == rest.Length - 1 ? "0" : rest[(dot + 1)..]; // "E12." means no decimals
            if (!int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out int width) || width < 1
                || !int.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out int dec))
                throw new FitsException($"Invalid ASCII table TFORM '{tform}'.");
            if (s[0] is 'F' or 'E' or 'D' && dec > width)
                throw new FitsException($"TFORM '{tform}' leaves no room for the number.");
            return new AsciiFormat(s[0], width, dec);
        }
    }

    private protected override long ItemsPerRow(TableColumn column) => 1;

    private protected override Type ExpectedDataType(TableColumn column) => AsciiFormat.Parse(column.Format).Code switch
    {
        'A' => typeof(string[]),
        'I' => typeof(long?[]),
        _ => typeof(double[]),
    };

    private protected override void ValidateColumns(IReadOnlyList<TableColumn> columns, long rowWidth)
    {
        long previousEnd = 0;
        foreach (var c in columns.OrderBy(c => c.AsciiStart))
        {
            var f = AsciiFormat.Parse(c.Format);
            if (c.AsciiStart < 1 || c.AsciiStart - 1 + (long)f.Width > rowWidth)
                throw new FitsException($"Column '{c.Name}' (TBCOL {c.AsciiStart}, width {f.Width}) does not fit in {rowWidth} bytes per row.");
            if (c.AsciiStart <= previousEnd)
                throw new FitsException($"Column '{c.Name}' overlaps the previous column (TBCOL {c.AsciiStart}).");
            previousEnd = c.AsciiStart - 1 + f.Width;
        }
    }

    private protected override long[] ColumnOffsets(IReadOnlyList<TableColumn> columns) =>
        columns.Select(c => (long)c.AsciiStart - 1).ToArray();

    private protected override Array DecodeColumn(ReadOnlySpan<byte> data, long rows, TableColumn column, int index, long offset)
    {
        var f = AsciiFormat.Parse(column.Format);
        long rowWidth = RowWidth;
        if (column.AsciiStart < 1 || column.AsciiStart - 1 + (long)f.Width > rowWidth)
            throw new FitsException($"Column {index} does not fit in the row width (NAXIS1).");
        long total;
        try { total = checked(rows * rowWidth); }
        catch (OverflowException) { throw new FitsException("The table size overflows."); }
        if (total > data.Length) throw new FitsException("The table is larger than its data.");
        string? tnull = (column.Null as string)?.Trim();
        // A TNULL that Latin-1 cannot hold never matches a cell.
        byte[]? tnullBytes = tnull is not null && tnull.All(c => c <= 0xFF) ? Encoding.Latin1.GetBytes(tnull) : null;
        // The fields are read straight from the bytes of the table: a string for each cell is only made when something is wrong.
        int start = (int)column.AsciiStart - 1, width = f.Width;

        switch (f.Code)
        {
            case 'A':
            {
                var strings = new string[rows];
                for (long r = 0; r < rows; r++) strings[r] = TableColumn.Latin1(Cell(data, r, rowWidth, start, width).TrimEnd((byte)' '));
                return strings;
            }
            case 'I':
            {
                var result = new long?[rows];
                for (int i = 0; i < result.Length; i++)
                {
                    ReadOnlySpan<byte> t = TrimWhite(Cell(data, i, rowWidth, start, width));
                    if (t.Length == 0 || tnullBytes is not null && t.SequenceEqual(tnullBytes)) continue;
                    result[i] = long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v)
                        ? v : throw new FitsException($"Invalid integer '{TableColumn.Latin1(t)}' in column {index}, row {i}.");
                }
                return result;
            }
            default:
            {
                var result = new double[rows];
                for (int i = 0; i < result.Length; i++)
                {
                    ReadOnlySpan<byte> t = TrimWhite(Cell(data, i, rowWidth, start, width));
                    if (t.Length == 0 || tnullBytes is not null && t.SequenceEqual(tnullBytes)) result[i] = double.NaN;
                    else if (!TryParseFloatFast(t, f.Decimals, out result[i])) result[i] = ParseFloat(TableColumn.Latin1(t), f.Decimals, index, i);
                }
                return result;
            }
        }
    }

    private static ReadOnlySpan<byte> Cell(ReadOnlySpan<byte> data, long row, long rowWidth, int start, int width) =>
        data.Slice((int)(row * rowWidth + start), width);

    private static ReadOnlySpan<byte> TrimWhite(ReadOnlySpan<byte> field)
    {
        int start = 0, end = field.Length;
        while (start < end && char.IsWhiteSpace((char)field[start])) start++;
        while (end > start && char.IsWhiteSpace((char)field[end - 1])) end--;
        return field[start..end];
    }

    /// <summary>
    /// The usual case, a number with its decimal point in the field (<c>12.5</c>, <c>-1.25E+03</c>, <c>0.5D-2</c>), parsed without any
    /// text. Anything else (an implied decimal point, an exponent without a letter, an invalid field) is left to
    /// <see cref="ParseFloat"/>, which also makes the error.
    /// </summary>
    private static bool TryParseFloatFast(ReadOnlySpan<byte> field, int decimals, out double value)
    {
        value = 0;
        if (decimals > 0 && field.IndexOf((byte)'.') < 0) return false;
        if (field.Length > 64) return false;
        Span<byte> text = stackalloc byte[field.Length];
        for (int i = 0; i < field.Length; i++) text[i] = field[i] is (byte)'D' or (byte)'d' ? (byte)'E' : field[i];
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <summary>Parses Fortran style reals (D or E exponent); without a decimal point it is implied <paramref name="decimals"/> digits from the right.</summary>
    private static double ParseFloat(string text, int decimals, int column, long row)
    {
        string s = text.Replace('D', 'E').Replace('d', 'E').Replace('e', 'E');
        int e = s.IndexOf('E');
        string mantissa = e < 0 ? s : s[..e];
        string exponent = e < 0 ? "" : s[e..];
        if (e < 0)
        {
            // A signed exponent without letter (e.g. 1.5+3) is also allowed by the standard.
            int sign = mantissa.LastIndexOfAny(['+', '-']);
            if (sign > 0) { exponent = "E" + mantissa[sign..]; mantissa = mantissa[..sign]; }
        }
        if (!mantissa.Any(char.IsAsciiDigit) || (exponent.Length > 0 && !exponent.Any(char.IsAsciiDigit)))
            throw new FitsException($"Invalid number '{text}' in column {column}, row {row}.");
        if (!mantissa.Contains('.') && decimals > 0)
        {
            string sign = mantissa.StartsWith('-') || mantissa.StartsWith('+') ? mantissa[..1] : "";
            string digits = mantissa[sign.Length..].PadLeft(decimals + 1, '0');
            mantissa = sign + digits[..^decimals] + "." + digits[^decimals..];
        }
        return double.TryParse(mantissa + exponent, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v)
            ? v : throw new FitsException($"Invalid number '{text}' in column {column}, row {row}.");
    }

    private protected override (byte[] Data, long Rows, long RowWidth, long HeapSize) EncodeColumns(IReadOnlyList<TableColumn> columns, long? knownRows)
    {
        long rows = -1;
        foreach (var c in columns)
        {
            long n = c.Data!.Length;
            if (rows >= 0 && n != rows) throw new FitsException($"Column '{c.Name}' has {n} rows but the previous columns have {rows}.");
            rows = n;
        }
        if (knownRows is { } known && columns.Count > 0 && rows != known)
            throw new FitsException($"The columns hold {rows} rows but the table has {known}.");
        if (rows < 0) rows = knownRows ?? 0;

        int start = 1;
        var formats = new AsciiFormat[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            formats[i] = AsciiFormat.Parse(columns[i].Format);
            columns[i].AsciiStart = start;
            start += formats[i].Width + 1;
        }
        long rowWidth = columns.Count == 0 ? 0 : start - 2;

        var buffer = new byte[checked(rows * rowWidth)];
        Array.Fill(buffer, (byte)' ');
        Span<char> digits = stackalloc char[128];
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            var f = formats[i];
            string? tnull = c.Null as string;
            string numberFormat = f.Code is 'F' or 'E' or 'D' ? (f.Code == 'F' ? "F" : "E") + f.Decimals.ToString(CultureInfo.InvariantCulture) : "";
            for (long r = 0; r < rows; r++)
            {
                int at = (int)(r * rowWidth + c.AsciiStart - 1);
                if (TryWriteFast(c, f, numberFormat, r, digits, buffer.AsSpan(at, f.Width))) continue;
                string text = FormatCell(c, f, r, tnull);
                if (text.Length > f.Width)
                    throw new FitsException($"Value '{text.Trim()}' does not fit in format {c.Format} (column '{c.Name}', row {r}); use a wider format.");
                Encoding.Latin1.GetBytes(text.PadLeft(f.Code == 'A' ? 0 : f.Width).PadRight(f.Width), 0, f.Width, buffer, (int)(r * rowWidth + c.AsciiStart - 1));
            }
        }
        return (buffer, rows, rowWidth, 0);
    }

    /// <summary>
    /// Writes the usual cells (a finite number that fits, a string of the right size) straight into the field without any text of its
    /// own. The others (null, too wide, invalid) are left to <see cref="FormatCell"/>, which also makes the errors.
    /// </summary>
    private static bool TryWriteFast(TableColumn c, AsciiFormat f, string numberFormat, long row, Span<char> digits, Span<byte> field)
    {
        int length;
        switch (f.Code)
        {
            case 'A':
            {
                string s = ((string[])c.Data!)[row];
                if (s.Length > field.Length) return false;
                for (int i = 0; i < s.Length; i++)
                {
                    char ch = s[i];
                    if (ch is < ' ' or > '\u00FF') return false;
                    field[i] = (byte)ch;
                }
                return true;   // the field is already filled with blanks
            }
            case 'I':
                if (((long?[])c.Data!)[row] is not { } v || !v.TryFormat(digits, out length, default, CultureInfo.InvariantCulture)) return false;
                break;
            default:
            {
                double d = ((double[])c.Data!)[row];
                if (!double.IsFinite(d) || !d.TryFormat(digits, out length, numberFormat, CultureInfo.InvariantCulture)) return false;
                if (f.Code == 'F')
                {
                    if (length > field.Length)
                    {
                        // no room for the leading zero: 0.5 is written .5 and -0.5 is written -.5
                        int zero = digits[0] == '-' ? 1 : 0;
                        if (length - zero < 2 || digits[zero] != '0' || digits[zero + 1] != '.') return false;
                        digits[(zero + 1)..length].CopyTo(digits[zero..]);
                        length--;
                    }
                }
                else
                {
                    // 1.2345678E+005 becomes 1.2345678E+05: two digits at least, as Fortran writes them
                    int e = digits[..length].IndexOf('E');
                    if (e < 0 || e + 2 > length) return false;
                    int first = e + 2;
                    while (first < length - 2 && digits[first] == '0') first++;
                    int exponentDigits = length - first;
                    if (exponentDigits < 1) return false;
                    digits[e] = f.Code;
                    int target = e + 2;
                    for (int i = 0; i < exponentDigits; i++) digits[target + i] = digits[first + i];
                    length = target + exponentDigits;
                }
                break;
            }
        }
        if (length > field.Length) return false;
        int offset = field.Length - length;   // numbers are right-aligned
        for (int i = 0; i < length; i++) field[offset + i] = (byte)digits[i];
        return true;
    }

    private static string FormatCell(TableColumn c, AsciiFormat f, long row, string? tnull)
    {
        switch (f.Code)
        {
            case 'A':
            {
                string s = ((string[])c.Data!)[row];
                foreach (char ch in s)
                    if (ch is < ' ' or > '\u00FF') throw new FitsException("Table strings must be single-byte characters without control codes.");
                return s;
            }
            case 'I':
                return ((long?[])c.Data!)[row] is { } v ? v.ToString(CultureInfo.InvariantCulture) : tnull ?? "";
            default:
            {
                double d = ((double[])c.Data!)[row];
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    if (double.IsInfinity(d)) throw new FitsException("Infinity cannot be stored in an ASCII table.");
                    return tnull ?? "";
                }
                if (f.Code == 'F') return FormatFixed(d, f);
                return FormatExponent(d, f.Decimals, f.Code);
            }
        }
    }

    /// <summary>
    /// Fixed-point text that fits the field: the leading zero is dropped when there is no room for it. A field written without a decimal
    /// point (implied decimals) is read by this library but never produced, because other software reads it differently.
    /// </summary>
    private static string FormatFixed(double d, AsciiFormat f)
    {
        string text = d.ToString("F" + f.Decimals, CultureInfo.InvariantCulture);
        if (text.Length <= f.Width) return text;
        if (text.StartsWith("0.", StringComparison.Ordinal)) text = text[1..];
        else if (text.StartsWith("-0.", StringComparison.Ordinal)) text = "-" + text[2..];
        return text; // does not fit: the caller reports it
    }

    /// <summary>Fortran style number with a two-digit exponent (e.g. <c>1.5000000E+10</c>); keeps full double precision.</summary>
    private static string FormatExponent(double d, int decimals, char letter)
    {
        string s = d.ToString("E" + decimals, CultureInfo.InvariantCulture); // 1.2345678E+005
        int e = s.IndexOf('E');
        string digits = s[(e + 2)..].TrimStart('0').PadLeft(2, '0');
        return s[..e] + letter + s[e + 1] + digits;
    }

    // ---- Typed access --------------------------------------------------------------------------------------------

    /// <summary>Reads an <c>A</c> column.</summary>
    /// <param name="index">The zero-based index of the column.</param>
    public string[] ReadStringColumn(int index) =>
        ColumnAt(index).Format.TrimStart().StartsWith('A') || ColumnAt(index).Format.TrimStart().StartsWith('a')
            ? (string[])ReadRaw(index)
            : throw new InvalidOperationException($"Column {index} is not a character column.");

    /// <summary>Reads an <c>I</c> column; blank or TNULL fields are null.</summary>
    /// <param name="index">The zero-based index of the column.</param>
    public long?[] ReadIntegerColumn(int index)
    {
        var column = ColumnAt(index);
        if (AsciiFormat.Parse(column.Format).Code != 'I') throw new InvalidOperationException($"Column {index} is not an integer column.");
        return (long?[])ReadRaw(index);
    }

    /// <summary>Reads a numeric column as <c>double</c>/<c>float</c> (TSCAL/TZERO applied, blank or TNULL fields are NaN).</summary>
    /// <typeparam name="T">The type the values are returned as: <c>double</c> or <c>float</c>, or <c>long</c> for an unscaled <c>I</c> column; any other type throws <see cref="InvalidOperationException"/>.</typeparam>
    /// <param name="index">The zero-based index of the column.</param>
    public T[] ReadColumn<T>(int index) where T : unmanaged
    {
        var column = ColumnAt(index);
        var f = AsciiFormat.Parse(column.Format);
        if (f.Code == 'A') throw new InvalidOperationException("Use ReadStringColumn for character columns.");
        double scale = column.Scale ?? 1.0, zero = column.Zero ?? 0.0;
        Array raw = ReadRaw(index);

        double[] values;
        if (f.Code == 'I')
        {
            var ints = (long?[])raw;
            if (typeof(T) == typeof(long) && scale == 1 && zero == 0)
            {
                var result = new long[ints.Length];
                for (int i = 0; i < result.Length; i++)
                    result[i] = ints[i] ?? throw new InvalidOperationException($"Row {i} is null; use ReadIntegerColumn.");
                return (T[])(object)result;
            }
            values = new double[ints.Length];
            for (int i = 0; i < values.Length; i++) values[i] = ints[i] is { } x ? x : double.NaN;
        }
        else
        {
            values = (double[])raw;
        }

        if (typeof(T) != typeof(double) && typeof(T) != typeof(float))
            throw new InvalidOperationException($"Cannot read column {index} as {typeof(T).Name}; use double or float.");
        if (scale != 1 || zero != 0)
            for (int i = 0; i < values.Length; i++) values[i] = zero + scale * values[i];
        if (typeof(T) == typeof(double)) return (T[])(object)values;
        var singles = new float[values.Length];
        for (int i = 0; i < singles.Length; i++) singles[i] = (float)values[i];
        return (T[])(object)singles;
    }

    /// <summary>Reads a numeric column by name (see <see cref="ReadColumn{T}(int)"/>).</summary>
    /// <typeparam name="T">The type the values are returned as: <c>double</c> or <c>float</c>, or <c>long</c> for an unscaled <c>I</c> column; any other type throws <see cref="InvalidOperationException"/>.</typeparam>
    /// <param name="name">The name of the column (<c>TTYPEn</c>); the match ignores case.</param>
    public T[] ReadColumn<T>(string name) where T : unmanaged => ReadColumn<T>(RequireIndex(name));

    /// <summary>Reads an <c>A</c> column by name: one string per row, with the trailing blanks removed.</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>); the match ignores case.</param>
    public string[] ReadStringColumn(string name) => ReadStringColumn(RequireIndex(name));
}
