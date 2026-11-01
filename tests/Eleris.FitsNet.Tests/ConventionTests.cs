using System.Text;
using Eleris.FitsNet.Wcs;

namespace Eleris.FitsNet.Tests;

/// <summary>Conforming extensions (Appendix F), header inheritance (K), the Green Bank convention (L), MIME types (G), Stokes values (8.5).</summary>
public class ConventionTests
{
    private static byte[] Save(FitsFile f)
    {
        var ms = new MemoryStream();
        f.Save(ms);
        return ms.ToArray();
    }

    private static FitsFile Reload(byte[] bytes) => FitsFile.Read(new MemoryStream(bytes), new FitsReadOptions(true));

    // ---- Appendix F ------------------------------------------------------------------------------------------------

    [Fact]
    public void An_iueimage_extension_is_an_image_and_keeps_its_name()
    {
        var image = ImageHdu.Create<short>([1, 2, 3, 4], 2, 2);
        var file = new FitsFile().Add(ImageHdu.Create<byte>([0], 1)).Add(image);
        image.Header.Set("XTENSION", "IUEIMAGE");
        byte[] bytes = Save(file);
        Assert.Contains("XTENSION= 'IUEIMAGE'", Encoding.ASCII.GetString(bytes));

        using var back = Reload(bytes);
        var hdu = Assert.IsType<ImageHdu>(back[1]);
        Assert.Equal(new short[] { 1, 2, 3, 4 }, hdu.ReadPixels<short>());
        Assert.Equal("IUEIMAGE", hdu.Header.Get<string>("XTENSION"));
        Assert.Equal(bytes, Save(back));
        hdu.SetPixels<short>([9, 8, 7, 6], 2, 2);                       // editing keeps the name
        Assert.Equal("IUEIMAGE", hdu.Header.Get<string>("XTENSION"));
    }

    [Fact]
    public void An_a3dtable_extension_is_a_binary_table_and_keeps_its_name()
    {
        var table = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2, 3]), TableColumn.Create<double>("B", [0.5, 1.5, 2.5])]);
        table.Header.Set("XTENSION", "A3DTABLE");
        byte[] bytes = Save(new FitsFile().Add(table));
        Assert.Contains("XTENSION= 'A3DTABLE'", Encoding.ASCII.GetString(bytes));

        using var back = Reload(bytes);
        var t = Assert.IsType<BinaryTableHdu>(back[1]);
        Assert.Equal(new[] { 1, 2, 3 }, t.ReadColumn<int>("A"));
        Assert.Equal(new[] { 0.5, 1.5, 2.5 }, t.ReadColumn<double>("B"));
        Assert.Equal(bytes, Save(back));
        t.AppendRows([TableColumn.Create<int>("A", [4]), TableColumn.Create<double>("B", [3.5])]);
        Assert.Equal("A3DTABLE", t.Header.Get<string>("XTENSION"));
        Assert.Equal(4, t.RowCount);
    }

    [Theory]
    [InlineData("FOREIGN ")]
    [InlineData("DUMP    ")]
    public void Foreign_and_dump_extensions_are_kept_byte_for_byte(string name)
    {
        byte[] bytes = Save(new FitsFile().Add(ImageHdu.Create<byte>([0], 1)).Add(ImageHdu.Create<byte>("hello"u8.ToArray(), 5)));
        string text = Encoding.ASCII.GetString(bytes);
        int at = text.IndexOf("XTENSION= 'IMAGE   '", StringComparison.Ordinal);
        Assert.True(at > 0);
        Encoding.ASCII.GetBytes("XTENSION= '" + name + "'").CopyTo(bytes, at);

        using var f = Reload(bytes);
        var generic = Assert.IsType<GenericHdu>(f[1]);
        Assert.Equal("hello"u8.ToArray(), generic.ReadRawData());
        Assert.Equal(bytes, Save(f));
    }

    // ---- Appendix K ------------------------------------------------------------------------------------------------

    [Fact]
    public void Inheriting_extensions_see_the_primary_keywords_with_their_own_taking_precedence()
    {
        var primary = ImageHdu.Create<byte>([0], 1);
        primary.Header.Set("TELESCOP", "HST").Set("OBSERVER", "primary").Set("BSCALE", 1.0).AddComment("not inherited").AddHistory("not inherited");
        var inheriting = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1])]);
        inheriting.Header.Set("INHERIT", true).Set("OBSERVER", "extension");
        var isolated = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1])]);
        isolated.Header.Set("INHERIT", false);
        var plain = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1])]);
        using var file = Reload(Save(new FitsFile().Add(primary).Add(inheriting).Add(isolated).Add(plain)));

        var h = file.GetEffectiveHeader(1);
        Assert.Equal("HST", h.Get<string>("TELESCOP"));
        Assert.Equal("extension", h.Get<string>("OBSERVER"));
        Assert.Equal(1.0, h.Get<double>("BSCALE"));
        Assert.Equal("BINTABLE", h.Get<string>("XTENSION"));
        Assert.Equal(8, h.Get<int>("BITPIX"));
        Assert.Equal(1L, h.Get<long>("NAXIS1") / 4);
        Assert.False(h.Contains("SIMPLE"));
        Assert.DoesNotContain(h, c => c.Kind == FitsCardKind.Commentary);                      // commentary is not inherited
        Assert.False(file.GetEffectiveHeader(2).Contains("TELESCOP"));   // INHERIT = F
        Assert.False(file.GetEffectiveHeader(3).Contains("TELESCOP"));   // no INHERIT
        Assert.Equal("primary", file.GetEffectiveHeader(0).Get<string>("OBSERVER"));
        // the HDU itself is untouched
        Assert.False(file[1].Header.Contains("TELESCOP"));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.GetEffectiveHeader(9));
    }

    // ---- Appendix L ------------------------------------------------------------------------------------------------

    private static BinaryTableHdu SdTable() => BinaryTableHdu.Create(
    [
        TableColumn.Create<double>("FLUX", [1.0, 2.0, 3.0]),
        TableColumn.Create<double>("TSYS", [50.5, 50.5, 50.5]),
        TableColumn.Create<int>("SCAN", [7, 7, 7]),
        TableColumn.CreateString("OBJECT", ["M31", "M31", "M31"], 8),
        TableColumn.CreateLogical("CAL", [true, true, true]),
        TableColumn.Create<int>("MIXED", [1, 2, 1]),
        TableColumn.Create<float>("VEC", [1f, 2f, 1f, 2f, 1f, 2f], repeat: 2),
    ]);

    [Fact]
    public void The_column_takes_precedence_over_the_keyword_of_the_same_name()
    {
        var t = SdTable();
        t.Header.Set("TSYS", 1.0).Set("HUMIDITY", 0.35).Set("OBSERVER", "me");
        Assert.Equal(2.0, t.GetValue("FLUX", 1));
        Assert.Equal(50.5, t.GetValue("TSYS", 2));       // the column, not the keyword
        Assert.Equal(0.35, t.GetValue("HUMIDITY", 0));   // the keyword stands for a constant column
        Assert.Equal("me", t.GetValue("OBSERVER", 2));
        Assert.Equal(new float[] { 1f, 2f }, (float[])t.GetValue("VEC", 1)!);
        Assert.Null(t.GetValue("NOPE", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.GetValue("FLUX", 3));
    }

    [Fact]
    public void Constant_columns_collapse_into_keywords_and_back()
    {
        var t = SdTable();
        var collapsed = t.CollapseConstantColumns();
        Assert.Equal(["TSYS", "SCAN", "OBJECT", "CAL"], collapsed);
        Assert.Equal(["FLUX", "MIXED", "VEC"], t.Columns.Select(c => c.Name!));
        Assert.Equal(50.5, t.Header.Get<double>("TSYS"));
        Assert.Equal(7, t.Header.Get<int>("SCAN"));
        Assert.Equal("M31", t.Header.Get<string>("OBJECT"));
        Assert.True(t.Header.Get<bool>("CAL"));
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, t.ReadColumn<double>("FLUX"));
        Assert.Equal(3, t.RowCount);
        Assert.Equal(50.5, t.GetValue("TSYS", 1));      // reads the same through the convention

        t.ExpandKeyword("OBJECT");
        t.ExpandKeyword("SCAN");
        Assert.False(t.Header.Contains("OBJECT"));
        Assert.Equal(["FLUX", "MIXED", "VEC", "OBJECT", "SCAN"], t.Columns.Select(c => c.Name!));
        Assert.Equal(["M31", "M31", "M31"], t.ReadStringColumn("OBJECT"));
        Assert.Equal(new[] { 7, 7, 7 }, t.ReadColumn<int>("SCAN"));
        Assert.Empty(t.CollapseConstantColumns().Except(["OBJECT", "SCAN"]));
    }

    [Fact]
    public void Collapsing_survives_a_save_and_never_removes_the_last_column()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("ONLY", [5, 5])]);
        Assert.Empty(t.CollapseConstantColumns());
        Assert.Equal(1, t.ColumnCount);
        var u = BinaryTableHdu.Create([TableColumn.Create<int>("A", [5, 5]), TableColumn.Create<int>("B", [6, 6])]);
        Assert.Single(u.CollapseConstantColumns());
        Assert.Equal(1, u.ColumnCount);
        using var back = Reload(Save(new FitsFile().Add(u)));
        Assert.Equal(5, ((BinaryTableHdu)back[1]).Header.Get<int>("A"));
    }

    [Fact]
    public void Structural_keywords_and_unsuitable_columns_are_not_converted()
    {
        Assert.False(GreenBankConvention.IsExpandable("NAXIS1"));
        Assert.False(GreenBankConvention.IsExpandable("TTYPE3"));
        Assert.False(GreenBankConvention.IsExpandable("EXTNAME"));
        Assert.False(GreenBankConvention.IsExpandable("HISTORY"));
        Assert.False(GreenBankConvention.IsExpandable("lower"));
        Assert.False(GreenBankConvention.IsExpandable("TOOLONGNAME"));
        Assert.True(GreenBankConvention.IsExpandable("DATE-OBS"));
        Assert.True(GreenBankConvention.IsExpandable("TSYS"));
        Assert.True(GreenBankConvention.IsExpandable("TTYPES")); // not TTYPEn
        var t = SdTable();
        Assert.Throws<ArgumentException>(() => t.ExpandKeyword("EXTNAME"));
        Assert.Throws<ArgumentException>(() => t.ExpandKeyword("ABSENT"));
        t.Header.Set("FLUX", 3.0);
        Assert.Throws<ArgumentException>(() => t.ExpandKeyword("FLUX"));             // a column of that name exists

        var scaled = BinaryTableHdu.Create([TableColumn.Create<short>("S", [1, 1]), TableColumn.Create<int>("K", [1, 2])]);
        scaled.Header.Set("TSCAL1", 2.0);
        Assert.Empty(scaled.CollapseConstantColumns());                                // scaled values are not plain constants
    }

    [Fact]
    public void Long_integers_and_strings_expand_to_the_right_column_types()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2])]);
        t.Header.Set("BIG", 5_000_000_000L).Set("SMALL", 3L).Set("NAME", "abc").Set("FLAG", false).Set("X", 1.5);
        foreach (string k in new[] { "BIG", "SMALL", "NAME", "FLAG", "X" }) t.ExpandKeyword(k);
        Assert.Equal(new[] { 5_000_000_000L, 5_000_000_000L }, t.ReadColumn<long>("BIG"));
        Assert.Equal(new[] { 3, 3 }, t.ReadColumn<int>("SMALL"));
        Assert.Equal(["abc", "abc"], t.ReadStringColumn("NAME"));
        Assert.Equal(new bool?[] { false, false }, t.ReadLogicalColumn(t.IndexOf("FLAG")));
        Assert.Equal(new[] { 1.5, 1.5 }, t.ReadColumn<double>("X"));
    }

    // ---- Appendix G and section 8.5 -----------------------------------------------------------------------------------

    [Fact]
    public void Mime_types_are_the_registered_ones()
    {
        Assert.Equal("application/fits", FitsMimeTypes.Application);
        Assert.Equal("image/fits", FitsMimeTypes.Image);
        Assert.Contains(".fits", FitsMimeTypes.FileExtensions);
    }

    [Fact]
    public void Stokes_values_follow_table_29()
    {
        Assert.Equal("I", StokesParameters.Symbol(1));
        Assert.Equal("V", StokesParameters.Symbol(4.0));
        Assert.Equal("RR", StokesParameters.Symbol(-1));
        Assert.Equal("YX", StokesParameters.Symbol(-8));
        Assert.Null(StokesParameters.Symbol(0));
        Assert.Null(StokesParameters.Symbol(5));
        Assert.Null(StokesParameters.Symbol(1.5));
        Assert.Equal(-5, StokesParameters.Code("XX"));
        Assert.Equal(2, StokesParameters.Code("Q"));
        Assert.Null(StokesParameters.Code("Z"));
        Assert.Equal("Standard Stokes linear", StokesParameters.Description("U"));
        Assert.Throws<ArgumentException>(() => StokesParameters.Description("Z"));
        for (int code = -8; code <= 4; code++)
            if (code != 0) Assert.Equal(code, StokesParameters.Code(StokesParameters.Symbol(code)!));
    }

    [Fact]
    public void Stokes_and_complex_axes_are_recognised()
    {
        var h = new FitsHeader();
        h.Set("CTYPE1", "COMPLEX").Set("CRPIX1", 0.0).Set("CRVAL1", 0.0).Set("CDELT1", 1.0)
            .Set("CTYPE2", "STOKES").Set("CRPIX2", 1.0).Set("CRVAL2", 1.0).Set("CDELT2", 1.0).Set("WCSAXES", 2);
        var wcs = WorldCoordinateSystem.FromHeader(h);
        Assert.Equal([WorldAxisKind.Complex, WorldAxisKind.Stokes], wcs.AxisKinds);
        Assert.Equal(1.0, wcs.PixelToWorld([1.0, 1.0])[0]);   // real part has coordinate 1
        Assert.Equal(2.0, wcs.PixelToWorld([2.0, 1.0])[0]);   // imaginary part has coordinate 2
        Assert.Equal("Q", StokesParameters.Symbol(wcs.PixelToWorld([1.0, 2.0])[1]));
    }

    // ---- findings of the review --------------------------------------------------------------------------------------

    [Fact]
    public void Collapsing_leaves_alone_columns_that_would_lose_something()
    {
        var t = BinaryTableHdu.Create(
        [
            TableColumn.Create<int>("A", [1, 1]),
            TableColumn.Create<int>("A", [2, 2]),                       // two columns of one name cannot share a keyword
            TableColumn.Create<double>("TEMP", [300.0, 300.0], unit: "K"),   // the unit would be lost
            TableColumn.Create<int>("B", [5, 5]),
            TableColumn.Create<int>("C", [1, 2]),
        ]);
        Assert.Equal(["B"], t.CollapseConstantColumns());
        Assert.Equal(["A", "A", "TEMP", "C"], t.Columns.Select(c => c.Name!));
        Assert.Equal("K", t.Columns[2].Unit);
        Assert.Equal(new[] { 1, 1 }, t.ReadColumn<int>(0));
        Assert.Equal(new[] { 2, 2 }, t.ReadColumn<int>(1));
        Assert.Equal(5, t.Header.Get<int>("B"));
    }

    [Fact]
    public void Keywords_that_cannot_appear_in_a_binary_table_are_not_converted()
    {
        foreach (string key in new[] { "SIMPLE", "EXTEND", "GROUPS", "BSCALE", "BZERO", "BLANK", "INHERIT", "CHECKSUM", "DATASUM", "TBCOL1" })
            Assert.False(GreenBankConvention.IsExpandable(key), key);
        var t = BinaryTableHdu.Create(
        [
            TableColumn.Create<double>("BSCALE", [2.0, 2.0]),
            TableColumn.Create<int>("SIMPLE", [1, 1]),
            TableColumn.Create<int>("X", [1, 2]),
        ]);
        Assert.Empty(t.CollapseConstantColumns());
        Assert.Equal(3, t.ColumnCount);
        Assert.False(t.Header.Contains("SIMPLE"));
    }

    [Fact]
    public void The_effective_header_does_not_inherit_what_identifies_the_primary_hdu()
    {
        var primary = ImageHdu.Create<byte>([0], 1);
        primary.Header.Set("EXTNAME", "PRIM").Set("EXTVER", 3).Set("EXTLEVEL", 1).Set("TELESCOP", "HST");
        var ext = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1])]);
        ext.Header.Set("INHERIT", true);
        var ms = new MemoryStream();
        new FitsFile().Add(primary).Add(ext).Save(ms, ChecksumPolicy.Always);   // CHECKSUM and DATASUM are written in the primary header
        using var file = FitsFile.Read(new MemoryStream(ms.ToArray()));
        Assert.True(file[0].Header.Contains("CHECKSUM"));
        var h = file.GetEffectiveHeader(1);
        Assert.Equal("HST", h.Get<string>("TELESCOP"));
        Assert.Equal(file[1].Header.Get<string>("CHECKSUM"), h.Get<string>("CHECKSUM"));   // its own, not the primary one
        foreach (string key in new[] { "EXTNAME", "EXTVER", "EXTLEVEL" })
            Assert.False(h.Contains(key), key);
    }

    [Fact]
    public void All_the_values_of_a_keyword_or_column_come_in_one_read()
    {
        var t = SdTable();
        t.Header.Set("HUMIDITY", 0.35);
        Assert.Equal(new object?[] { 1.0, 2.0, 3.0 }, t.GetValues("FLUX"));
        Assert.Equal(new object?[] { 0.35, 0.35, 0.35 }, t.GetValues("HUMIDITY"));
        Assert.Equal(3, t.GetValues("VEC")!.Length);
        Assert.Equal(new float[] { 1f, 2f }, (float[])t.GetValues("VEC")![1]!);
        Assert.Null(t.GetValues("ABSENT"));
        for (int row = 0; row < 3; row++) Assert.Equal(t.GetValue("TSYS", row), t.GetValues("TSYS")![row]);
    }

    [Fact]
    public void A_table_that_claims_too_many_rows_is_not_expanded()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2])]);
        t.Header.Set("EXTRA", 1).Set("NAXIS2", 2_000_000_000L);
        Assert.Throws<NotSupportedException>(() => t.ExpandKeyword("EXTRA"));
        t.Header.Set("NAXIS2", 1_000_000L);
        Assert.Throws<FitsException>(() => t.ExpandKeyword("EXTRA"));   // the data does not hold that many rows
    }

    [Fact]
    public void Stokes_helpers_agree_on_spaces_and_null()
    {
        Assert.Equal("Standard Stokes unpolarized", StokesParameters.Description(" I"));
        Assert.Equal(2, StokesParameters.Code(" Q "));
        Assert.Throws<ArgumentNullException>(() => StokesParameters.Description(null!));
        Assert.Throws<ArgumentNullException>(() => StokesParameters.Code(null!));
    }

    [Fact]
    public void Conforming_extensions_keep_their_checksums_and_heap()
    {
        var image = ImageHdu.Create<short>([1, 2, 3, 4], 2, 2);
        image.Header.Set("XTENSION", "IUEIMAGE");
        var table = BinaryTableHdu.Create([TableColumn.Create<int>("ID", [1, 2]), TableColumn.CreateVariable<short>("V", [[1, 2, 3], [4]])]);
        table.Header.Set("XTENSION", "A3DTABLE");
        var ms = new MemoryStream();
        new FitsFile().Add(ImageHdu.Create<byte>([0], 1)).Add(image).Add(table).Save(ms, ChecksumPolicy.Always);
        byte[] bytes = ms.ToArray();
        using var back = Reload(bytes);
        Assert.Equal(ChecksumStatus.Valid, back[1].VerifyChecksum().Checksum);
        Assert.Equal(ChecksumStatus.Valid, back[2].VerifyChecksum().Checksum);
        Assert.Equal(new short[][] { [1, 2, 3], [4] }, ((BinaryTableHdu)back[2]).ReadVariableColumn<short>("V"));
        Assert.True(back[2].Header.Get<long>("PCOUNT") > 0);
        Assert.Equal(bytes, Save(back));
        var again = new MemoryStream();
        back.Save(again, ChecksumPolicy.Always);
        Assert.Equal(bytes, again.ToArray());
    }

    [Fact]
    public void An_inconsistent_iueimage_is_reported_by_its_own_name()
    {
        var ms = new MemoryStream();
        var image = ImageHdu.Create<short>([1, 2, 3, 4], 2, 2);
        new FitsFile().Add(ImageHdu.Create<byte>([0], 1)).Add(image).Save(ms);
        byte[] bytes = ms.ToArray();
        string text = Encoding.ASCII.GetString(bytes);
        int at = text.IndexOf("XTENSION= 'IMAGE   '", StringComparison.Ordinal);
        Encoding.ASCII.GetBytes("XTENSION= 'IUEIMAGE'").CopyTo(bytes, at);
        int pcount = text.IndexOf("PCOUNT  =                    0", at, StringComparison.Ordinal);
        Encoding.ASCII.GetBytes("PCOUNT  =                    5").CopyTo(bytes, pcount);
        var ex = Assert.Throws<FitsException>(() => Reload(bytes));
        Assert.Contains("IUEIMAGE", ex.Message);
    }

    [Fact]
    public void A_logarithmic_spectral_axis_needs_a_reference_value()
    {
        var h = new FitsHeader();
        h.Set("CTYPE1", "FREQ-LOG").Set("CRVAL1", 0.0).Set("CDELT1", 1.0).Set("CRPIX1", 1.0);
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(h));
        h.Set("CRVAL1", 1.4e9);
        Assert.Equal(1.4e9, WorldCoordinateSystem.FromHeader(h).PixelToWorld([1.0])[0], 3);
    }

    [Fact]
    public void Log1p_is_accurate_over_its_whole_range()
    {
        Assert.Equal(9.9995000333308335e-05, Trig.Log1P(1e-4), 1e-19);
        Assert.Equal(-1.00005000333358e-04, Trig.Log1P(-1e-4), 1e-18);
        Assert.Equal(1e-20, Trig.Log1P(1e-20), 1e-35);
        Assert.Equal(0.0953101798043249, Trig.Log1P(0.1), 15);
        Assert.Equal(Math.Log(2.0), Trig.Log1P(1.0), 15);
        Assert.Equal(0.0, Trig.Log1P(0.0));
    }
}
