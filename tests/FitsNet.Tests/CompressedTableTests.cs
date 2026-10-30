using System.Numerics;
using FitsNet.Compression;

namespace FitsNet.Tests;

/// <summary>Tile-compressed binary tables (standard section 10.3).</summary>
public class CompressedTableTests
{
    private static byte[] Save(FitsFile f, ChecksumPolicy policy = ChecksumPolicy.Auto)
    {
        var ms = new MemoryStream();
        f.Save(ms, policy);
        return ms.ToArray();
    }

    private static FitsFile Reload(BinaryTableHdu hdu)
    {
        byte[] bytes = Save(new FitsFile().Add(hdu), ChecksumPolicy.Always);
        return FitsFile.Read(new MemoryStream(bytes), new FitsReadOptions(true));
    }

    private static BinaryTableHdu Sample(int rows)
    {
        var rng = new Random(11);
        var t = BinaryTableHdu.Create(
        [
            TableColumn.Create<byte>("U8", Enumerable.Range(0, rows * 2).Select(i => (byte)(i * 7)).ToArray(), repeat: 2),
            TableColumn.Create<short>("I16", Enumerable.Range(0, rows).Select(i => (short)(i * 3 - 100)).ToArray()),
            TableColumn.Create<int>("I32", Enumerable.Range(0, rows * 3).Select(_ => rng.Next(-1000, 1000)).ToArray(), repeat: 3),
            TableColumn.Create<long>("I64", Enumerable.Range(0, rows).Select(i => (long)i * 1_000_000_007L).ToArray()),
            TableColumn.Create<float>("F32", Enumerable.Range(0, rows).Select(_ => (float)rng.NextDouble()).ToArray(), unit: "Jy"),
            TableColumn.Create<double>("F64", Enumerable.Range(0, rows * 2).Select(_ => rng.NextDouble() * 1e6).ToArray(), repeat: 2),
            TableColumn.CreateLogical("FLAG", Enumerable.Range(0, rows).Select(i => i % 3 == 0 ? (bool?)null : i % 2 == 0).ToArray()),
            TableColumn.CreateBits("BITS", Enumerable.Range(0, rows * 12).Select(i => i % 5 == 0).ToArray(), 12),
            TableColumn.CreateString("NAME", Enumerable.Range(0, rows).Select(i => "src" + i).ToArray(), 8),
            TableColumn.CreateComplex("Z", Enumerable.Range(0, rows).Select(i => new Complex(i, -i / 2.0)).ToArray()),
            TableColumn.Create<ushort>("U16", Enumerable.Range(0, rows).Select(i => (ushort)(40000 + i)).ToArray()),
        ]);
        t.Header.Set("EXTNAME", "CATALOG", "name of the table").Set("OBSERVER", "somebody");
        t.Header.AddComment("kept as it is");
        return t;
    }

    private static void AssertSameTable(BinaryTableHdu expected, BinaryTableHdu actual)
    {
        Assert.Equal(expected.RowCount, actual.RowCount);
        Assert.Equal(expected.RowWidth, actual.RowWidth);
        Assert.Equal(expected.ColumnCount, actual.ColumnCount);
        var a = expected.ReadColumns();
        var b = actual.ReadColumns();
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Format, b[i].Format);
            Assert.Equal(a[i].Name, b[i].Name);
            Assert.Equal(a[i].Zero, b[i].Zero);
            Assert.Equal(a[i].Unit, b[i].Unit);
            Assert.Equal((IEnumerable<object?>)a[i].Data!.Cast<object?>(), b[i].Data!.Cast<object?>());
        }
    }

    // ---- fixed-length columns ------------------------------------------------------------------------------------------

    public static IEnumerable<object[]> Algorithms() =>
        new[] { (CompressionAlgorithm?)null, CompressionAlgorithm.Rice, CompressionAlgorithm.Gzip1, CompressionAlgorithm.Gzip2, CompressionAlgorithm.NoCompress }
            .Select(a => new object[] { a?.ToString() ?? "Auto" });

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void Every_column_type_survives_a_round_trip_with_every_algorithm(string name)
    {
        var original = Sample(1000);
        var options = new CompressedTableOptions { TileRows = 128 };
        if (name != "Auto") options.Algorithm = Enum.Parse<CompressionAlgorithm>(name);
        var compressed = CompressedTableHdu.Compress(original, options);

        Assert.Equal(1000, compressed.UncompressedRowCount);
        Assert.Equal(original.RowWidth, compressed.UncompressedRowWidth);
        Assert.Equal(128, compressed.TileRows);
        Assert.Equal(8, compressed.RowCount); // 1000 rows in tiles of 128
        Assert.Equal(original.ColumnCount, compressed.ColumnCount);
        Assert.All(compressed.Columns, c => Assert.Equal("1QB", c.Format));

        AssertSameTable(original, compressed.Decompress());
        AssertSameTable(original, compressed.Decompress(maxDegreeOfParallelism: 1));
    }

    [Fact]
    public void Automatic_choice_uses_rice_for_integers_gzip1_for_text_and_gzip2_for_floats()
    {
        var c = CompressedTableHdu.Compress(Sample(50));
        Assert.Equal(["RICE_1", "RICE_1", "RICE_1", "GZIP_2", "GZIP_2", "GZIP_2", "GZIP_1", "GZIP_1", "GZIP_1", "GZIP_2", "RICE_1"], c.ColumnAlgorithms);
    }

    [Fact]
    public void Rice_is_actually_smaller_than_the_original_for_smooth_integers()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("N", Enumerable.Range(0, 100_000).Select(i => i / 7).ToArray())]);
        var c = CompressedTableHdu.Compress(t, new CompressedTableOptions { Algorithm = CompressionAlgorithm.Rice });
        Assert.True(c.Source.Length < t.Source.Length / 3, $"{c.Source.Length} vs {t.Source.Length}");
    }

    [Fact]
    public void The_header_is_restored_card_for_card()
    {
        var original = Sample(300);
        original.Header.Set("TNULL3", 7).Set("TDIM3", "(3)");
        var compressed = CompressedTableHdu.Compress(original, new CompressedTableOptions { TileRows = 100 });
        var h = compressed.Header;
        Assert.True(h.Get<bool>("ZTABLE"));
        Assert.Equal(original.RowWidth, h.Get<long>("ZNAXIS1"));
        Assert.Equal(300L, h.Get<long>("ZNAXIS2"));
        Assert.Equal(100L, h.Get<long>("ZTILELEN"));
        Assert.Equal("3J", h.Get<string>("ZFORM3"));
        Assert.Equal("1QB", h.Get<string>("TFORM3"));
        Assert.Equal("CATALOG", h.Get<string>("EXTNAME")); // verbatim cards
        Assert.Equal("Jy", h.Get<string>("TUNIT5"));
        Assert.Equal("(3)", h.Get<string>("TDIM3"));
        Assert.Equal("TFIELDS", h[7].Keyword);

        var back = compressed.Decompress();
        Assert.Equal(original.Header.Select(c => c.Keyword), back.Header.Select(c => c.Keyword));
        foreach (var card in original.Header.Where(c => c.Kind == FitsCardKind.Value))
            Assert.Equal(card.Value, back.Header.Find(card.Keyword)!.Value);
        Assert.False(back.Header.Contains("ZTABLE"));
        Assert.False(back.Header.Contains("ZFORM1"));
    }

    [Fact]
    public void A_compressed_table_is_saved_and_read_back_as_a_compressed_table()
    {
        var original = Sample(500);
        var compressed = CompressedTableHdu.Compress(original, new CompressedTableOptions { TileRows = 64 });
        using var file = Reload(compressed);
        var back = Assert.IsType<CompressedTableHdu>(file[1]);
        var sums = back.VerifyChecksum();
        Assert.Equal(ChecksumStatus.Valid, sums.Checksum);
        Assert.Equal(ChecksumStatus.Valid, sums.DataSum);
        AssertSameTable(original, back.Decompress());

        // and unedited it is written back identically
        byte[] bytes = Save(file);
        using var again = FitsFile.Read(new MemoryStream(bytes));
        Assert.Equal(bytes, Save(again));
    }

    [Fact]
    public void A_compressed_table_works_from_a_lazily_opened_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-ztable-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            var original = Sample(400);
            new FitsFile().Add(CompressedTableHdu.Compress(original, new CompressedTableOptions { TileRows = 50 })).Save(path);
            using var f = FitsFile.Open(path);
            AssertSameTable(original, ((CompressedTableHdu)f[1]).Decompress());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parallel_and_sequential_compression_give_the_same_bytes()
    {
        var t = Sample(2000);
        byte[] one = CompressedTableHdu.Compress(t, new CompressedTableOptions { TileRows = 100, MaxDegreeOfParallelism = 1 }).Source.ReadAll();
        byte[] many = CompressedTableHdu.Compress(t, new CompressedTableOptions { TileRows = 100, MaxDegreeOfParallelism = 8 }).Source.ReadAll();
        Assert.Equal(one, many);
    }

    [Fact]
    public void A_single_row_a_single_tile_and_a_short_last_tile_all_work()
    {
        foreach (int rows in new[] { 1, 2, 7 })
            foreach (long tile in new long[] { 1, 2, 3, 100 })
                AssertSameTable(Sample(rows), CompressedTableHdu.Compress(Sample(rows), new CompressedTableOptions { TileRows = tile }).Decompress());
    }

    [Fact]
    public void The_fz_directives_of_the_header_are_obeyed_and_not_copied()
    {
        var t = Sample(100);
        t.Header.Set("FZTILELN", 25).Set("FZALGOR", "GZIP_1").Set("FZALG2", "NOCOMPRESS");
        var c = CompressedTableHdu.Compress(t);
        Assert.Equal(25, c.TileRows);
        Assert.Equal("GZIP_1", c.ColumnAlgorithms[0]);
        Assert.Equal("NOCOMPRESS", c.ColumnAlgorithms[1]);
        Assert.False(c.Header.Contains("FZALGOR") || c.Header.Contains("FZTILELN") || c.Header.Contains("FZALG2"));
        AssertSameTable(t, c.Decompress());
    }

    [Fact]
    public void Options_override_directives_and_bad_requests_are_refused()
    {
        var t = Sample(10);
        Assert.Throws<ArgumentException>(() => CompressedTableHdu.Compress(t, new CompressedTableOptions { Algorithm = CompressionAlgorithm.Plio }));
        Assert.Throws<ArgumentException>(() => CompressedTableHdu.Compress(t, new CompressedTableOptions { Algorithm = CompressionAlgorithm.HCompress }));
        var perColumn = new CompressedTableOptions();
        perColumn.ColumnAlgorithms[4] = CompressionAlgorithm.Rice; // F32
        Assert.Throws<ArgumentException>(() => CompressedTableHdu.Compress(t, perColumn));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompressedTableHdu.Compress(t, new CompressedTableOptions { TileRows = -1 }));
        Assert.Throws<ArgumentException>(() => CompressedTableHdu.Compress(CompressedTableHdu.Compress(t)));
        Assert.Throws<ArgumentNullException>(() => CompressedTableHdu.Compress(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompressedTableHdu.Compress(t).Decompress(0));

        var ok = new CompressedTableOptions { Algorithm = CompressionAlgorithm.Rice };
        ok.ColumnAlgorithms[0] = CompressionAlgorithm.Gzip1;
        var c = CompressedTableHdu.Compress(t, ok);
        Assert.Equal("GZIP_1", c.ColumnAlgorithms[0]);
        Assert.Equal("RICE_1", c.ColumnAlgorithms[1]);
        Assert.Equal("GZIP_2", c.ColumnAlgorithms[4]); // Rice for all falls back where it cannot apply
    }

    [Fact]
    public void An_empty_table_and_rows_with_spare_bytes_are_refused_instead_of_losing_data()
    {
        Assert.Throws<ArgumentException>(() => CompressedTableHdu.Compress(BinaryTableHdu.Create([TableColumn.Create<int>("A", [])])));
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2])]);
        t.Header.Set("NAXIS1", 6L); // two spare bytes in every row
        t.Source = new MemoryDataSource(new byte[12]);
        Assert.Throws<NotSupportedException>(() => CompressedTableHdu.Compress(t));
    }

    [Fact]
    public void The_stored_table_cannot_be_edited_as_a_table()
    {
        var c = CompressedTableHdu.Compress(Sample(10));
        Assert.Throws<NotSupportedException>(() => c.SetColumns([]));
        Assert.Throws<NotSupportedException>(() => c.AddColumn(TableColumn.Create<int>("X", new int[1])));
        Assert.Throws<NotSupportedException>(() => c.RemoveColumn(0));
        Assert.Throws<NotSupportedException>(() => c.RemoveRows(0, 1));
        Assert.Throws<NotSupportedException>(() => c.AppendRows([]));
        Assert.Throws<NotSupportedException>(() => CompressedTableHdu.Create([]));
    }

    // ---- variable-length columns ---------------------------------------------------------------------------------------

    private static BinaryTableHdu WithVariableColumns(int rows, bool wide)
    {
        var rng = new Random(5);
        short[][] shorts = Enumerable.Range(0, rows).Select(i => i % 4 == 0 ? [] : Enumerable.Range(0, rng.Next(1, 40)).Select(k => (short)(k * 2 + i)).ToArray()).ToArray();
        double[][] doubles = Enumerable.Range(0, rows).Select(_ => Enumerable.Range(0, rng.Next(0, 10)).Select(_ => rng.NextDouble()).ToArray()).ToArray();
        byte[][] bytes = Enumerable.Range(0, rows).Select(i => Enumerable.Range(0, i % 17).Select(k => (byte)(k + i)).ToArray()).ToArray();
        return BinaryTableHdu.Create(
        [
            TableColumn.Create<int>("ID", Enumerable.Range(0, rows).ToArray()),
            TableColumn.CreateVariable("SPECTRUM", shorts, wide),
            TableColumn.CreateVariable("TIMES", doubles, wide),
            TableColumn.CreateVariable("RAW", bytes, wide),
            TableColumn.CreateVariableString("COMMENT", Enumerable.Range(0, rows).Select(i => new string('x', i % 9)).ToArray(), wide),
        ]);
    }

    private static void AssertSameVariableTable(BinaryTableHdu expected, BinaryTableHdu actual)
    {
        Assert.Equal(expected.ReadColumn<int>("ID"), actual.ReadColumn<int>("ID"));
        Assert.Equal(expected.ReadVariableColumn<short>("SPECTRUM"), actual.ReadVariableColumn<short>("SPECTRUM"));
        Assert.Equal(expected.ReadVariableColumn<double>("TIMES"), actual.ReadVariableColumn<double>("TIMES"));
        Assert.Equal(expected.ReadVariableColumn<byte>("RAW"), actual.ReadVariableColumn<byte>("RAW"));
        Assert.Equal(expected.ReadStringColumn("COMMENT"), actual.ReadStringColumn("COMMENT"));
    }

    [Theory]
    [InlineData(false, "Auto")]
    [InlineData(true, "Auto")]
    [InlineData(false, "Gzip1")]
    [InlineData(true, "Gzip2")]
    [InlineData(false, "Rice")]
    [InlineData(true, "NoCompress")]
    public void Variable_length_columns_with_p_and_q_descriptors_round_trip(bool wide, string algorithm)
    {
        var original = WithVariableColumns(300, wide);
        var options = new CompressedTableOptions { TileRows = 40 };
        if (algorithm != "Auto") options.Algorithm = Enum.Parse<CompressionAlgorithm>(algorithm);
        var compressed = CompressedTableHdu.Compress(original, options);
        var restored = compressed.Decompress();
        AssertSameVariableTable(original, restored);
        Assert.Equal(original.Header.Get<long>("PCOUNT"), restored.Header.Get<long>("PCOUNT"));

        using var file = Reload(compressed);
        AssertSameVariableTable(original, ((CompressedTableHdu)file[1]).Decompress());
    }

    [Fact]
    public void The_heap_layout_of_the_original_including_a_gap_is_reproduced()
    {
        var original = WithVariableColumns(60, wide: false);
        // move the heap 24 bytes further and shift every descriptor-independent byte: arrays keep their offsets from the heap start
        long main = original.RowCount * original.RowWidth;
        byte[] data = original.Source.ReadAll();
        var shifted = new byte[data.Length + 24];
        Array.Copy(data, 0, shifted, 0, main);
        Array.Copy(data, main, shifted, main + 24, data.Length - main);
        original.Source = new MemoryDataSource(shifted);
        original.Header.Set("PCOUNT", (long)shifted.Length - main).Set("THEAP", main + 24);

        var compressed = CompressedTableHdu.Compress(original, new CompressedTableOptions { TileRows = 7 });
        Assert.Equal(main + 24, compressed.Header.Get<long>("ZTHEAP"));
        var restored = compressed.Decompress();
        Assert.Equal(main + 24, restored.Header.Get<long>("THEAP"));
        AssertSameVariableTable(original, restored);
        Assert.Equal(shifted, restored.Source.ReadAll());
    }

    [Fact]
    public void The_original_checksum_keywords_go_to_zhecksum_and_zdatasum()
    {
        var t = Sample(20);
        var file = new FitsFile().Add(t);
        using var stored = FitsFile.Read(new MemoryStream(Save(file, ChecksumPolicy.Always)));
        var withSums = (BinaryTableHdu)stored[1];
        var c = CompressedTableHdu.Compress(withSums);
        Assert.Equal(withSums.Header.Get<string>("CHECKSUM"), c.Header.Get<string>("ZHECKSUM"));
        Assert.Equal(withSums.Header.Get<string>("DATASUM"), c.Header.Get<string>("ZDATASUM"));
        Assert.False(c.Header.Contains("CHECKSUM"));
        var back = c.Decompress();
        Assert.False(back.Header.Contains("ZHECKSUM") || back.Header.Contains("CHECKSUM"));
    }

    // ---- hostile input -------------------------------------------------------------------------------------------------

    private static CompressedTableHdu Fresh() => CompressedTableHdu.Compress(WithVariableColumns(40, wide: false), new CompressedTableOptions { TileRows = 8 });

    [Theory]
    [InlineData("ZNAXIS1", 5)]
    [InlineData("ZNAXIS1", -1)]
    [InlineData("ZNAXIS2", 41)]
    [InlineData("ZNAXIS2", -3)]
    [InlineData("ZTILELEN", 0)]
    [InlineData("ZTILELEN", 3)]
    [InlineData("ZPCOUNT", 1)]
    [InlineData("ZPCOUNT", -1)]
    [InlineData("ZPCOUNT", 2_000_000_000)]
    [InlineData("ZNAXIS1", 2_000_000_000)]
    [InlineData("ZTHEAP", 1)]
    public void Damaged_structure_keywords_are_reported_as_fits_errors(string key, long value)
    {
        var c = Fresh();
        c.Header.Set(key, value);
        Assert.ThrowsAny<FitsException>(() => c.Decompress());
    }

    [Theory]
    [InlineData("ZFORM2", "1Z")]
    [InlineData("ZFORM1", "999999999999J")]
    [InlineData("ZCTYP1", "HCOMPRESS_1")]
    [InlineData("ZCTYP1", "PLIO_1")]
    [InlineData("ZCTYP5", "RICE_1")] // Rice on a character column
    [InlineData("TFORM1", "1J")]
    [InlineData("TFORM1", "1QJ")]
    public void Damaged_column_keywords_are_reported_as_fits_errors(string key, string value)
    {
        var c = Fresh();
        c.Header.Set(key, value);
        Assert.ThrowsAny<FitsException>(() => c.Decompress());
    }

    [Fact]
    public void A_missing_keyword_is_reported()
    {
        foreach (string key in new[] { "ZNAXIS1", "ZNAXIS2", "ZTILELEN", "ZFORM1", "ZCTYP1" })
        {
            var c = Fresh();
            c.Header.Remove(key);
            Assert.ThrowsAny<FitsException>(() => c.Decompress());
        }
    }

    [Fact]
    public void Corrupted_data_never_crashes_it_is_always_a_fits_error_or_a_table()
    {
        byte[] good = Fresh().Source.ReadAll();
        var rng = new Random(2024);
        int errors = 0;
        for (int round = 0; round < 3000; round++)
        {
            byte[] bad = (byte[])good.Clone();
            int flips = rng.Next(1, 6);
            for (int k = 0; k < flips; k++) bad[rng.Next(bad.Length)] = (byte)rng.Next(256);
            if (round % 7 == 0) Array.Resize(ref bad, rng.Next(0, bad.Length));
            var c = Fresh();
            c.Source = new MemoryDataSource(bad);
            try
            {
                c.Decompress();
            }
            catch (FitsException)
            {
                errors++;
            }
        }
        Assert.True(errors > 100, $"only {errors} errors");
    }

    [Fact]
    public void A_forged_huge_descriptor_does_not_allocate_or_read_out_of_bounds()
    {
        var c = Fresh();
        byte[] data = c.Source.ReadAll();
        // first descriptor of the first row: count = 2^40
        data[0] = 0; data[1] = 0; data[2] = 1; data[3] = 0; data[4] = 0; data[5] = 0; data[6] = 0; data[7] = 0;
        c.Source = new MemoryDataSource(data);
        Assert.Throws<FitsException>(() => c.Decompress());
    }

    // ---- findings of the review --------------------------------------------------------------------------------------

    [Fact]
    public void Descriptors_that_all_point_at_one_big_array_are_refused_quickly()
    {
        const int rows = 2000;
        var source = BinaryTableHdu.Create([TableColumn.CreateVariable<byte>("V", Enumerable.Range(0, rows).Select(_ => new byte[] { 1 }).ToArray())]);
        var c = CompressedTableHdu.Compress(source, new CompressedTableOptions { TileRows = rows });
        Assert.Equal(1, c.RowCount);

        // heap: one gzip stream that inflates to 1 MB, then the combined descriptors: every row uses it as an array of 1 000 000 bytes
        byte[] big = FitsNet.Compression.GzipCodec.Compress(new byte[1_000_000]);
        var combined = new byte[rows * (16 + 8)];
        for (int r = 0; r < rows; r++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(combined.AsSpan(r * 16), big.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(combined.AsSpan(rows * 16 + r * 8), 1_000_000);
        }
        byte[] gz = FitsNet.Compression.GzipCodec.Compress(combined);
        var data = new byte[16 + big.Length + gz.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(data, gz.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(8), big.Length);
        big.CopyTo(data, 16);
        gz.CopyTo(data, 16 + big.Length);
        c.Source = new MemoryDataSource(data);
        c.Header.Set("PCOUNT", (long)(data.Length - 16)).Set("ZPCOUNT", 1_000_000L).Set("ZFORM1", "1PB");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<FitsException>(() => c.Decompress());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public void A_tile_length_near_the_long_limit_does_not_overflow_the_tile_count()
    {
        var c = Fresh();
        c.Header.Set("ZTILELEN", long.MaxValue);
        Assert.Throws<FitsException>(() => c.Decompress());
    }

    [Fact]
    public void Directives_this_table_cannot_follow_are_ignored()
    {
        var t = Sample(20);
        t.Header.Set("FZALGOR", "PLIO_1").Set("FZALG2", "who_knows");
        var c = CompressedTableHdu.Compress(t);
        Assert.Equal("RICE_1", c.ColumnAlgorithms[0]); // automatic choice
        AssertSameTable(t, c.Decompress());
        // and an option wins over a directive that would not even be understood
        c = CompressedTableHdu.Compress(t, new CompressedTableOptions { Algorithm = CompressionAlgorithm.Gzip1 });
        Assert.All(c.ColumnAlgorithms, a => Assert.Equal("GZIP_1", a));
    }

    [Fact]
    public void A_column_index_outside_the_table_is_reported()
    {
        var o = new CompressedTableOptions();
        o.ColumnAlgorithms[99] = CompressionAlgorithm.Gzip1;
        Assert.Throws<ArgumentException>(() => CompressedTableHdu.Compress(Sample(3), o));
    }

    [Fact]
    public void Data_that_a_compressed_table_cannot_keep_is_refused_instead_of_lost()
    {
        var original = WithVariableColumns(20, wide: false);
        long main = original.RowCount * original.RowWidth;
        byte[] data = original.Source.ReadAll();

        // a gap before the heap that holds data
        var gap = new byte[data.Length + 8];
        Array.Copy(data, 0, gap, 0, main);
        Array.Fill(gap, (byte)0xFF, (int)main, 8);
        Array.Copy(data, main, gap, main + 8, data.Length - main);
        original.Source = new MemoryDataSource(gap);
        original.Header.Set("PCOUNT", (long)gap.Length - main).Set("THEAP", main + 8);
        Assert.Throws<NotSupportedException>(() => CompressedTableHdu.Compress(original));

        // a heap byte nobody points at
        var original2 = WithVariableColumns(20, wide: false);
        byte[] extra = original2.Source.ReadAll().Concat(new byte[] { 9 }).ToArray();
        original2.Source = new MemoryDataSource(extra);
        original2.Header.Set("PCOUNT", original2.Header.Get<long>("PCOUNT") + 1);
        Assert.Throws<NotSupportedException>(() => CompressedTableHdu.Compress(original2));

        // ... but unused zero bytes are harmless: they come back as zeros
        var original3 = WithVariableColumns(20, wide: false);
        original3.Source = new MemoryDataSource(original3.Source.ReadAll().Concat(new byte[] { 0, 0 }).ToArray());
        original3.Header.Set("PCOUNT", original3.Header.Get<long>("PCOUNT") + 2);
        Assert.Equal(original3.Source.ReadAll(), CompressedTableHdu.Compress(original3).Decompress().Source.ReadAll());
    }

    [Fact]
    public void Column_keywords_are_copied_verbatim_and_describe_the_uncompressed_values()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<short>("A", [1, 2, 3, 4, 5, 6], repeat: 6, dimensions: [3, 2])]);
        t.Header.Set("TNULL1", -1).Set("TSCAL1", 0.5).Set("TZERO1", 10.0).Set("TDISP1", "I4");
        var c = CompressedTableHdu.Compress(t);
        var h = c.Header;
        Assert.Equal("(3,2)", h.Get<string>("TDIM1"));
        Assert.Equal(-1L, h.Get<long>("TNULL1"));
        Assert.Equal(0.5, h.Get<double>("TSCAL1"));
        Assert.Equal(10.0, h.Get<double>("TZERO1"));
        Assert.Equal("I4", h.Get<string>("TDISP1"));
        var back = c.Decompress();
        Assert.Equal(new long[] { 3, 2 }, back.Columns[0].Dimensions);
        Assert.Equal(new double[] { 10.5, 11, 11.5, 12, 12.5, 13 }, back.ReadColumn<double>(0));
    }
}
