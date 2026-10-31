namespace FitsNet;

/// <summary>Common behaviour of ASCII (<c>TABLE</c>) and binary (<c>BINTABLE</c>) tables.</summary>
public abstract class TableHdu : Hdu
{
    private protected TableHdu(FitsHeader header, DataSource source) : base(header, source) { }

    private protected abstract string XtensionName { get; }

    private protected abstract bool IsAscii { get; }

    internal override HduLayout GetLayout(bool primary) => HduLayout.Parse(Header, primary);

    private HduLayout Layout => GetLayout(false);

    /// <summary>NAXIS2.</summary>
    public long RowCount => Layout.Axes[1];

    /// <summary>NAXIS1: bytes per row.</summary>
    public long RowWidth => Layout.Axes[0];

    /// <summary>TFIELDS.</summary>
    public int ColumnCount => Header.TryGetValue("TFIELDS", out int n) && n is >= 0 and <= 999
        ? n
        : throw new FitsException("TFIELDS is missing or invalid.");

    // The columns are parsed from the header once per header version: a wide table (hundreds of columns) must not re-parse
    // every column for each column that is read.
    private int _cacheVersion = -1;
    private List<TableColumn>? _templates;
    private long[]? _offsets;
    private Dictionary<string, int>? _names;

    private void EnsureCache()
    {
        if (_templates is not null && _cacheVersion == Header.Version) return;
        int count = ColumnCount;
        var list = new List<TableColumn>(count);
        for (int i = 1; i <= count; i++) list.Add(TableColumn.FromHeader(Header, i, IsAscii));
        long[] offsets = ColumnOffsets(list);
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < list.Count; i++)
            if (list[i].Name?.Trim() is { } name && !names.ContainsKey(name)) names[name] = i;
        (_templates, _offsets, _names, _cacheVersion) = (list, offsets, names, Header.Version);
    }

    /// <summary>Column metadata read from the header (<see cref="TableColumn.Data"/> is null).</summary>
    public IReadOnlyList<TableColumn> Columns
    {
        get
        {
            EnsureCache();
            return _templates!.Select(c => c.Clone()).ToList();
        }
    }

    /// <summary>Index of the column whose TTYPE equals <paramref name="name"/> (case-insensitive), or -1.</summary>
    /// <param name="name">The name of the column (<c>TTYPEn</c>); the match ignores case.</param>
    public int IndexOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureCache();
        return _names!.TryGetValue(name.Trim(), out int index) ? index : -1;
    }

    private protected int RequireIndex(string name)
    {
        int index = IndexOf(name);
        return index >= 0 ? index : throw new KeyNotFoundException($"No column named '{name}'.");
    }

    private protected TableColumn ColumnAt(int index)
    {
        EnsureCache();
        if (index < 0 || index >= _templates!.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _templates[index].Clone();
    }

    // ---- Codec hooks ---------------------------------------------------------------------------------------------

    private protected abstract Array DecodeColumn(ReadOnlySpan<byte> data, long rows, TableColumn column, int index, long offset);

    /// <summary>Byte offset of each column within a row (binary tables) or start offset (ASCII tables).</summary>
    private protected abstract long[] ColumnOffsets(IReadOnlyList<TableColumn> columns);

    private protected abstract (byte[] Data, long Rows, long RowWidth, long HeapSize) EncodeColumns(IReadOnlyList<TableColumn> columns, long? knownRows);

    private protected abstract long ItemsPerRow(TableColumn column);

    /// <summary>The CLR array type the data of a column must have, given its TFORM.</summary>
    private protected abstract Type ExpectedDataType(TableColumn column);

    private protected abstract void ValidateColumns(IReadOnlyList<TableColumn> columns, long rowWidth);

    // ---- Reading -------------------------------------------------------------------------------------------------

    /// <summary>The stored (unscaled) values of a column in the native representation described on <see cref="TableColumn"/>.</summary>
    /// <param name="index">The zero-based index of the column.</param>
    public Array ReadRaw(int index)
    {
        var column = ColumnAt(index);
        return ReadColumnData(column, index, KnownOffset(index));
    }

    private long KnownOffset(int index) =>
        _offsets![index] >= 0
            ? _offsets[index]
            : throw new FitsException($"The position of column {index + 1} is unknown because the TFORM of an earlier column is invalid or too wide.");

    /// <summary>Reads one column; the default loads the whole data unit, binary tables read fixed-length columns by blocks.</summary>
    private protected virtual Array ReadColumnData(TableColumn column, int index, long offset) =>
        DecodeColumn(Source.ReadMemory().Span, RowCount, column, index, offset);

    /// <summary>Reads every column, data included.</summary>
    public IReadOnlyList<TableColumn> ReadColumns()
    {
        var memory = Source.ReadMemory();
        long rows = RowCount;
        var columns = Columns.ToList();
        long[] offsets = _offsets!; // filled by Columns
        for (int i = 0; i < columns.Count; i++) columns[i].Data = DecodeColumn(memory.Span, rows, columns[i], i, KnownOffset(i));
        return columns;
    }

    // ---- Editing -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Replaces the whole table: columns, data and the structural keywords. Other header cards are kept, except the
    /// per-column keywords (TTYPEn, TFORMn, TUNITn, TSCALn, TZEROn, TNULLn, TDISPn, TDIMn, TBCOLn, THEAP and the registered
    /// TCTYPn/TDMINn-style keywords carried by <see cref="TableColumn.Extras"/>), which are regenerated from
    /// <paramref name="columns"/>; comments on them are not kept. The caller's column objects are not modified. Heap data not
    /// referenced by any row (and any gap before the heap) is dropped, and unused bytes at the end of rows are removed.
    /// </summary>
    /// <param name="columns">The new columns, each with its data; they must all have the same number of rows.</param>
    public virtual void SetColumns(IReadOnlyList<TableColumn> columns) => SetColumnsCore(columns, null);

    private void SetColumnsCore(IReadOnlyList<TableColumn> columns, long? knownRows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count > 999) throw new ArgumentException("A table has at most 999 columns.", nameof(columns));
        foreach (var c in columns)
        {
            if (c.Data is null) throw new ArgumentException($"Column '{c.Name}' has no data.", nameof(columns));
            Type expected = ExpectedDataType(c);
            if (c.Data.GetType() != expected)
                throw new ArgumentException($"Column '{c.Name}' has format '{c.Format}': its data must be {expected.Name} but is {c.Data.GetType().Name}.", nameof(columns));
            if (!expected.GetElementType()!.IsValueType)
                foreach (object? item in c.Data)
                    if (item is null) throw new ArgumentException($"Column '{c.Name}' contains a null entry.", nameof(columns));
            foreach (string key in c.Extras.Keys)
                if (!TableColumn.IsKnownExtra(key))
                    throw new ArgumentException($"'{key}' is not a supported per-column keyword; supported: TCTYP, TCUNI, TCRPX, TCRVL, TCDLT, TCROT, TRPOS, TDMIN, TDMAX, TLMIN, TLMAX, TUCD, TUTYP (upper case).", nameof(columns));
        }

        // Work on copies: encoding refreshes derived metadata (variable-length maxima, TBCOL) that must not leak into the caller's objects.
        var work = columns.Select(c => c.Clone()).ToList();
        var (data, rows, rowWidth, heap) = EncodeColumns(work, knownRows);
        ValidateColumns(work, rowWidth);

        Header.ReplaceAll(Header.Where(c => c.Kind != FitsCardKind.Value || !TableColumn.IsColumnKeyword(c.Keyword)).ToList());
        RebuildMandatory(Header, false, XtensionName, BitPix.Byte, [rowWidth, rows], heap, 1, false, false);
        Header.Set("TFIELDS", work.Count);
        for (int i = 0; i < work.Count; i++) work[i].WriteToHeader(Header, i + 1, IsAscii);
        MoveTfields();
        Source = new MemoryDataSource(data);
    }

    /// <summary>Appends a column (its data must have <see cref="RowCount"/> rows).</summary>
    /// <param name="column">The column to add; its data must have one entry for each row of the table.</param>
    public virtual void AddColumn(TableColumn column)
    {
        var columns = ReadColumns().ToList();
        columns.Add(column);
        SetColumnsCore(columns, RowCount);
    }

    /// <summary>Removes a column.</summary>
    /// <param name="index">The zero-based index of the column.</param>
    public virtual void RemoveColumn(int index)
    {
        var columns = ReadColumns().ToList();
        if (index < 0 || index >= columns.Count) throw new ArgumentOutOfRangeException(nameof(index));
        columns.RemoveAt(index);
        SetColumnsCore(columns, RowCount);
    }

    /// <summary>Removes <paramref name="count"/> rows starting at <paramref name="start"/>.</summary>
    /// <param name="start">The zero-based index of the first row to remove.</param>
    /// <param name="count">The number of rows to remove.</param>
    public virtual void RemoveRows(long start, long count)
    {
        long rows = RowCount;
        if (start < 0 || count < 0 || start > rows || count > rows - start) throw new ArgumentOutOfRangeException(nameof(start));
        var columns = ReadColumns();
        foreach (var c in columns)
        {
            long per = ItemsPerRow(c);
            c.Data = BinaryTableCodec.Concat(
                BinaryTableCodec.SliceRows(c.Data!, per, 0, start),
                BinaryTableCodec.SliceRows(c.Data!, per, start + count, rows - start - count));
        }
        SetColumnsCore(columns, rows - count);
    }

    /// <summary>
    /// Appends the rows of <paramref name="rows"/>, which must have the same columns as this table: same TFORM (ignoring the
    /// variable-length maximum), TSCAL, TZERO, TNULL and TDIM, and data of the same native type.
    /// </summary>
    /// <param name="rows">One column of data for each column of the table, in the same order, holding the rows to add.</param>
    public virtual void AppendRows(IReadOnlyList<TableColumn> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var columns = ReadColumns();
        if (rows.Count != columns.Count) throw new ArgumentException("The column count differs.", nameof(rows));
        for (int i = 0; i < columns.Count; i++)
        {
            var (have, add) = (columns[i], rows[i]);
            bool same = string.Equals(StripMax(have.Format), StripMax(add.Format), StringComparison.OrdinalIgnoreCase)
                && (have.Scale ?? 1.0) == (add.Scale ?? 1.0) && (have.Zero ?? 0.0) == (add.Zero ?? 0.0)
                && Equals(have.Null, add.Null)
                && (have.Dimensions ?? []).SequenceEqual(add.Dimensions ?? [])
                && add.Data is not null && add.Data.GetType() == have.Data!.GetType();
            if (!same)
                throw new ArgumentException($"Column {i} does not match the table schema (format, scaling, null value, dimensions or data type).", nameof(rows));
            if (add.Data!.Length % Math.Max(1, ItemsPerRow(have)) != 0)
                throw new ArgumentException($"Column {i} holds a partial row.", nameof(rows));
            have.Data = BinaryTableCodec.Concat(have.Data!, add.Data!);
        }
        SetColumnsCore(columns, null);
    }

    private static string StripMax(string format)
    {
        string f = format.Trim();
        int open = f.IndexOf('(');
        return open < 0 ? f : f[..open];
    }

    // ---- Structure -----------------------------------------------------------------------------------------------

    internal override void Normalize(bool primary, bool hasExtensions)
    {
        if (primary) throw new FitsException("A table cannot be the primary HDU.");
        var l = Layout;
        RebuildMandatory(Header, false, XtensionName, BitPix.Byte, l.Axes, l.ParameterCount, l.GroupCount, false, false);
        MoveTfields();
        ValidateColumns(Columns, l.Axes[0]);
    }

    /// <summary>TFIELDS must directly follow GCOUNT (position 8).</summary>
    private void MoveTfields()
    {
        var card = Header.Find("TFIELDS");
        if (card is null) return;
        var cards = Header.Where(c => !ReferenceEquals(c, card)).ToList();
        cards.Insert(Math.Min(7, cards.Count), card);
        Header.ReplaceAll(cards);
    }
}
