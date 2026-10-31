using System.Globalization;
using System.Numerics;
using System.Text;

namespace FitsNet;

/// <summary>
/// A table column: its header metadata (TTYPE, TFORM, TUNIT, TSCAL, TZERO, TNULL, TDISP, TDIM) and, optionally, its data.
/// Data is stored in the native representation of the column type:
/// <c>B</c>=byte[], <c>I</c>=short[], <c>J</c>=int[], <c>K</c>=long[], <c>E</c>=float[], <c>D</c>=double[],
/// <c>C</c>/<c>M</c>=Complex[], <c>L</c>=bool?[], <c>X</c>=bool[] (one entry per bit), <c>A</c>=string[];
/// variable-length columns (<c>P</c>/<c>Q</c>) hold one array per row (string[] for character data).
/// Fixed-length numeric data is flat: row-major, <c>repeat</c> values per row.
/// </summary>
public sealed class TableColumn
{
    /// <summary>
    /// Creates a column description without data.
    /// </summary>
    /// <param name="name">The column name (<c>TTYPEn</c>).</param>
    /// <param name="format">The format (<c>TFORMn</c>), such as <c>1J</c> or <c>8A</c> for a binary table, <c>F8.3</c> for an ASCII table.</param>
    public TableColumn(string? name, string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        Name = name;
        Format = format;
    }

    /// <summary>TTYPEn.</summary>
    public string? Name { get; set; }

    /// <summary>TFORMn, e.g. <c>1J</c>, <c>10A</c>, <c>PD(100)</c> (binary) or <c>F8.3</c> (ASCII).</summary>
    public string Format { get; set; }

    /// <summary>The physical unit (<c>TUNITn</c>), or null.</summary>
    public string? Unit { get; set; }

    /// <summary>The display format (<c>TDISPn</c>), or null.</summary>
    public string? Display { get; set; }

    /// <summary>TSCALn (null = 1).</summary>
    public double? Scale { get; set; }

    /// <summary>TZEROn (null = 0).</summary>
    public double? Zero { get; set; }

    private object? _null;

    /// <summary>TNULLn: an integer (stored as <see cref="long"/>) for binary integer columns, a <see cref="string"/> for ASCII tables.</summary>
    public object? Null
    {
        get => _null;
        set => _null = value switch
        {
            null or string => value,
            sbyte or byte or short or ushort or int or uint or long => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("TNULL must be an integer or a string."),
        };
    }

    /// <summary>
    /// Other per-column keywords of the registered conventions (TCTYPn, TCUNIn, TCRPXn, TCRVLn, TCDLTn, TCROTn, TRPOSn,
    /// TDMINn, TDMAXn, TLMINn, TLMAXn, TUCDn, TUTYPn), keyed by the keyword without its column number (e.g. <c>"TDMIN"</c>).
    /// They follow the column when columns are added or removed.
    /// </summary>
    public Dictionary<string, object?> Extras { get; } = [];

    /// <summary>TDIMn (e.g. 4, 3 for a 4x3 array per cell).</summary>
    public long[]? Dimensions { get; set; }

    /// <summary>The column data, or null when only the metadata was read.</summary>
    public Array? Data { get; set; }

    /// <summary>TBCOLn (1-based start column) of an ASCII table column.</summary>
    internal int AsciiStart { get; set; }

    internal BinaryFormat BinaryFormat => BinaryFormat.Parse(Format);

    // ---- Header I/O ----------------------------------------------------------------------------------------------

    private static readonly string[] ColumnKeys = ["TTYPE", "TFORM", "TUNIT", "TSCAL", "TZERO", "TNULL", "TDISP", "TDIM", "TBCOL"];

    private static readonly string[] ExtraKeys =
        ["TCTYP", "TCUNI", "TCRPX", "TCRVL", "TCDLT", "TCROT", "TRPOS", "TDMIN", "TDMAX", "TLMIN", "TLMAX", "TUCD", "TUTYP"];

    /// <summary>Whether <paramref name="key"/> is one of the registered per-column keywords carried by <see cref="Extras"/>.</summary>
    internal static bool IsKnownExtra(string key) => ExtraKeys.Contains(key);

    internal static bool IsColumnKeyword(string keyword)
    {
        if (keyword == "THEAP" || keyword == "TFIELDS") return true;
        foreach (string key in ColumnKeys.Concat(ExtraKeys))
            if (keyword.Length > key.Length && keyword.StartsWith(key, StringComparison.Ordinal)
                && keyword.AsSpan(key.Length).IndexOfAnyExceptInRange('0', '9') < 0)
                return true;
        return false;
    }

    internal static TableColumn FromHeader(FitsHeader h, int n, bool ascii)
    {
        string format = h.Get<string>("TFORM" + n) ?? throw new FitsException($"TFORM{n} is missing.");
        var column = new TableColumn(h.Get<string>("TTYPE" + n), format)
        {
            Unit = h.Get<string>("TUNIT" + n),
            Display = h.Get<string>("TDISP" + n),
            Scale = h.TryGetValue("TSCAL" + n, out double scale) ? scale : null,
            Zero = h.TryGetValue("TZERO" + n, out double zero) ? zero : null,
            Dimensions = ParseDimensions(h.Get<string>("TDIM" + n)),
        };
        foreach (string key in ExtraKeys)
            if (h.Find(key + n) is { } extra) column.Extras[key] = extra.Value;
        if (ascii)
        {
            column.Null = h.Get<string>("TNULL" + n);
            column.AsciiStart = h.TryGetValue("TBCOL" + n, out int start) ? start : throw new FitsException($"TBCOL{n} is missing.");
        }
        else
        {
            column.Null = h.TryGetValue("TNULL" + n, out long nul) ? nul : null;
        }
        return column;
    }

    internal void WriteToHeader(FitsHeader h, int n, bool ascii)
    {
        if (Name is not null) h.SetLenient("TTYPE" + n, Name);
        h.SetLenient("TFORM" + n, Format);
        if (ascii) h.SetLenient("TBCOL" + n, AsciiStart);
        if (Unit is not null) h.SetLenient("TUNIT" + n, Unit);
        if (Scale is not null) h.SetLenient("TSCAL" + n, Number(Scale.Value));
        if (Zero is not null) h.SetLenient("TZERO" + n, Number(Zero.Value));
        if (Null is not null) h.SetLenient("TNULL" + n, Null);
        if (Display is not null) h.SetLenient("TDISP" + n, Display);
        if (Dimensions is not null) h.SetLenient("TDIM" + n, "(" + string.Join(',', Dimensions) + ")");
        foreach (var (key, value) in Extras) h.SetLenient(key + n, value);
    }

    /// <summary>A shallow copy: metadata is copied, <see cref="Data"/> is shared.</summary>
    internal TableColumn Clone()
    {
        var copy = new TableColumn(Name, Format)
        {
            Unit = Unit, Display = Display, Scale = Scale, Zero = Zero, Null = Null,
            Dimensions = Dimensions?.ToArray(), Data = Data, AsciiStart = AsciiStart,
        };
        foreach (var (key, value) in Extras) copy.Extras[key] = value;
        return copy;
    }

    private static object Number(double v)
    {
        if (!double.IsFinite(v)) throw new FitsException("TSCAL and TZERO must be finite numbers.");
        if (v == 9223372036854775808.0) return new BigInteger(1UL << 63);
        return v == Math.Floor(v) && Math.Abs(v) < 9.2e18 ? (object)(long)v : v; // object: a plain ?: would promote the long to double
    }

    private static long[]? ParseDimensions(string? tdim)
    {
        if (tdim is null) return null;
        string s = tdim.Trim();
        if (s.Length < 2 || s[0] != '(' || s[^1] != ')') return null;
        var parts = s[1..^1].Split(',');
        var dims = new long[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!long.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out dims[i])) return null;
        return dims;
    }

    // ---- Factories -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Numeric column. Supported: byte, sbyte, short, ushort, int, uint, long, ulong, float, double. Unsigned types and
    /// sbyte use the standard TZERO conventions. <paramref name="repeat"/> values per row.
    /// </summary>
    /// <typeparam name="T">The numeric type; it chooses the format (<c>short</c> gives <c>I</c>, <c>float</c> gives <c>E</c>, and so on). <c>sbyte</c>, <c>ushort</c>, <c>uint</c> and <c>ulong</c> use the <c>TZERO</c> convention.</typeparam>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="values">The values of all the rows, row after row, <paramref name="repeat"/> values for each row.</param>
    /// <param name="repeat">The number of values in each row (the repeat count of <c>TFORMn</c>).</param>
    /// <param name="unit">The physical unit (<c>TUNITn</c>), or null.</param>
    /// <param name="dimensions">The dimensions of the array in each row (<c>TDIMn</c>), or null.</param>
    public static TableColumn Create<T>(string name, T[] values, int repeat = 1, string? unit = null, long[]? dimensions = null)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(values);
        if (repeat < 0) throw new ArgumentOutOfRangeException(nameof(repeat));
        if (repeat > 0 && values.Length % repeat != 0)
            throw new ArgumentException("The number of values must be a multiple of the repeat count.", nameof(values));
        Array native = ToNative(values, out char code, out double? zero);
        return new TableColumn(name, repeat.ToString(CultureInfo.InvariantCulture) + code)
        {
            Unit = unit, Zero = zero, Dimensions = dimensions, Data = native,
        };
    }

    /// <summary>Complex column (<c>C</c> single or <c>M</c> double precision storage).</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="values">The values of all the rows, <paramref name="repeat"/> for each row.</param>
    /// <param name="singlePrecision">True for single precision (<c>C</c>), false for double precision (<c>M</c>).</param>
    /// <param name="repeat">The number of values in each row.</param>
    /// <param name="unit">The physical unit (<c>TUNITn</c>), or null.</param>
    public static TableColumn CreateComplex(string name, Complex[] values, bool singlePrecision = false, int repeat = 1, string? unit = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (repeat > 0 && values.Length % repeat != 0) throw new ArgumentException("Length must be a multiple of the repeat count.", nameof(values));
        return new TableColumn(name, repeat.ToString(CultureInfo.InvariantCulture) + (singlePrecision ? 'C' : 'M')) { Unit = unit, Data = values };
    }

    /// <summary>Logical column (<c>L</c>): true, false or undefined (null).</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="values">The values of all the rows, <paramref name="repeat"/> for each row; null for an undefined value.</param>
    /// <param name="repeat">The number of values in each row.</param>
    public static TableColumn CreateLogical(string name, bool?[] values, int repeat = 1) =>
        new(name, repeat.ToString(CultureInfo.InvariantCulture) + "L") { Data = values };

    /// <summary>Bit column (<c>X</c>): <paramref name="bitsPerRow"/> bits per row, one bool per bit.</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="bits">All the bits, <paramref name="bitsPerRow"/> for each row.</param>
    /// <param name="bitsPerRow">The number of bits in each row (the repeat count of the <c>X</c> format).</param>
    public static TableColumn CreateBits(string name, bool[] bits, int bitsPerRow) =>
        new(name, bitsPerRow.ToString(CultureInfo.InvariantCulture) + "X") { Data = bits };

    /// <summary>Fixed-width character column: <paramref name="stringsPerRow"/> strings of <paramref name="width"/> characters per row.</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="values">The strings of all the rows, <paramref name="stringsPerRow"/> for each row.</param>
    /// <param name="width">The width of each string in characters; longer strings are refused.</param>
    /// <param name="stringsPerRow">The number of strings in each row.</param>
    public static TableColumn CreateString(string name, string[] values, int width, int stringsPerRow = 1)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        foreach (string v in values) CheckString(v, width);
        string tform = stringsPerRow == 1 ? $"{width}A" : $"{width * stringsPerRow}A{width}";
        return new TableColumn(name, tform) { Data = values };
    }

    /// <summary>Variable-length numeric column (<c>P</c>, or <c>Q</c> with 64-bit descriptors): one array per row.</summary>
    /// <typeparam name="T">The element type of the arrays.</typeparam>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="rows">One array for each row.</param>
    /// <param name="use64BitDescriptors">True to use 64-bit descriptors (<c>Q</c>) instead of 32-bit ones (<c>P</c>).</param>
    /// <param name="unit">The physical unit (<c>TUNITn</c>), or null.</param>
    public static TableColumn CreateVariable<T>(string name, T[][] rows, bool use64BitDescriptors = false, string? unit = null)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(rows);
        char code = 'B';
        double? zero = null;
        var converted = new List<Array>();
        foreach (T[] row in rows)
        {
            converted.Add(ToNative(row, out code, out zero));
        }
        if (rows.Length == 0) ToNative(Array.Empty<T>(), out code, out zero);
        Array data = Array.CreateInstance(ToNative(Array.Empty<T>(), out _, out _).GetType(), rows.Length);
        for (int i = 0; i < rows.Length; i++) data.SetValue(converted[i], i);
        long max = rows.Length == 0 ? 0 : rows.Max(r => (long)r.Length);
        return new TableColumn(name, $"1{(use64BitDescriptors ? 'Q' : 'P')}{code}({max})") { Unit = unit, Zero = zero, Data = data };
    }

    /// <summary>Variable-length character column: one string per row.</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="rows">One string for each row.</param>
    /// <param name="use64BitDescriptors">True to use 64-bit descriptors (<c>Q</c>) instead of 32-bit ones (<c>P</c>).</param>
    public static TableColumn CreateVariableString(string name, string[] rows, bool use64BitDescriptors = false)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (string r in rows) CheckString(r, int.MaxValue);
        long max = rows.Length == 0 ? 0 : rows.Max(r => (long)r.Length);
        return new TableColumn(name, $"1{(use64BitDescriptors ? 'Q' : 'P')}A({max})") { Data = rows };
    }

    /// <summary>ASCII table character column (<c>Aw</c>); width defaults to the longest value.</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="values">One string for each row.</param>
    /// <param name="width">The width of the field, or null for the smallest that holds every string.</param>
    public static TableColumn CreateAsciiString(string name, string[] values, int? width = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        int w = Math.Max(1, width ?? (values.Length == 0 ? 1 : values.Max(v => v.Length)));
        foreach (string v in values) CheckString(v, w);
        return new TableColumn(name, $"A{w}") { Data = values };
    }

    /// <summary>ASCII table integer column (<c>Iw</c>); width defaults to the widest value.</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="values">One value for each row; null for an undefined value.</param>
    /// <param name="width">The width of the field, or null for the smallest that holds every value.</param>
    public static TableColumn CreateAsciiInteger(string name, long?[] values, int? width = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        int w = Math.Max(1, width ?? (values.Length == 0 ? 1 : values.Max(v => (v ?? 0).ToString(CultureInfo.InvariantCulture).Length)));
        return new TableColumn(name, $"I{w}") { Data = values };
    }

    /// <summary>ASCII table floating point column; <paramref name="format"/> is <c>Fw.d</c>, <c>Ew.d</c> or <c>Dw.d</c>.</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>).</param>
    /// <param name="values">One value for each row.</param>
    /// <param name="format">The Fortran format of the field, for example <c>E15.7</c> or <c>F12.5</c>.</param>
    public static TableColumn CreateAsciiFloat(string name, double[] values, string format = "E15.7") =>
        new(name, format) { Data = values };

    private static void CheckString(string value, int width)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > width) throw new ArgumentException($"'{value}' is longer than the column width {width}.");
        foreach (char c in value)
            if (c is < ' ' or > '~') throw new ArgumentException("FITS table strings must be printable ASCII.");
    }

    /// <summary>Maps a CLR numeric array to its stored (native) array, the type code and the TZERO of unsigned conventions.</summary>
    /// <remarks>Tests <c>typeof(T)</c> rather than type patterns: CLR array covariance makes <c>sbyte[]</c> match <c>byte[]</c> and so on.</remarks>
    private static Array ToNative<T>(T[] values, out char code, out double? zero) where T : unmanaged
    {
        zero = null;
        object boxed = values;
        Type t = typeof(T);
        if (t == typeof(byte)) { code = 'B'; return (byte[])boxed; }
        if (t == typeof(short)) { code = 'I'; return (short[])boxed; }
        if (t == typeof(int)) { code = 'J'; return (int[])boxed; }
        if (t == typeof(long)) { code = 'K'; return (long[])boxed; }
        if (t == typeof(float)) { code = 'E'; return (float[])boxed; }
        if (t == typeof(double)) { code = 'D'; return (double[])boxed; }
        if (t == typeof(sbyte))
        {
            var s = (sbyte[])boxed;
            code = 'B';
            zero = -128;
            var r = new byte[s.Length];
            for (int i = 0; i < r.Length; i++) r[i] = unchecked((byte)((byte)s[i] ^ 0x80));
            return r;
        }
        if (t == typeof(ushort))
        {
            var u = (ushort[])boxed;
            code = 'I';
            zero = 32768;
            var r = new short[u.Length];
            for (int i = 0; i < r.Length; i++) r[i] = unchecked((short)(u[i] ^ 0x8000));
            return r;
        }
        if (t == typeof(uint))
        {
            var u = (uint[])boxed;
            code = 'J';
            zero = 2147483648.0;
            var r = new int[u.Length];
            for (int i = 0; i < r.Length; i++) r[i] = unchecked((int)(u[i] ^ 0x80000000u));
            return r;
        }
        if (t == typeof(ulong))
        {
            var u = (ulong[])boxed;
            code = 'K';
            zero = 9223372036854775808.0;
            var r = new long[u.Length];
            for (int i = 0; i < r.Length; i++) r[i] = unchecked((long)(u[i] ^ 0x8000000000000000UL));
            return r;
        }
        throw new NotSupportedException($"Column type {typeof(T).Name} is not supported; use the dedicated factory.");
    }

    internal static string Latin1(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);
}
