using System.Numerics;
using System.Runtime.InteropServices;

namespace Eleris.FitsNet.Tests;

/// <summary><see cref="FitsImageWriter{T}"/> writes an image band by band, byte for byte as the in-memory save (issue #13).</summary>
public class StreamedWriteTests
{
    internal static T[][] RandomPlanes<T>(int planes, int perPlane, int seed) where T : unmanaged
    {
        var random = new Random(seed);
        return Enumerable.Range(0, planes).Select(_ =>
        {
            var bytes = new byte[perPlane * System.Runtime.CompilerServices.Unsafe.SizeOf<T>()];
            random.NextBytes(bytes);
            var values = MemoryMarshal.Cast<byte, T>(bytes).ToArray();
            if (typeof(T) == typeof(float)) for (int i = 0; i < values.Length; i++) values[i] = (T)(object)(float)(random.NextDouble() * 1e6 - 5e5);
            if (typeof(T) == typeof(double)) for (int i = 0; i < values.Length; i++) values[i] = (T)(object)(random.NextDouble() * 1e9 - 5e8);
            return values;
        }).ToArray();
    }

    /// <summary>Writes the planes with the writer, in bands of <paramref name="bandHeight"/> rows; returns the bytes of the file.</summary>
    internal static byte[] Stream<T>(string path, FitsHeader header, T[][] planes, int rowLength, int bandHeight,
        ChecksumPolicy checksums = ChecksumPolicy.Auto, FitsSaveMode mode = FitsSaveMode.Overwrite) where T : unmanaged
    {
        using (var writer = FitsImageWriter<T>.Create(path, header, checksums, mode))
        {
            for (int p = 0; p < planes.Length; p++)
            {
                int rows = planes[p].Length / rowLength;
                for (int row = 0; row < rows; row += bandHeight)
                {
                    int n = Math.Min(bandHeight, rows - row);
                    writer.WriteRows(p, row, planes[p].AsSpan(row * rowLength, n * rowLength));
                }
            }
            writer.Complete();
        }
        return File.ReadAllBytes(path);
    }

    /// <summary>The image the in-memory path writes for the same planes, with the cards that <paramref name="decorate"/> sets.</summary>
    private static ImageHdu InMemory<T>(T[][] planes, long[] axes, Action<FitsHeader>? decorate) where T : unmanaged
    {
        var image = ImageHdu.Create<T>(Planes.Concat(planes), axes);
        image.Header.Set("OBJECT", "M42", "target");
        decorate?.Invoke(image.Header);
        return image;
    }

    private static void CheckEveryWay<T>(ScratchDirectory dir, int seed, Action<FitsHeader>? decorate = null, Action<T[][]>? alter = null)
        where T : unmanaged
    {
        foreach (long[] axes in new[] { new long[] { 37, 23 }, [37, 23, 3] })
        {
            int planeCount = axes.Length == 3 ? 3 : 1;
            var planes = RandomPlanes<T>(planeCount, 37 * 23, seed);
            alter?.Invoke(planes);
            foreach (var checksums in new[] { ChecksumPolicy.Auto, ChecksumPolicy.Always, ChecksumPolicy.Never })
                foreach (bool sumsInHeader in new[] { false, true })
                {
                    var image = InMemory(planes, axes, decorate);
                    if (sumsInHeader) image.Header.Set("DATASUM", "0");   // Auto refreshes the sums of a header that has them
                    var given = new FitsHeader();                         // the header before the save, which changes it
                    foreach (var card in image.Header) given.Add(card);
                    byte[] expected = Planes.Save(new FitsFile().Add(image), checksums);
                    foreach (int band in new[] { 1, 5, 23 })   // one row, a height that does not divide the plane, the whole plane
                    {
                        string path = dir.File($"{typeof(T).Name}-{axes.Length}-{checksums}-{sumsInHeader}-{band}.fits");
                        byte[] streamed = Stream(path, given, planes, 37, band, checksums);
                        Assert.True(expected.AsSpan().SequenceEqual(streamed),
                            $"{typeof(T).Name}, {axes.Length} axes, {checksums}, sums in header {sumsInHeader}, bands of {band}: the files differ");
                    }
                }
        }
    }

    [Fact]
    public void Every_pixel_type_is_written_byte_for_byte_like_the_in_memory_save()
    {
        using var dir = new ScratchDirectory();
        CheckEveryWay<byte>(dir, 1);
        CheckEveryWay<sbyte>(dir, 2);
        CheckEveryWay<short>(dir, 3);
        CheckEveryWay<ushort>(dir, 4);
        CheckEveryWay<int>(dir, 5);
        CheckEveryWay<uint>(dir, 6);
        CheckEveryWay<long>(dir, 7);
        CheckEveryWay<ulong>(dir, 8);
        CheckEveryWay<float>(dir, 9);
        CheckEveryWay<double>(dir, 10);
        Assert.DoesNotContain(dir.Names(), n => n.Contains(".tmp-", StringComparison.Ordinal));
    }

    [Fact]
    public void Scaling_blank_and_not_a_number_are_written_like_the_in_memory_save()
    {
        using var dir = new ScratchDirectory();
        // BSCALE / BZERO and BLANK are cards: the stored values are written as given
        static void Scaled(FitsHeader h) => h.Set("BSCALE", 0.5, "scale").Set("BZERO", 100.0, "offset").Set("BUNIT", "adu");
        CheckEveryWay<byte>(dir, 11, h => Scaled(h.Set("BLANK", 255L)), p => p[0][3] = 255);
        CheckEveryWay<short>(dir, 12, h => Scaled(h.Set("BLANK", -32768L)), p => p[^1][^1] = short.MinValue);
        CheckEveryWay<int>(dir, 13, h => Scaled(h.Set("BLANK", -999L)), p => p[0][0] = -999);
        CheckEveryWay<long>(dir, 14, h => Scaled(h.Set("BLANK", long.MinValue)), p => p[0][1] = long.MinValue);
        // the unsigned convention, with a BLANK among the stored values
        CheckEveryWay<ushort>(dir, 15, h => h.Set("BLANK", 0L), p => p[0][5] = 32768);
        CheckEveryWay<float>(dir, 16, null, p => p[0][7] = float.NaN);
        CheckEveryWay<double>(dir, 17, h => h.Set("BUNIT", "Jy"), p => p[^1][2] = double.NaN);

        // and the values read back are the physical ones
        var planes = RandomPlanes<short>(1, 37 * 23, 18);
        planes[0][4] = short.MinValue;
        var image = InMemory(planes, [37, 23], h => Scaled(h.Set("BLANK", -32768L)));
        Stream(dir.File("scaled.fits"), image.Header, planes, 37, 4, ChecksumPolicy.Always);
        using var file = FitsFile.Open(dir.File("scaled.fits"));
        double[] physical = ((ImageHdu)file.Primary).ReadPixels<double>();
        Assert.True(double.IsNaN(physical[4]));
        Assert.Equal(100 + 0.5 * planes[0][5], physical[5]);
        Assert.Equal(ChecksumStatus.Valid, file.Primary.VerifyChecksum().Checksum);
    }

    [Fact]
    public void Large_bands_spanning_several_blocks_are_written_like_the_in_memory_save()
    {
        using var dir = new ScratchDirectory();
        // 3 planes of 700 x 500 doubles: bands of 37 rows hold several blocks, and blocks end inside a band
        var doubles = RandomPlanes<double>(3, 700 * 500, 42);
        var cube = InMemory(doubles, [700, 500, 3], null);
        Assert.Equal(Planes.Save(new FitsFile().Add(cube), ChecksumPolicy.Always),
            Stream(dir.File("doubles.fits"), cube.Header, doubles, 700, 37, ChecksumPolicy.Always));

        // rows of 300 001 bytes: one row is more than a block, and the blocks end on no 4-byte boundary of the checksum
        var bytes = RandomPlanes<sbyte>(2, 300_001 * 3, 43);
        var wide = InMemory(bytes, [300_001, 3, 2], null);
        Assert.Equal(Planes.Save(new FitsFile().Add(wide), ChecksumPolicy.Always),
            Stream(dir.File("bytes.fits"), wide.Header, bytes, 300_001, 2, ChecksumPolicy.Always));
    }

    [Fact]
    public void The_header_of_an_extension_or_without_SIMPLE_gives_a_primary_image()
    {
        using var dir = new ScratchDirectory();
        var planes = RandomPlanes<float>(2, 6 * 4, 5);

        // the header of an extension, read from a file: the file is the one Save writes for that image as the primary HDU
        var image = InMemory(planes, [6, 4, 2], h => h.Set("EXTNAME", "SCI"));
        var read = FitsFile.Read(new MemoryStream(Planes.Save(new FitsFile().Add(new ImageHdu()).Add(image))));
        var extension = (ImageHdu)read[1];
        Assert.Equal("XTENSION", extension.Header[0].Keyword);
        byte[] streamed = Stream(dir.File("from-extension.fits"), extension.Header, planes, 6, 3, ChecksumPolicy.Always);
        Assert.Equal("XTENSION", extension.Header[0].Keyword);   // the header given is not changed
        read.RemoveAt(0);
        Assert.Equal(Planes.Save(read, ChecksumPolicy.Always), streamed);

        // a header made from scratch, without SIMPLE
        var header = new FitsHeader().Set("BITPIX", -32).Set("NAXIS", 3).Set("NAXIS1", 6).Set("NAXIS2", 4).Set("NAXIS3", 2).Set("OBJECT", "M42");
        Stream(dir.File("scratch.fits"), header, planes, 6, 4);
        using var file = FitsFile.Open(dir.File("scratch.fits"));
        var written = (ImageHdu)file.Primary;
        Assert.Equal("SIMPLE", written.Header[0].Keyword);
        Assert.Equal([6L, 4, 2], written.Axes);
        Assert.Equal("M42", written.Header.Get<string>("OBJECT"));
        Assert.Equal(Planes.Concat(planes), written.ReadPixels<float>());
        Assert.Equal(6, header.Count);   // the header given is copied, not changed
    }

    [Fact]
    public void The_shape_of_the_image_gives_the_rows_and_the_planes()
    {
        using var dir = new ScratchDirectory();
        var header = new FitsHeader().Set("BITPIX", 16).Set("NAXIS", 4).Set("NAXIS1", 4).Set("NAXIS2", 3).Set("NAXIS3", 2).Set("NAXIS4", 5);
        using (var writer = FitsImageWriter<short>.Create(dir.File("4d.fits"), header))
        {
            Assert.Equal(4, writer.RowLength);
            Assert.Equal(3, writer.RowsPerPlane);
            Assert.Equal(10, writer.PlaneCount);
            short[] values = [.. Enumerable.Range(0, 4 * 3 * 2 * 5).Select(i => (short)i)];
            for (int p = 0; p < 10; p++) writer.WriteRows(p, 0, values.AsSpan(p * 12, 12));
            writer.Complete();
            using var file = FitsFile.Open(dir.File("4d.fits"));
            Assert.Equal(values, ((ImageHdu)file.Primary).ReadPixels<short>());
        }

        // one axis: one plane of one row
        var line = new FitsHeader().Set("BITPIX", 8).Set("NAXIS", 1).Set("NAXIS1", 5);
        using (var writer = FitsImageWriter<byte>.Create(dir.File("line.fits"), line))
        {
            Assert.Equal(1, writer.RowsPerPlane);
            Assert.Equal(1, writer.PlaneCount);
            writer.WriteRows(0, 0, [1, 2, 3, 4, 5]);
            writer.Complete();
        }
        using var reread = FitsFile.Open(dir.File("line.fits"));
        Assert.Equal([1, 2, 3, 4, 5], ((ImageHdu)reread.Primary).ReadPixels<byte>());
    }

    // ---- Publication ---------------------------------------------------------------------------------------------

    private static FitsHeader SmallHeader() => new FitsHeader().Set("BITPIX", 32).Set("NAXIS", 2).Set("NAXIS1", 10).Set("NAXIS2", 6);

    private static readonly int[] Rows = [.. Enumerable.Range(0, 60)];

    [Fact]
    public void The_target_is_not_touched_until_Complete()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("target.fits");
        File.WriteAllText(path, "old");
        using var writer = FitsImageWriter<int>.Create(path, SmallHeader(), ChecksumPolicy.Always);
        writer.WriteRows(0, 0, Rows.AsSpan(0, 30));
        Assert.Equal("old", File.ReadAllText(path));
        Assert.Equal(2, dir.Names().Length);   // the target and the temporary file next to it
        writer.WriteRows(0, 3, Rows.AsSpan(30, 30));
        Assert.Equal("old", File.ReadAllText(path));

        writer.Complete();
        Assert.Equal(["target.fits"], dir.Names());
        using var file = FitsFile.Open(path);
        Assert.Equal(Rows, ((ImageHdu)file.Primary).ReadPixels<int>());
    }

    [Fact]
    public void Disposing_before_Complete_abandons_the_file()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("target.fits");
        File.WriteAllText(path, "old");
        using (var writer = FitsImageWriter<int>.Create(path, SmallHeader()))
            writer.WriteRows(0, 0, Rows.AsSpan(0, 30));
        Assert.Equal("old", File.ReadAllText(path));
        Assert.Equal(["target.fits"], dir.Names());

        using (FitsImageWriter<int>.Create(dir.File("never.fits"), SmallHeader(), mode: FitsSaveMode.CreateNew)) { }
        Assert.Equal(["target.fits"], dir.Names());
    }

    [Fact]
    public void A_cancelled_write_leaves_no_file_and_the_target_as_it_was()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("target.fits");
        File.WriteAllText(path, "old");

        // between bands
        using var cancel = new CancellationTokenSource();
        var writer = FitsImageWriter<int>.Create(path, SmallHeader(), ChecksumPolicy.Always, FitsSaveMode.Overwrite, cancel.Token);
        writer.WriteRows(0, 0, Rows.AsSpan(0, 20), cancel.Token);
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => writer.WriteRows(0, 2, Rows.AsSpan(20, 20), cancel.Token));
        Assert.Equal(["target.fits"], dir.Names());
        Assert.Equal("old", File.ReadAllText(path));
        Assert.Throws<InvalidOperationException>(() => writer.WriteRows(0, 2, Rows.AsSpan(20, 20)));   // the write is abandoned
        Assert.Throws<InvalidOperationException>(() => writer.Complete());
        writer.Dispose();

        // in the middle of a band of many blocks
        Assert.True(CancelledWithinABand(dir, path), "the band was written whole before the token could be cancelled, every time");
        Assert.Equal(["target.fits"], dir.Names());
        Assert.Equal("old", File.ReadAllText(path));

        // at the end, before the file is put in place
        using var late = new CancellationTokenSource();
        using (var last = FitsImageWriter<int>.Create(path, SmallHeader(), ChecksumPolicy.Always))
        {
            last.WriteRows(0, 0, Rows);
            late.Cancel();
            Assert.Throws<OperationCanceledException>(() => last.Complete(late.Token));
        }
        Assert.Equal(["target.fits"], dir.Names());
        Assert.Equal("old", File.ReadAllText(path));

        // and nothing is created when the token is cancelled from the start
        Assert.Throws<OperationCanceledException>(() => FitsImageWriter<int>.Create(dir.File("other.fits"), SmallHeader(), cancellationToken: late.Token));
        Assert.Equal(["target.fits"], dir.Names());
    }

    /// <summary>
    /// Writes a band of 64 MB while another thread cancels the token as soon as the temporary file holds more than the header and one
    /// block: true when the write stopped in the middle of the band. A machine fast enough to write the band first is tried again.
    /// </summary>
    private static bool CancelledWithinABand(ScratchDirectory dir, string path)
    {
        const int width = 1 << 20, rows = 64;
        var band = new byte[width * rows];
        var header = new FitsHeader().Set("BITPIX", 8).Set("NAXIS", 2).Set("NAXIS1", (long)width).Set("NAXIS2", (long)rows);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            using var cancel = new CancellationTokenSource();
            using var writer = FitsImageWriter<byte>.Create(path, header);
            string temp = Directory.GetFiles(dir.Path, "*.tmp-*").Single();
            long seen = 0;
            var watcher = new Thread(() =>
            {
                while (!cancel.IsCancellationRequested)
                {
                    try
                    {
                        seen = new FileInfo(temp).Length;
                    }
                    catch (IOException)
                    {
                    }
                    if (seen > FitsHeader.BlockSize + 256 * 1024 || !File.Exists(temp)) cancel.Cancel();
                }
            });
            watcher.Start();
            try
            {
                writer.WriteRows(0, 0, band, cancel.Token);
            }
            catch (OperationCanceledException)
            {
                watcher.Join();
                if (seen > FitsHeader.BlockSize && seen < FitsHeader.BlockSize + band.Length) return true;
                continue;
            }
            cancel.Cancel();
            watcher.Join();
        }
        return false;
    }

    [Fact]
    public void A_target_in_a_missing_directory_fails_without_leaving_anything()
    {
        using var dir = new ScratchDirectory();
        Assert.Throws<DirectoryNotFoundException>(() => FitsImageWriter<int>.Create(dir.File(Path.Combine("missing", "x.fits")), SmallHeader()));
        Assert.Empty(dir.Names());
    }

    [Fact]
    public void CreateNew_refuses_an_existing_file_before_writing_anything()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("taken.fits");
        File.WriteAllText(path, "not mine");
        var e = Assert.Throws<FitsFileExistsException>(() => FitsImageWriter<int>.Create(path, SmallHeader(), mode: FitsSaveMode.CreateNew));
        Assert.Equal(Path.GetFullPath(path), e.FileName);
        Assert.Equal("not mine", File.ReadAllText(path));
        Assert.Equal(["taken.fits"], dir.Names());

        // a new file is the one a save in CreateNew mode writes
        string fresh = dir.File("new.fits");
        var image = ImageHdu.Create<int>(Rows, 10, 6);
        byte[] streamed = Stream(fresh, image.Header, [Rows], 10, 4, ChecksumPolicy.Always, FitsSaveMode.CreateNew);
        Assert.Equal(Planes.Save(new FitsFile().Add(image), ChecksumPolicy.Always), streamed);
    }

    [Fact]
    public void CreateNew_keeps_a_file_that_appears_while_the_image_is_written()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("race.fits");
        using var writer = FitsImageWriter<int>.Create(path, SmallHeader(), ChecksumPolicy.Always, FitsSaveMode.CreateNew);
        writer.WriteRows(0, 0, Rows);
        File.WriteAllText(path, "written by someone else");   // after the early check, before the file is put in place

        var e = Assert.Throws<FitsFileExistsException>(() => writer.Complete());
        Assert.IsAssignableFrom<IOException>(e.InnerException);   // refused by the move itself, not by the early check
        Assert.Equal("written by someone else", File.ReadAllText(path));
        Assert.Equal(["race.fits"], dir.Names());                 // no temporary file left
        Assert.Throws<InvalidOperationException>(() => writer.Complete());
    }

    [Fact]
    public void Concurrent_writers_in_CreateNew_mode_create_the_file_once()
    {
        using var dir = new ScratchDirectory();
        for (int round = 0; round < AsyncIoTests.ConcurrentRounds; round++)
        {
            string path = dir.File($"shared-{round}.fits");
            var files = AsyncIoTests.Contending();
            var outcome = new Exception?[files.Count];
            using var start = new Barrier(files.Count);
            var threads = files.Select((f, k) => new Thread(() =>
            {
                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(30)), "the threads did not all start");
                try
                {
                    var image = (ImageHdu)f.Primary;
                    int[] values = image.ReadPixels<int>();
                    Stream(path, image.Header, [values], values.Length, 1, ChecksumPolicy.Always, FitsSaveMode.CreateNew);
                }
                catch (Exception e)
                {
                    outcome[k] = e;
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            AsyncIoTests.CheckOneWinner(files, outcome, path);
        }
        Assert.Equal(AsyncIoTests.ConcurrentRounds, dir.Names().Length);   // no temporary file and no claim left
    }

    // ---- Errors --------------------------------------------------------------------------------------------------

    /// <summary>Runs <paramref name="misuse"/> on a writer that has written the first plane rows of <paramref name="written"/>.</summary>
    private static TException Misuse<TException>(int written, Action<FitsImageWriter<int>> misuse) where TException : Exception
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("image.fits");
        using var writer = FitsImageWriter<int>.Create(path, SmallHeader(), ChecksumPolicy.Always);
        if (written > 0) writer.WriteRows(0, 0, Rows.AsSpan(0, written * 10));
        var e = Assert.Throws<TException>(() => misuse(writer));
        Assert.Empty(dir.Names());                                                       // nothing published, no temporary file
        Assert.Throws<InvalidOperationException>(() => writer.WriteRows(0, written, Rows.AsSpan(0, 10)));   // the write is abandoned
        Assert.Throws<InvalidOperationException>(() => writer.Complete());
        return e;
    }

    [Fact]
    public void Rows_out_of_order_are_refused_and_nothing_is_published()
    {
        Assert.Equal("firstRow", Misuse<ArgumentException>(0, w => w.WriteRows(0, 1, Rows.AsSpan(0, 10))).ParamName);   // a row skipped
        Assert.Equal("firstRow", Misuse<ArgumentException>(2, w => w.WriteRows(0, 1, Rows.AsSpan(0, 10))).ParamName);   // a row written again
        Assert.Equal("firstRow", Misuse<ArgumentException>(2, w => w.WriteRows(0, 0, Rows.AsSpan(0, 10))).ParamName);
        Assert.Equal("plane", Misuse<ArgumentException>(0, w => w.WriteRows(1, 0, Rows.AsSpan(0, 10))).ParamName);      // a plane that does not exist
        Assert.Equal("firstRow", Misuse<ArgumentException>(0, w => w.WriteRows(0, -1, Rows.AsSpan(0, 10))).ParamName);
    }

    [Fact]
    public void Bands_of_the_wrong_length_are_refused_and_nothing_is_published()
    {
        Assert.Equal("rows", Misuse<ArgumentException>(0, w => w.WriteRows(0, 0, Rows.AsSpan(0, 15))).ParamName);   // not whole rows
        Assert.Equal("rows", Misuse<ArgumentException>(0, w => w.WriteRows(0, 0, Rows.AsSpan(0, 9))).ParamName);
        Assert.Equal("rows", Misuse<ArgumentException>(1, w => w.WriteRows(0, 1, [])).ParamName);                    // empty
        Assert.Equal("rows", Misuse<ArgumentException>(4, w => w.WriteRows(0, 4, Rows.AsSpan(0, 30))).ParamName);  // more rows than are left
        Assert.Equal("plane", Misuse<ArgumentException>(6, w => w.WriteRows(1, 0, Rows.AsSpan(0, 10))).ParamName); // rows after the last one
        Assert.Equal("plane", Misuse<ArgumentException>(6, w => w.WriteRows(0, 6, Rows.AsSpan(0, 10))).ParamName);
    }

    [Fact]
    public void Completing_with_missing_rows_is_refused_and_nothing_is_published()
    {
        Assert.Contains("6 row(s) are missing", Misuse<InvalidOperationException>(0, w => w.Complete()).Message);
        Assert.Contains("1 row(s) are missing", Misuse<InvalidOperationException>(5, w => w.Complete()).Message);
    }

    [Fact]
    public void A_completed_or_disposed_writer_refuses_more_calls()
    {
        using var dir = new ScratchDirectory();
        var writer = FitsImageWriter<int>.Create(dir.File("done.fits"), SmallHeader());
        writer.WriteRows(0, 0, Rows);
        writer.Complete();
        Assert.Throws<InvalidOperationException>(() => writer.WriteRows(0, 0, Rows.AsSpan(0, 10)));
        Assert.Throws<InvalidOperationException>(() => writer.Complete());
        writer.Dispose();
        writer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => writer.WriteRows(0, 0, Rows.AsSpan(0, 10)));
        Assert.Throws<ObjectDisposedException>(() => writer.Complete());
        Assert.Equal(["done.fits"], dir.Names());   // still there
    }

    [Fact]
    public void Headers_that_do_not_fit_the_values_are_refused_before_anything_is_written()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("x.fits");
        FitsHeader Header(int bitpix, params long[] axes)
        {
            var h = new FitsHeader().Set("BITPIX", bitpix).Set("NAXIS", axes.Length);
            for (int i = 0; i < axes.Length; i++) h.Set("NAXIS" + (i + 1), axes[i]);
            return h;
        }

        Assert.Throws<ArgumentNullException>(() => FitsImageWriter<int>.Create(path, null!));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<int>.Create("", Header(32, 2, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsImageWriter<int>.Create(path, Header(32, 2, 2), (ChecksumPolicy)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsImageWriter<int>.Create(path, Header(32, 2, 2), mode: (FitsSaveMode)9));
        Assert.Throws<NotSupportedException>(() => FitsImageWriter<char>.Create(path, Header(16, 2, 2)));
        Assert.Throws<NotSupportedException>(() => FitsImageWriter<Vector2>.Create(path, Header(-32, 2, 2)));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<float>.Create(path, Header(16, 2, 2)));     // BITPIX of another type
        Assert.Throws<ArgumentException>(() => FitsImageWriter<short>.Create(path, Header(-32, 2, 2)));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<ushort>.Create(path, Header(16, 2, 2)));    // no BZERO for ushort
        Assert.Throws<ArgumentException>(() => FitsImageWriter<ushort>.Create(path, Header(16, 2, 2).Set("BZERO", 100L)));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<ulong>.Create(path, Header(64, 2, 2).Set("BZERO", new BigInteger(1UL << 63) + 1)));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<uint>.Create(path, Header(32, 2, 2).Set("BZERO", 2147483649L)));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<ushort>.Create(path, Header(16, 2, 2).Set("BZERO", 32768L).Set("BSCALE", 2.0)));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<int>.Create(path, Header(32)));             // no pixel
        Assert.Throws<ArgumentException>(() => FitsImageWriter<int>.Create(path, Header(32, 4, 0)));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<int>.Create(path, new FitsHeader().Set("BITPIX", 32).Set("NAXIS", 2).Set("NAXIS1", 4)));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<int>.Create(path, Header(32, 4, 2).Set("BITPIX", 12)));
        Assert.Throws<ArgumentException>(() => FitsImageWriter<int>.Create(path, Header(32, 3_000_000_000L, 1)));   // rows too long for a span
        Assert.Throws<ArgumentException>(() => FitsImageWriter<int>.Create(path, Header(32, 0, 2, 2).Also(h => h.Insert(0, new FitsCard("SIMPLE", true))).Set("GROUPS", true).Set("PCOUNT", 0L).Set("GCOUNT", 2L)));
        var table = new FitsHeader().Set("XTENSION", "BINTABLE").Set("BITPIX", 8).Set("NAXIS", 2).Set("NAXIS1", 4).Set("NAXIS2", 2).Set("PCOUNT", 0L).Set("GCOUNT", 1L);
        Assert.Throws<ArgumentException>(() => FitsImageWriter<byte>.Create(path, table));
        Assert.Empty(dir.Names());

        // the unsigned convention accepts its BZERO however it is written
        using (FitsImageWriter<ushort>.Create(path, Header(16, 2, 1).Set("BZERO", 32768.0).Set("BSCALE", 1.0))) { }
        using (FitsImageWriter<ulong>.Create(path, ImageHdu.Create<ulong>([1, 2], 2).Header)) { }
        using (FitsImageWriter<sbyte>.Create(path, ImageHdu.Create<sbyte>([1, 2], 2).Header)) { }
        Assert.Empty(dir.Names());
    }

    // ---- Memory --------------------------------------------------------------------------------------------------

    [Fact]
    public void The_memory_used_does_not_grow_with_the_height_of_the_image()
    {
        using var dir = new ScratchDirectory();
        const int width = 2048, bandHeight = 16;
        var band = new float[width * bandHeight];
        new Random(1).NextBytes(MemoryMarshal.AsBytes(band.AsSpan()));
        for (int i = 0; i < band.Length; i++) if (float.IsNaN(band[i])) band[i] = 0;

        long Allocated(int height)
        {
            void Write()
            {
                var header = new FitsHeader().Set("BITPIX", -32).Set("NAXIS", 2).Set("NAXIS1", (long)width).Set("NAXIS2", (long)height);
                using var writer = FitsImageWriter<float>.Create(dir.File("tall.fits"), header, ChecksumPolicy.Always);
                for (int row = 0; row < height; row += bandHeight) writer.WriteRows(0, row, band);
                writer.Complete();
            }

            Write();   // warms up the pool, the JIT and the file system
            long before = GC.GetAllocatedBytesForCurrentThread();
            Write();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        long small = Allocated(256);    // 2 MB of data
        long tall = Allocated(4096);    // 32 MB of data
        Assert.True(tall < 512 * 1024, $"writing 32 MB band by band allocated {tall:N0} bytes");
        Assert.True(Math.Abs(tall - small) < 16 * 1024, $"the allocations grew with the height: {small:N0} bytes for 256 rows, {tall:N0} for 4096");
        Assert.Equal(FitsHeader.BlockSize + HduLayout.Padded(4096L * width * 4), new FileInfo(dir.File("tall.fits")).Length);
    }
}
