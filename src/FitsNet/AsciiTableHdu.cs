using System.Globalization;
using System.Text;

namespace FitsNet;

/// <summary>An ASCII table extension (<c>XTENSION = 'TABLE'</c>). Columns hold string[] (A), long?[] (I) or double[] (F/E/D, NaN = null).</summary>
public sealed class AsciiTableHdu : TableHdu
{
    /// <summary>Creates a table from columns of equal length (see the <c>CreateAscii*</c> factories of <see cref="TableColumn"/>).</summary>
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
        var strings = new string[rows];
        for (long r = 0; r < rows; r++)
        {
            long at = r * rowWidth + column.AsciiStart - 1;
            if (column.AsciiStart < 1 || at + f.Width > data.Length) throw new FitsException($"Column {index} lies outside the row.");
            strings[r] = TableColumn.Latin1(data.Slice((int)at, f.Width));
        }

        switch (f.Code)
        {
            case 'A':
                for (int i = 0; i < strings.Length; i++) strings[i] = strings[i].TrimEnd(' ');
                return strings;
            case 'I':
            {
                var result = new long?[rows];
                for (int i = 0; i < result.Length; i++)
                {
                    string t = strings[i].Trim();
                    if (t.Length == 0 || t == tnull) continue;
                    result[i] = long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v)
                        ? v : throw new FitsException($"Invalid integer '{t}' in column {index}, row {i}.");
                }
                return result;
            }
            default:
            {
                var result = new double[rows];
                for (int i = 0; i < result.Length; i++)
                {
                    string t = strings[i].Trim();
                    result[i] = t.Length == 0 || t == tnull ? double.NaN : ParseFloat(t, f.Decimals, index, i);
                }
                return result;
            }
        }
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
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            var f = formats[i];
            string? tnull = c.Null as string;
            for (long r = 0; r < rows; r++)
            {
                string text = FormatCell(c, f, r, tnull);
                if (text.Length > f.Width)
                    throw new FitsException($"Value '{text.Trim()}' does not fit in format {c.Format} (column '{c.Name}', row {r}); use a wider format.");
                Encoding.Latin1.GetBytes(text.PadLeft(f.Code == 'A' ? 0 : f.Width).PadRight(f.Width), 0, f.Width, buffer, (int)(r * rowWidth + c.AsciiStart - 1));
            }
        }
        return (buffer, rows, rowWidth, 0);
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
    public string[] ReadStringColumn(int index) =>
        ColumnAt(index).Format.TrimStart().StartsWith('A') || ColumnAt(index).Format.TrimStart().StartsWith('a')
            ? (string[])ReadRaw(index)
            : throw new InvalidOperationException($"Column {index} is not a character column.");

    /// <summary>Reads an <c>I</c> column; blank or TNULL fields are null.</summary>
    public long?[] ReadIntegerColumn(int index)
    {
        var column = ColumnAt(index);
        if (AsciiFormat.Parse(column.Format).Code != 'I') throw new InvalidOperationException($"Column {index} is not an integer column.");
        return (long?[])ReadRaw(index);
    }

    /// <summary>Reads a numeric column as <c>double</c>/<c>float</c> (TSCAL/TZERO applied, blank or TNULL fields are NaN).</summary>
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
            values = ints.Select(v => v is { } x ? (double)x : double.NaN).ToArray();
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
        return (T[])(object)values.Select(v => (float)v).ToArray();
    }

    public T[] ReadColumn<T>(string name) where T : unmanaged => ReadColumn<T>(RequireIndex(name));

    public string[] ReadStringColumn(string name) => ReadStringColumn(RequireIndex(name));
}
