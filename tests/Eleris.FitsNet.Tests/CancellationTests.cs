using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>Pixel reads, decompression and saves take a <see cref="CancellationToken"/> that stops them between blocks (issue #9).</summary>
public class CancellationTests
{
    private static CancellationToken Cancelled => new(canceled: true);

    private static string WriteImage(ScratchDirectory dir, string name, int width = 300, int height = 200)
    {
        string path = dir.File(name);
        var pixels = Enumerable.Range(0, width * height).Select(i => (ushort)(i % 65536)).ToArray();
        new FitsFile().Add(ImageHdu.Create<ushort>(pixels, width, height)).Save(path);
        return path;
    }

    // ---- Reads -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_cancellable_read_of_a_lazily_opened_image_stops_on_a_cancelled_token()
    {
        using var dir = new ScratchDirectory();
        using var file = FitsFile.Open(WriteImage(dir, "a.fits"));
        var image = (ImageHdu)file.Primary;

        Assert.Throws<OperationCanceledException>(() => image.ReadPixels<ushort>(Cancelled));
        Assert.Throws<OperationCanceledException>(() => image.ReadPixels<float>(Cancelled));
        Assert.Throws<OperationCanceledException>(() => image.ReadPixels<double>(Cancelled));
        Assert.Throws<OperationCanceledException>(() => image.ReadRawPixels<short>(Cancelled));
        Assert.Throws<OperationCanceledException>(() => image.ReadRegion<float>([0, 0], [10, 10], Cancelled));
        Assert.Throws<OperationCanceledException>(() => image.ReadRawRegion<short>([0, 0], [10, 10], Cancelled));
        Assert.Throws<OperationCanceledException>(() => image.Subimage([0, 0], [10, 10], Cancelled));

        // the image is still readable afterwards, and a token that is not cancelled changes nothing
        using var live = new CancellationTokenSource();
        Assert.Equal(image.ReadPixels<ushort>(), image.ReadPixels<ushort>(live.Token));
        Assert.Equal(image.ReadPixels<float>(), image.ReadPixels<float>(live.Token));
        Assert.Equal(image.ReadRawPixels<short>(), image.ReadRawPixels<short>(live.Token));
        Assert.Equal(image.ReadRegion<double>([5, 7], [100, 50]), image.ReadRegion<double>([5, 7], [100, 50], live.Token));
        Assert.Equal(image.ReadRawRegion<short>([5, 7], [100, 50]), image.ReadRawRegion<short>([5, 7], [100, 50], live.Token));
    }

    [Theory]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("raw")]
    public void A_read_cancelled_while_it_runs_stops_before_the_end(string kind)
    {
        // 4 planes of 300 000 floats (1.2 MB each): the read goes through several blocks per plane.
        const int perPlane = 300_000;
        using var cts = new CancellationTokenSource();
        int accesses = 0;
        var cancelling = Enumerable.Range(0, 4).Select(_ => new HookedMemory<float>(new float[perPlane], () =>
        {
            if (++accesses == 2) cts.Cancel();
        })).ToList();
        var image = ImageHdu.CreateFromPlanes<float>(cancelling.Select(h => h.Plane).ToList(), 600, 500, 4);

        Action read = kind switch
        {
            "float" => () => image.ReadPixels<float>(cts.Token),
            "double" => () => image.ReadPixels<double>(cts.Token),
            _ => () => image.ReadRawPixels<float>(cts.Token),
        };
        Assert.Throws<OperationCanceledException>(read);
        // one access per block of 256 KB (a block never spans two planes here); a full read takes more than 16 blocks
        Assert.InRange(accesses, 2, 3);
    }

    [Fact]
    public void A_region_read_cancelled_while_it_runs_stops_before_the_end()
    {
        using var cts = new CancellationTokenSource();
        int accesses = 0;
        var plane = new HookedMemory<float>(new float[100 * 100], () =>
        {
            if (++accesses == 3) cts.Cancel();
        });
        var image = ImageHdu.CreateFromPlanes<float>([plane.Plane], 100, 100, 1);

        // a narrow column: one short run per row, 100 runs
        Assert.Throws<OperationCanceledException>(() => image.ReadRegion<float>([10, 0, 0], [2, 100, 1], cts.Token));
        Assert.Equal(3, accesses);
    }

    [Fact]
    public void A_region_of_whole_rows_is_read_in_blocks_that_can_be_cancelled()
    {
        // The whole image is one contiguous run of 4.8 MB: it is still read a megabyte at a time when the read can be cancelled.
        const int perPlane = 300_000;
        using var cts = new CancellationTokenSource();
        int accesses = 0;
        var planes = Enumerable.Range(0, 4).Select(p => new HookedMemory<float>(Enumerable.Repeat((float)p, perPlane).ToArray(), () =>
        {
            if (++accesses == 1) cts.Cancel();
        })).ToList();
        var image = ImageHdu.CreateFromPlanes<float>(planes.Select(h => h.Plane).ToList(), 600, 500, 4);

        Assert.Throws<OperationCanceledException>(() => image.ReadRegion<float>([0, 0, 0], [600, 500, 4], cts.Token));
        Assert.Equal(1, accesses);   // the first megabyte only

        // with a live token, the blocks put together give the same pixels as one read
        using var live = new CancellationTokenSource();
        Assert.Equal(image.ReadRegion<float>([0, 100, 1], [600, 400, 3]), image.ReadRegion<float>([0, 100, 1], [600, 400, 3], live.Token));
    }

    [Fact]
    public void Decompression_stops_on_a_cancelled_token()
    {
        var pixels = Enumerable.Range(0, 64 * 64).Select(i => (short)(i % 1000)).ToArray();
        var compressed = CompressedImageHdu.Create<short>(pixels, [64, 64], new CompressionOptions { TileShape = [16, 16] });

        Assert.Throws<OperationCanceledException>(() => compressed.Decompress(-1, Cancelled));
        Assert.Throws<OperationCanceledException>(() => compressed.ReadPixels<short>(Cancelled));
        Assert.Throws<OperationCanceledException>(() => compressed.ReadRawPixels<short>(Cancelled));

        using var live = new CancellationTokenSource();
        Assert.Equal(pixels, compressed.ReadPixels<short>(live.Token));
        Assert.Equal(pixels, compressed.ReadRawPixels<short>(live.Token));
        Assert.Equal(pixels, compressed.Decompress(2, live.Token).ReadPixels<short>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Tiles_stop_being_started_once_the_token_is_cancelled(int degree)
    {
        using var cts = new CancellationTokenSource();
        int started = 0;
        Assert.Throws<OperationCanceledException>(() => CompressedImageHdu.RunTiles(1000, degree, 16, t =>
        {
            if (Interlocked.Increment(ref started) == 5) cts.Cancel();
        }, cts.Token));
        Assert.InRange(started, 5, 5 + degree);   // the tiles already running when the token was cancelled may finish
    }

    // ---- Saves -------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_save_to_a_path_cancelled_before_it_starts_writes_nothing()
    {
        using var dir = new ScratchDirectory();
        string path = WriteImage(dir, "a.fits");
        byte[] before = File.ReadAllBytes(path);
        var file = new FitsFile().Add(ImageHdu.Create<float>(new float[100], 10, 10));

        Assert.Throws<OperationCanceledException>(() => file.Save(path, ChecksumPolicy.Always, Cancelled));
        Assert.Throws<OperationCanceledException>(() => file.Save(dir.File("new.fits"), ChecksumPolicy.Auto, FitsSaveMode.CreateNew, Cancelled));
        Assert.Throws<OperationCanceledException>(() => file.Save(new MemoryStream(), ChecksumPolicy.Auto, Cancelled));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(["a.fits"], dir.Names());
        Assert.False(file.Primary.Header.Contains("CHECKSUM"));
    }

    [Theory]
    [InlineData(FitsSaveMode.Overwrite, ChecksumPolicy.Auto)]
    [InlineData(FitsSaveMode.Overwrite, ChecksumPolicy.Always)]
    [InlineData(FitsSaveMode.CreateNew, ChecksumPolicy.Auto)]
    public void A_save_cancelled_while_the_data_is_written_leaves_no_file_and_the_target_untouched(FitsSaveMode mode, ChecksumPolicy checksums)
    {
        using var dir = new ScratchDirectory();
        string existing = WriteImage(dir, "existing.fits");
        byte[] before = File.ReadAllBytes(existing);
        string target = mode == FitsSaveMode.Overwrite ? existing : dir.File("new.fits");

        using var cts = new CancellationTokenSource();
        int accesses = 0;
        var planes = Planes.Float(400, 300, 3)
            .Select(p => new HookedMemory<float>(p, () =>
            {
                if (++accesses == 2) cts.Cancel();   // the second block of pixels taken: the save is under way
            }))
            .Select(h => h.Plane)
            .ToList();
        var image = ImageHdu.CreateFromPlanes<float>(planes, 400, 300, 3);
        image.Header.Set("OBJECT", "M42");
        var file = new FitsFile().Add(image);
        var cardsBefore = image.Header.Select(c => c.ToString()).ToList();

        Assert.Throws<OperationCanceledException>(() => file.Save(target, checksums, mode, cts.Token));

        Assert.InRange(accesses, 2, 3);   // one access per block of 256 KB; the first plane alone is two blocks
        Assert.Equal(before, File.ReadAllBytes(existing));
        Assert.Equal(["existing.fits"], dir.Names());                         // no temporary file, no new file
        Assert.Equal(cardsBefore, image.Header.Select(c => c.ToString()));    // the headers are rolled back

        // the same file saves normally afterwards
        file.Save(dir.File("after.fits"));
        using var reread = FitsFile.Open(dir.File("after.fits"));
        Assert.Equal(Planes.Concat(Planes.Float(400, 300, 3)), ((ImageHdu)reread.Primary).ReadPixels<float>());
    }

    [Fact]
    public void A_cancelled_save_over_the_open_source_file_keeps_the_instance_usable()
    {
        using var dir = new ScratchDirectory();
        string path = WriteImage(dir, "a.fits");
        byte[] before = File.ReadAllBytes(path);
        using var file = FitsFile.Open(path);
        var image = (ImageHdu)file.Primary;
        ushort[] pixels = image.ReadPixels<ushort>();

        using var cts = new CancellationTokenSource();
        int accesses = 0;
        var plane = new HookedMemory<float>(new float[50], () =>
        {
            if (++accesses == 1) cts.Cancel();
        });
        file.Add(ImageHdu.CreateFromPlanes<float>([plane.Plane], 50, 1));
        image.Header.Set("OBSERVER", "someone");

        Assert.Throws<OperationCanceledException>(() => file.Save(path, ChecksumPolicy.Auto, cts.Token));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(["a.fits"], dir.Names());
        Assert.Equal(pixels, image.ReadPixels<ushort>());   // still reading the original file
        file.Save(path);                                    // and the save can be done again
        Assert.Equal(pixels, ((ImageHdu)file.Primary).ReadPixels<ushort>());
    }

    [Fact]
    public void A_save_to_a_stream_stops_between_blocks_of_data_held_in_memory()
    {
        // 8 MB of pixels in memory: written in blocks of 1 MB when the save can be cancelled.
        var image = ImageHdu.Create<double>(new double[1 << 20], 1024, 1024);
        var file = new FitsFile().Add(image);
        long full = Planes.Save(file).Length;

        using var cts = new CancellationTokenSource();
        using var stream = new CancellingStream(cts, after: 3 << 20);
        Assert.Throws<OperationCanceledException>(() => file.Save(stream, ChecksumPolicy.Always, cts.Token));
        Assert.InRange(stream.Length, 3 << 20, (4 << 20) + 2880);
        Assert.True(stream.Length < full);
        Assert.False(image.Header.Contains("CHECKSUM"));
    }

    /// <summary>A stream that cancels a token once a number of bytes has been written to it.</summary>
    private sealed class CancellingStream(CancellationTokenSource cts, long after) : MemoryStream
    {
        // MemoryStream sends the span overload of a derived class here too
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            if (Length >= after) cts.Cancel();
        }
    }
}
