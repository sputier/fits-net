namespace Eleris.FitsNet;

/// <summary>The MIME types of Appendix G of the standard (RFC 4047).</summary>
public static class FitsMimeTypes
{
    /// <summary>The general type, for any FITS file.</summary>
    public const string Application = "application/fits";

    /// <summary>For a FITS file whose purpose is a single image in the primary HDU.</summary>
    public const string Image = "image/fits";

    /// <summary>The usual file name extensions.</summary>
    public static readonly IReadOnlyList<string> FileExtensions = [".fits", ".fit", ".fts"];
}

/// <summary>The header inheritance convention (Appendix K): extensions that share the keywords of the primary HDU.</summary>
public static class HeaderInheritance
{
    /// <summary>
    /// The header of an HDU as an application should see it. When the HDU is an extension whose header has <c>INHERIT = T</c>, the
    /// keywords of the primary header are added after its own, except the mandatory ones and the commentary cards, and except those the
    /// extension defines itself (its value takes precedence). The HDU itself is not modified.
    /// </summary>
    /// <param name="file">The file the HDU belongs to.</param>
    /// <param name="index">Index of the HDU in <paramref name="file"/>.</param>
    public static FitsHeader GetEffectiveHeader(this FitsFile file, int index)
    {
        ArgumentNullException.ThrowIfNull(file);
        var hdu = file[index];
        var result = new FitsHeader();
        foreach (var card in hdu.Header) result.Add(card);
        if (index == 0 || !hdu.Header.TryGetValue("INHERIT", out bool inherit) || !inherit) return result;

        foreach (var card in file[0].Header)
        {
            if (card.Kind != FitsCardKind.Value) continue;
            string key = card.Keyword;
            if (key is "SIMPLE" or "BITPIX" or "NAXIS" or "EXTEND" or "GROUPS" or "PCOUNT" or "GCOUNT" or "INHERIT" or "XTENSION"
                    or "EXTNAME" or "EXTVER" or "EXTLEVEL" or "CHECKSUM" or "DATASUM"   // they identify the primary HDU and describe its own bytes
                || IsAxisKeyword(key)) continue;
            if (hdu.Header.Contains(key)) continue; // the extension wins
            result.Add(card);
        }
        return result;
    }

    private static bool IsAxisKeyword(string key) =>
        key.StartsWith("NAXIS", StringComparison.Ordinal) && key.Length > 5 && key.AsSpan(5).IndexOfAnyExceptInRange('0', '9') < 0;
}

/// <summary>The Green Bank convention (Appendix L): header keywords expanded into binary-table columns, and constant columns collapsed into keywords.</summary>
public static class GreenBankConvention
{
    private static readonly HashSet<string> Structural =
    [
        "XTENSION", "BITPIX", "NAXIS", "PCOUNT", "GCOUNT", "TFIELDS", "EXTNAME", "EXTVER", "EXTLEVEL", "THEAP", "DATE", "ORIGIN",
        "COMMENT", "HISTORY", "CONTINUE", "END",
        // keywords that cannot appear in a binary table, or that describe bytes of the table and would go stale
        "SIMPLE", "EXTEND", "GROUPS", "BSCALE", "BZERO", "BLANK", "INHERIT", "CHECKSUM", "DATASUM",
    ];

    private static readonly string[] StructuralPrefixes = ["NAXIS", "TTYPE", "TFORM", "TUNIT", "TSCAL", "TZERO", "TNULL", "TDISP", "TDIM", "TBCOL"];

    /// <summary>Whether a keyword may be expanded into a column (the keywords that describe the structure of the table may not).</summary>
    /// <param name="keyword">The keyword.</param>
    public static bool IsExpandable(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        if (Structural.Contains(keyword)) return false;
        foreach (string prefix in StructuralPrefixes)
            if (keyword.StartsWith(prefix, StringComparison.Ordinal) && keyword.Length > prefix.Length && keyword.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0)
                return false;
        return keyword.Length is >= 1 and <= 8 && keyword.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_');
    }

    /// <summary>
    /// The value of a "keyword" in a row of a table: the cell of the column of that name if there is one (it takes precedence), else the
    /// value of the header keyword. Cells are returned unscaled; a cell holding several values is returned as an array. Each call reads the
    /// column: to visit every row use <see cref="GetValues"/>.
    /// </summary>
    /// <returns>The value, or null when there is neither a column nor a keyword of that name.</returns>
    /// <param name="table">The table.</param>
    /// <param name="name">The name of a column or of a keyword.</param>
    /// <param name="row">The zero-based index of the row.</param>
    public static object? GetValue(this BinaryTableHdu table, string name, long row)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(name);
        if ((ulong)row >= (ulong)Math.Max(table.RowCount, 0)) throw new ArgumentOutOfRangeException(nameof(row));
        int index = table.IndexOf(name);
        if (index < 0) return table.Header.Find(name)?.Value;
        Array data = table.ReadRaw(index);
        long perRow = data.Length / table.RowCount;
        if (perRow == 1) return data.GetValue(row);
        var cell = Array.CreateInstance(data.GetType().GetElementType()!, perRow);
        Array.Copy(data, row * perRow, cell, 0, perRow);
        return cell;
    }

    /// <summary>The values of a "keyword" for every row (see <see cref="GetValue"/>); reads the column once, unlike calling <see cref="GetValue"/> per row.</summary>
    /// <returns>One entry per row, or null when there is neither a column nor a keyword of that name.</returns>
    /// <param name="table">The table.</param>
    /// <param name="name">The name of a column or of a keyword.</param>
    public static object?[]? GetValues(this BinaryTableHdu table, string name)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(name);
        long rows = table.RowCount;
        if (rows > Array.MaxLength) throw new NotSupportedException("The table has too many rows to be read into one array.");
        int index = table.IndexOf(name);
        if (index < 0)
        {
            if (table.Header.Find(name) is not { } card) return null;
            var constant = new object?[rows];
            Array.Fill(constant, card.Value);
            return constant;
        }
        Array data = table.ReadRaw(index);
        var result = new object?[rows];
        long perRow = rows == 0 ? 0 : data.Length / rows;
        for (long r = 0; r < rows; r++)
        {
            if (perRow == 1)
            {
                result[r] = data.GetValue(r);
                continue;
            }
            var cell = Array.CreateInstance(data.GetType().GetElementType()!, perRow);
            Array.Copy(data, r * perRow, cell, 0, perRow);
            result[r] = cell;
        }
        return result;
    }

    /// <summary>
    /// Replaces the keyword by a column of the same name that holds its value in every row (so that rows can then differ).
    /// </summary>
    /// <exception cref="ArgumentException">The keyword is a structural one, is not in the header, has no simple value, or a column of that name exists.</exception>
    /// <param name="table">The table.</param>
    /// <param name="keyword">The keyword to turn into a column.</param>
    public static void ExpandKeyword(this BinaryTableHdu table, string keyword)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!IsExpandable(keyword)) throw new ArgumentException($"'{keyword}' describes the structure of the table and cannot become a column.", nameof(keyword));
        if (table.IndexOf(keyword) >= 0) throw new ArgumentException($"A column named '{keyword}' already exists.", nameof(keyword));
        var card = table.Header.Find(keyword) ?? throw new ArgumentException($"The header has no keyword '{keyword}'.", nameof(keyword));
        long announced = table.RowCount;
        if (announced > Array.MaxLength / 8) throw new NotSupportedException("The table has too many rows to hold one more column in memory.");
        if (table.Source.Length < announced * table.RowWidth) throw new FitsException("The data of the table is shorter than its header announces.");
        int rows = (int)announced;
        TableColumn column = card.Value switch
        {
            bool b => TableColumn.CreateLogical(keyword, Enumerable.Repeat((bool?)b, rows).ToArray()),
            long l when l is >= int.MinValue and <= int.MaxValue => TableColumn.Create<int>(keyword, Enumerable.Repeat((int)l, rows).ToArray()),
            long l => TableColumn.Create<long>(keyword, Enumerable.Repeat(l, rows).ToArray()),
            double d => TableColumn.Create<double>(keyword, Enumerable.Repeat(d, rows).ToArray()),
            string s => TableColumn.CreateString(keyword, Enumerable.Repeat(s, rows).ToArray(), Math.Max(1, s.Length)),
            _ => throw new ArgumentException($"The value of '{keyword}' cannot be stored in a column.", nameof(keyword)),
        };
        table.AddColumn(column);
        table.Header.Remove(keyword);
    }

    /// <summary>
    /// Collapses into header keywords the columns that hold the same value in every row, when their name is a valid keyword name,
    /// and the value is a number, a string or a logical value (unscaled, without TNULL). The last column of a table is kept.
    /// </summary>
    /// <returns>The names of the columns that became keywords.</returns>
    /// <param name="table">The table.</param>
    public static IReadOnlyList<string> CollapseConstantColumns(this BinaryTableHdu table)
    {
        ArgumentNullException.ThrowIfNull(table);
        long rows = table.RowCount;
        if (rows == 0) return [];
        var columns = table.ReadColumns().ToList();
        var constant = new object?[columns.Count];
        var names = columns.Select(col => col.Name?.Trim()).ToList();
        for (int i = 0; i < columns.Count; i++)
        {
            string? name = names[i];
            if (name is null || !IsExpandable(name) || table.Header.Contains(name)) continue;
            if (names.Count(n => string.Equals(n, name, StringComparison.Ordinal)) > 1) continue;   // two columns of one name cannot share a keyword
            var col = columns[i];
            // a unit, a display format, dimensions or other per-column keywords would be lost: leave those columns alone
            if (col.Unit is not null || col.Display is not null || col.Dimensions is not null || col.Extras.Count > 0) continue;
            constant[i] = ConstantValue(col, rows);
        }
        // a table keeps at least one column
        if (constant.All(v => v is not null)) constant[^1] = null;
        var kept = columns.Where((_, i) => constant[i] is null).ToList();
        if (kept.Count == columns.Count) return [];
        var collapsedNames = new List<string>();
        var values = new List<object>();
        for (int i = 0; i < columns.Count; i++)
        {
            if (constant[i] is not { } value) continue;
            collapsedNames.Add(names[i]!);
            values.Add(value);
        }
        // the cards are checked before anything is changed: a value that a header cannot hold leaves the table as it was
        var cards = collapsedNames.Select((n, i) => new FitsCard(n, values[i])).ToList();
        table.SetColumns(kept);
        foreach (var card in cards) table.Header.Set(card.Keyword, card.Value);
        return collapsedNames;
    }

    private static object? ConstantValue(TableColumn column, long rows)
    {
        var f = column.BinaryFormat;
        if (f.IsVariable || column.Data is not { } data || column.Scale is not (null or 1.0) || column.Zero is not (null or 0.0) || column.Null is not null) return null;
        if (f.Code is 'X' or 'C' or 'M' || data.Length != rows) return null; // one value per row only
        object? first = data.GetValue(0);
        for (long i = 1; i < rows; i++)
            if (!Equals(first, data.GetValue(i))) return null;
        return first switch
        {
            string s => s.TrimEnd(),
            bool b => b,
            byte v => (long)v,
            short v => (long)v,
            int v => (long)v,
            long v => v,
            float v when float.IsFinite(v) => (double)v,
            double v when double.IsFinite(v) => v,
            _ => null,
        };
    }
}

/// <summary>The conventional Stokes values of the <c>STOKES</c> axis (Table 29 of the standard).</summary>
public static class StokesParameters
{
    private static readonly (int Code, string Symbol)[] Table =
    [
        (1, "I"), (2, "Q"), (3, "U"), (4, "V"), (-1, "RR"), (-2, "LL"), (-3, "RL"), (-4, "LR"), (-5, "XX"), (-6, "YY"), (-7, "XY"), (-8, "YX"),
    ];

    /// <summary>The symbol of a coordinate value of a STOKES axis, or null when the value is not a conventional one.</summary>
    /// <param name="coordinate">A coordinate value of a <c>STOKES</c> axis.</param>
    public static string? Symbol(double coordinate)
    {
        double rounded = Math.Round(coordinate);
        if (Math.Abs(coordinate - rounded) > 1e-9) return null;
        foreach (var (code, symbol) in Table)
            if (code == rounded) return symbol;
        return null;
    }

    /// <summary>The coordinate value of a symbol such as <c>Q</c> or <c>RR</c>, or null if unknown.</summary>
    /// <param name="symbol">The symbol, for example <c>I</c>, <c>Q</c> or <c>RR</c>.</param>
    public static int? Code(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        foreach (var (code, s) in Table)
            if (string.Equals(s, symbol.Trim(), StringComparison.Ordinal)) return code;
        return null;
    }

    /// <summary>The conventional meaning of a symbol, from Table 29.</summary>
    /// <param name="symbol">The symbol, for example <c>I</c>, <c>Q</c> or <c>RR</c>.</param>
    public static string Description(string symbol) => symbol?.Trim() switch
    {
        "I" => "Standard Stokes unpolarized",
        "Q" or "U" => "Standard Stokes linear",
        "V" => "Standard Stokes circular",
        "RR" => "Right-right circular",
        "LL" => "Left-left circular",
        "RL" => "Right-left cross-circular",
        "LR" => "Left-right cross-circular",
        "XX" => "X parallel linear",
        "YY" => "Y parallel linear",
        "XY" => "XY cross linear",
        "YX" => "YX cross linear",
        null => throw new ArgumentNullException(nameof(symbol)),
        _ => throw new ArgumentException($"'{symbol}' is not a conventional Stokes symbol.", nameof(symbol)),
    };
}
