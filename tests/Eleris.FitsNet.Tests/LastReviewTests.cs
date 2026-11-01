using System.Diagnostics;

namespace Eleris.FitsNet.Tests;

/// <summary>Regression tests for the findings of the last verification review.</summary>
public class LastReviewTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static byte[] Save(FitsFile f, ChecksumPolicy policy = ChecksumPolicy.Auto)
    {
        var ms = new MemoryStream();
        f.Save(ms, policy);
        return ms.ToArray();
    }

    private static readonly ChecksumResult Good = new(ChecksumStatus.Valid, ChecksumStatus.Valid);
    private static readonly ChecksumResult Bad = new(ChecksumStatus.Invalid, ChecksumStatus.Invalid);

    // ---- 1. corruption is never healed behind the caller's back --------------------------------------------------------

    [Fact]
    public void Saving_the_same_corrupt_file_twice_still_does_not_hide_the_corruption()
    {
        using var f = FitsFile.Read(new MemoryStream(File.ReadAllBytes(Fixture("checksum_corrupt.fits"))));
        byte[] first = Save(f);
        Assert.Equal(Bad, f.VerifyChecksums()[0]);
        byte[] second = Save(f);
        Assert.Equal(first, second);
        using var back = FitsFile.Read(new MemoryStream(second));
        Assert.Equal(Bad, back.VerifyChecksums()[0]);
        Assert.Equal(Good, back.VerifyChecksums()[1]);
    }

    [Fact]
    public void The_same_holds_for_a_lazily_opened_file_saved_twice_over_itself()
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-corrupt-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            File.Copy(Fixture("checksum_corrupt.fits"), path);
            using var f = FitsFile.Open(path);
            f.Save(path);
            f.Save(path);
            Assert.Equal(Bad, f.VerifyChecksums()[0]);
            using var again = FitsFile.Open(path);
            Assert.Equal(Bad, again.VerifyChecksums()[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_edited_hdu_gets_fresh_sums_even_when_the_old_ones_were_wrong()
    {
        using var f = FitsFile.Read(new MemoryStream(File.ReadAllBytes(Fixture("checksum_corrupt.fits"))));
        f[0].Header.Set("TOUCHED", true);
        byte[] saved = Save(f);
        using var back = FitsFile.Read(new MemoryStream(saved));
        Assert.Equal(Good, back.VerifyChecksums()[0]);
    }

    // ---- 2. an unedited file is written back identically ---------------------------------------------------------------

    public static IEnumerable<object[]> AllFixtures()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        return Directory.GetFiles(root, "*.fits", SearchOption.AllDirectories)
            .Select(p => new object[] { Path.GetRelativePath(root, p) });
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Every_fixture_is_written_back_byte_for_byte_when_nothing_was_edited(string name)
    {
        byte[] original = File.ReadAllBytes(Fixture(name));
        foreach (bool strict in new[] { false, true })
        {
            using var f = FitsFile.Read(new MemoryStream(original), new FitsReadOptions(strict));
            Assert.Equal(original, Save(f));
            Assert.Equal(original, Save(f)); // and again
        }
        using var lazy = FitsFile.Open(Fixture(name));
        Assert.Equal(original, Save(lazy));
    }

    [Fact]
    public void The_real_camera_images_are_written_back_byte_for_byte()
    {
        foreach (string file in new[] { "img-0022-M42-broadband.fits", "img-1200-M42-dualband.fits" })
        {
            using var f = FitsFile.Open(TestData.Vespera(file));
            Assert.Equal(File.ReadAllBytes(TestData.Vespera(file)), Save(f));
        }
    }

    // ---- 4/5. wide tables ----------------------------------------------------------------------------------------------

    [Fact]
    public void One_invalid_tform_leaves_the_other_columns_readable_and_the_header_repairable()
    {
        var t = BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2]), TableColumn.Create<int>("B", [3, 4]), TableColumn.Create<int>("C", [5, 6])]);
        t.Header.Set("TFORM2", "1Z");
        Assert.Equal(new[] { 1, 2 }, t.ReadColumn<int>("A"));
        Assert.Equal(2, t.IndexOf("C"));
        Assert.Equal(3, t.Columns.Count);
        Assert.Throws<FitsException>(() => t.ReadColumn<int>("B"));
        var ex = Assert.Throws<FitsException>(() => t.ReadColumn<int>("C"));
        Assert.Contains("unknown", ex.Message);
        Assert.Throws<FitsException>(() => t.ReadColumns());
        Assert.Throws<FitsException>(() => new FitsFile().Add(t).Save(new MemoryStream()));

        t.Header.Set("TFORM2", "1J"); // repair
        Assert.Equal(new[] { 3, 4 }, t.ReadColumn<int>("B"));
        Assert.Equal(new[] { 5, 6 }, t.ReadColumn<int>("C"));
    }

    [Fact]
    public void Editing_the_header_of_a_wide_table_does_not_make_every_read_slow()
    {
        const int columns = 999;
        var t = BinaryTableHdu.Create(Enumerable.Range(0, columns).Select(i => TableColumn.Create<int>("C" + i, [i])).ToList());
        for (int i = 0; i < 5000; i++) t.Header.Add(FitsCard.CommentCard("filler " + i));
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
        {
            t.Header.Set("TUNIT1", "u" + i); // invalidates the parsed columns
            Assert.Equal(new[] { 1 }, t.ReadColumn<int>(1));
        }
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"took {sw.Elapsed}"); // 8.4 s before the header was indexed
    }

    [Fact]
    public void The_header_index_never_returns_a_stale_card()
    {
        var h = new FitsHeader();
        for (int i = 0; i < 100; i++) h.Add(new FitsCard("K" + i, i));
        Assert.Equal(50L, h.Find("K50")!.Value);
        h.Set("K50", 500);
        Assert.Equal(500L, h.Find("K50")!.Value);
        h.Remove("K10");
        Assert.Null(h.Find("K10"));
        Assert.Equal(11L, h.Find("K11")!.Value);
        h.Insert(0, new FitsCard("FIRST", 1));
        Assert.Equal(1L, h.Find("FIRST")!.Value);
        h.Add(new FitsCard("K50", 7)); // a duplicate: the first one wins
        Assert.Equal(500L, h.Find("K50")!.Value);
        h.RemoveAt(0);
        Assert.Null(h.Find("FIRST"));
        h.Clear();
        Assert.Null(h.Find("K11"));
    }

    // ---- 6. error paths of Save -----------------------------------------------------------------------------------------

    [Fact]
    public void Saving_over_a_locked_file_reports_the_io_error_and_keeps_everything_usable()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fitsnet-locked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string source = Path.Combine(dir, "source.fits"), target = Path.Combine(dir, "target.fits");
            new FitsFile().Add(ImageHdu.Create<short>([1, 2, 3], 3)).Save(source);
            new FitsFile().Add(ImageHdu.Create<short>([9], 1)).Save(target);

            using var f = FitsFile.Open(source);
            using (new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                if (OperatingSystem.IsWindows()) Assert.Throws<IOException>(() => f.Save(target)); // other systems allow replacing an open file
            }
            Assert.Equal(new short[] { 1, 2, 3 }, ((ImageHdu)f.Primary).ReadPixels<short>());
            Assert.DoesNotContain(Directory.GetFiles(dir), p => p.Contains(".tmp-"));
            f.Save(target); // the file is free again
            using var back = FitsFile.Open(target);
            Assert.Equal(new short[] { 1, 2, 3 }, ((ImageHdu)back.Primary).ReadPixels<short>());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Saving_to_a_new_path_does_not_throw_and_catch_exceptions_internally()
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-new-" + Guid.NewGuid().ToString("N") + ".fits");
        int first = 0;
        int testThread = Environment.CurrentManagedThreadId; // other tests run in parallel: count only what this thread throws
        void Count(object? s, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            if (Environment.CurrentManagedThreadId == testThread) Interlocked.Increment(ref first);
        }
        try
        {
            var file = new FitsFile().Add(ImageHdu.Create<byte>([1], 1));
            AppDomain.CurrentDomain.FirstChanceException += Count;
            file.Save(path);
            AppDomain.CurrentDomain.FirstChanceException -= Count;
            Assert.Equal(0, first);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Count;
            File.Delete(path);
        }
    }
}
