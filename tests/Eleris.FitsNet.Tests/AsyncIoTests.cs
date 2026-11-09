using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>
/// The asynchronous pixel reads and saves give the bytes and values of the synchronous ones, with the same cancellation and the same
/// guarantees for the target file, and do their stream I/O asynchronously.
/// </summary>
public class AsyncIoTests
{
    private static CancellationToken Cancelled => new(canceled: true);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>A file with an image of every kind of reading: unsigned, scaled with BLANK, float, double, sbyte, and a compressed image.</summary>
    private static string WriteFile(ScratchDirectory dir)
    {
        const int w = 301, h = 207;   // runs and chunks that end anywhere
        var scaled = ImageHdu.Create<short>(Enumerable.Range(0, w * h).Select(i => (short)(i % 3000 - 1500)).ToArray(), w, h);
        scaled.Header.Set("BSCALE", 0.5).Set("BZERO", 100.0).Set("BLANK", -1500L);
        string path = dir.File("all.fits");
        new FitsFile()
            .Add(ImageHdu.Create<ushort>(Enumerable.Range(0, w * h).Select(i => (ushort)(i * 7)).ToArray(), w, h))
            .Add(scaled)
            .Add(ImageHdu.Create<float>(Enumerable.Range(0, w * h * 2).Select(i => i * 0.5f).ToArray(), w, h, 2))
            .Add(ImageHdu.Create<double>(Enumerable.Range(0, w * h).Select(i => i / 3.0).ToArray(), w, h))
            .Add(ImageHdu.Create<sbyte>(Enumerable.Range(0, w * h).Select(i => (sbyte)(i % 256 - 128)).ToArray(), w, h))
            .Add(ImageHdu.Create<uint>(Enumerable.Range(0, w * h).Select(i => (uint)i * 40_000u).ToArray(), w, h))
            .Add(CompressedImageHdu.Create<short>(Enumerable.Range(0, w * h).Select(i => (short)(i % 500)).ToArray(), [w, h],
                new CompressionOptions { TileShape = [64, 32] }))
            .Save(path, ChecksumPolicy.Always);
        return path;
    }

    // ---- Reads ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Asynchronous_reads_return_the_values_of_the_synchronous_ones(bool lazy)
    {
        using var dir = new ScratchDirectory();
        string path = WriteFile(dir);
        using var stream = File.OpenRead(path);
        using var file = lazy ? FitsFile.Open(path) : FitsFile.Read(stream);
        var images = file.OfType<ImageHdu>().ToList();

        async Task Same<T>(ImageHdu image) where T : unmanaged
        {
            Assert.Equal(image.ReadPixels<T>(), await image.ReadPixelsAsync<T>());
            long[][] regions = [[0, 0, 301, 207], [10, 0, 1, 207], [0, 50, 301, 64], [17, 33, 100, 1], [300, 206, 1, 1]];
            foreach (var r in regions)
            {
                long[] origin = image.Axes.Count == 3 ? [r[0], r[1], 1] : [r[0], r[1]];
                long[] shape = image.Axes.Count == 3 ? [r[2], r[3], 1] : [r[2], r[3]];
                Assert.Equal(image.ReadRegion<T>(origin, shape), await image.ReadRegionAsync<T>(origin, shape));
            }
        }

        await Same<ushort>(images[0]);
        await Same<double>(images[0]);
        await Same<float>(images[1]);    // BSCALE, BZERO and BLANK applied
        await Same<double>(images[1]);
        await Same<float>(images[2]);
        await Same<double>(images[3]);
        await Same<sbyte>(images[4]);
        await Same<uint>(images[5]);
        await Same<float>(images[5]);

        Assert.Equal(images[0].ReadRawPixels<short>(), await images[0].ReadRawPixelsAsync<short>());
        Assert.Equal(images[1].ReadRawPixels<short>(), await images[1].ReadRawPixelsAsync<short>());
        Assert.Equal(images[0].ReadRawRegion<short>([5, 6], [70, 80]), await images[0].ReadRawRegionAsync<short>([5, 6], [70, 80]));
        Assert.Equal(images[2].ReadRawRegion<float>([0, 0, 1], [301, 207, 1]), await images[2].ReadRawRegionAsync<float>([0, 0, 1], [301, 207, 1]));

        var compressed = file.OfType<CompressedImageHdu>().Single();
        Assert.Equal(compressed.ReadPixels<short>(), await compressed.ReadPixelsAsync<short>());
        Assert.Equal(compressed.ReadRawPixels<short>(), await compressed.ReadRawPixelsAsync<short>());
        Assert.Equal(compressed.ReadPixels<double>(), await compressed.ReadPixelsAsync<double>());
    }

    [Fact]
    public async Task Asynchronous_reads_of_planes_return_the_planes()
    {
        var planes = Planes.Float(123, 45, 3);
        var image = ImageHdu.CreateFromPlanes<float>(Planes.AsMemory(planes), 123, 45, 3);
        Assert.Equal(Planes.Concat(planes), await image.ReadPixelsAsync<float>());
        Assert.Equal(planes[2], await image.ReadRegionAsync<float>([0, 0, 2], [123, 45, 1]));
    }

    [Fact]
    public async Task Asynchronous_reads_stop_on_a_cancelled_token()
    {
        using var dir = new ScratchDirectory();
        using var file = FitsFile.Open(WriteFile(dir));
        var image = (ImageHdu)file[0];
        var compressed = file.OfType<CompressedImageHdu>().Single();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.ReadPixelsAsync<ushort>(Cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.ReadPixelsAsync<float>(Cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.ReadRawPixelsAsync<short>(Cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.ReadRegionAsync<float>([0, 0], [10, 10], Cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.ReadRawRegionAsync<short>([0, 0], [10, 10], Cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compressed.ReadPixelsAsync<short>(Cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compressed.ReadRawPixelsAsync<short>(Cancelled));
        Assert.Equal(image.ReadPixels<ushort>(), await image.ReadPixelsAsync<ushort>());   // still readable
    }

    [Fact]
    public async Task An_asynchronous_read_cancelled_while_it_runs_stops_at_the_next_block()
    {
        // 300 000 bytes in a stream that cancels the token on its first read: the read stops before the second chunk of 256 KB.
        using var cts = new CancellationTokenSource();
        using var stream = new AsyncOnlyStream(new byte[300_000], onRead: cts.Cancel);
        var source = new StreamDataSource(stream, 0, 300_000);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BigEndian.ArrayRead<short>(source).RunAsync(source, cts.Token));
        Assert.Equal(1, stream.Reads);
    }

    [Fact]
    public async Task An_asynchronous_region_read_is_cancelled_between_its_blocks()
    {
        using var cts = new CancellationTokenSource();
        int accesses = 0;
        var planes = Enumerable.Range(0, 4).Select(_ => new HookedMemory<float>(new float[300_000], () =>
        {
            if (++accesses == 1) cts.Cancel();
        })).ToList();
        var image = ImageHdu.CreateFromPlanes<float>(planes.Select(h => h.Plane).ToList(), 600, 500, 4);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.ReadRegionAsync<float>([0, 0, 0], [600, 500, 4], cts.Token));
        Assert.Equal(1, accesses);   // the first megabyte only
    }

    [Fact]
    public async Task A_narrow_asynchronous_region_read_in_coalesced_blocks_is_cancelled_between_them()
    {
        // A column 2 pixels wide of a 4-plane cube: 2000 short runs, read together a megabyte at a time.
        using var cts = new CancellationTokenSource();
        int accesses = 0;
        var planes = Enumerable.Range(0, 4).Select(p => new HookedMemory<float>(Enumerable.Range(0, 300_000).Select(i => (float)(p * 1_000_000 + i)).ToArray(), () =>
        {
            if (++accesses == 1) cts.Cancel();
        })).ToList();
        var image = ImageHdu.CreateFromPlanes<float>(planes.Select(h => h.Plane).ToList(), 600, 500, 4);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.ReadRegionAsync<float>([10, 0, 0], [2, 500, 4], cts.Token));
        Assert.Equal(1, accesses);   // the first megabyte only

        // and without cancellation, the runs picked out of the blocks are the right ones
        Assert.Equal(image.ReadRegion<float>([10, 0, 0], [2, 500, 4]), await image.ReadRegionAsync<float>([10, 0, 0], [2, 500, 4]));
    }

    [Fact]
    public async Task Errors_of_the_asynchronous_reads_are_in_the_task()
    {
        var image = ImageHdu.Create<float>(new float[6], 3, 2);
        var task = image.ReadPixelsAsync<int>();          // a float image cannot be read as int: no exception on the call
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        var raw = image.ReadRawPixelsAsync<short>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => raw);
        var region = image.ReadRegionAsync<float>([0], [1]);
        await Assert.ThrowsAsync<ArgumentException>(() => region);
    }

    [Fact]
    public async Task Data_is_read_from_a_stream_with_asynchronous_reads_only()
    {
        byte[] bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 31)).ToArray();
        using var stream = new AsyncOnlyStream(bytes);
        var source = new StreamDataSource(stream, 0, bytes.Length);

        Assert.Equal(new MemoryDataSource(bytes).ReadAll(), await source.ReadAllAsync(default));
        Assert.Equal(BigEndian.ReadArray<short>(new MemoryDataSource(bytes)), await BigEndian.ArrayRead<short>(source).RunAsync(source, default));
        Assert.True(stream.Reads > 1);
    }

    // ---- Saves to a stream -----------------------------------------------------------------------------------------------

    private static async Task<byte[]> SaveAsync(FitsFile file, ChecksumPolicy checksums)
    {
        using var stream = new MemoryStream();
        await file.SaveAsync(stream, checksums);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(ChecksumPolicy.Auto)]
    [InlineData(ChecksumPolicy.Always)]
    [InlineData(ChecksumPolicy.Never)]
    public async Task An_asynchronous_save_writes_the_bytes_of_the_synchronous_one(ChecksumPolicy checksums)
    {
        using var dir = new ScratchDirectory();
        string path = WriteFile(dir);

        // lazily opened files, edited the same way; then saved twice, so that the state committed by the first save is compared too
        using var a = FitsFile.Open(path);
        using var b = FitsFile.Open(path);
        foreach (var f in new[] { a, b })
        {
            f[1].Header.Set("OBSERVER", "someone");
            f.Add(ImageHdu.CreateFromPlanes<ushort>(Planes.AsMemory(Enumerable.Range(0, 3).Select(p => Enumerable.Range(0, 1000).Select(i => (ushort)(p * 1000 + i)).ToArray())), 1000, 3));
        }
        Assert.Equal(Planes.Save(a, checksums), await SaveAsync(b, checksums));
        Assert.Equal(Planes.Save(a, ChecksumPolicy.Auto), await SaveAsync(b, ChecksumPolicy.Auto));

        Assert.Equal(Planes.Save(new FitsFile()), await SaveAsync(new FitsFile(), checksums));
    }

    [Fact]
    public async Task An_unchanged_file_with_checksums_is_written_back_byte_for_byte()
    {
        byte[] original = File.ReadAllBytes(Fixture("checksum.fits"));
        using var file = FitsFile.Open(Fixture("checksum.fits"));
        Assert.Equal(original, await SaveAsync(file, ChecksumPolicy.Auto));
    }

    [Fact]
    public async Task A_file_is_saved_to_a_stream_with_asynchronous_writes_only()
    {
        using var dir = new ScratchDirectory();
        using var file = FitsFile.Open(WriteFile(dir));
        file.Add(ImageHdu.CreateFromPlanes<float>(Planes.AsMemory(Planes.Float(10, 10, 2)), 10, 10, 2));
        byte[] expected = Planes.Save(file, ChecksumPolicy.Always);

        using var stream = new AsyncOnlyStream([]);
        await file.SaveAsync(stream, ChecksumPolicy.Always);
        Assert.Equal(expected, stream.ToArray());
        Assert.True(stream.Writes > file.Count);
    }

    [Fact]
    public async Task An_asynchronous_save_to_a_stream_stops_between_blocks_and_restores_the_headers()
    {
        var image = ImageHdu.Create<double>(new double[1 << 20], 1024, 1024);
        var file = new FitsFile().Add(image);
        long full = Planes.Save(file).Length;

        using var cts = new CancellationTokenSource();
        using var stream = new AsyncOnlyStream([], onWrite: s => { if (s.Length >= 3 << 20) cts.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.SaveAsync(stream, ChecksumPolicy.Always, cts.Token));
        Assert.InRange(stream.Length, 3 << 20, (4 << 20) + 2880);
        Assert.True(stream.Length < full);
        Assert.False(image.Header.Contains("CHECKSUM"));
    }

    // ---- Saves to a path -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(FitsSaveMode.Overwrite)]
    [InlineData(FitsSaveMode.CreateNew)]
    public async Task An_asynchronous_save_to_a_path_writes_the_bytes_of_the_synchronous_one(FitsSaveMode mode)
    {
        using var dir = new ScratchDirectory();
        using var file = FitsFile.Open(WriteFile(dir));
        file[0].Header.Set("OBJECT", "M42");

        file.Save(dir.File("sync.fits"), ChecksumPolicy.Always, mode);
        await file.SaveAsync(dir.File("async.fits"), ChecksumPolicy.Always, mode);

        Assert.Equal(File.ReadAllBytes(dir.File("sync.fits")), File.ReadAllBytes(dir.File("async.fits")));
        Assert.Equal(["all.fits", "async.fits", "sync.fits"], dir.Names());
    }

    [Fact]
    public async Task An_asynchronous_save_over_the_open_source_keeps_the_instance_usable()
    {
        using var dir = new ScratchDirectory();
        string path = WriteFile(dir);
        using var file = FitsFile.Open(path);
        var image = (ImageHdu)file[0];
        ushort[] pixels = image.ReadPixels<ushort>();
        image.Header.Set("OBSERVER", "someone");

        await file.SaveAsync(path);

        Assert.Equal(pixels, await image.ReadPixelsAsync<ushort>());
        Assert.Equal(["all.fits"], dir.Names());
        using var reread = FitsFile.Open(path);
        Assert.Equal("someone", reread[0].Header.Get<string>("OBSERVER"));
    }

    [Theory]
    [InlineData(FitsSaveMode.Overwrite, ChecksumPolicy.Auto)]
    [InlineData(FitsSaveMode.Overwrite, ChecksumPolicy.Always)]
    [InlineData(FitsSaveMode.CreateNew, ChecksumPolicy.Auto)]
    public async Task An_asynchronous_save_cancelled_while_it_runs_leaves_no_file_and_the_target_untouched(FitsSaveMode mode, ChecksumPolicy checksums)
    {
        using var dir = new ScratchDirectory();
        string existing = WriteFile(dir);
        byte[] before = File.ReadAllBytes(existing);
        string target = mode == FitsSaveMode.Overwrite ? existing : dir.File("new.fits");

        using var cts = new CancellationTokenSource();
        int accesses = 0;
        var planes = Planes.Float(400, 300, 3)
            .Select(p => new HookedMemory<float>(p, () =>
            {
                // one access per block of 256 KB: the second block, in the checksum pass for Always, in the write otherwise
                if (++accesses == 2) cts.Cancel();
            }))
            .Select(h => h.Plane)
            .ToList();
        var image = ImageHdu.CreateFromPlanes<float>(planes, 400, 300, 3);
        var file = new FitsFile().Add(image);
        var cards = image.Header.Select(c => c.ToString()).ToList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.SaveAsync(target, checksums, mode, cts.Token));

        Assert.InRange(accesses, 2, 3);
        Assert.Equal(before, File.ReadAllBytes(existing));
        Assert.Equal(["all.fits"], dir.Names());
        Assert.Equal(cards, image.Header.Select(c => c.ToString()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.SaveAsync(target, checksums, mode, Cancelled));
        Assert.Equal(["all.fits"], dir.Names());
    }

    [Fact]
    public async Task An_asynchronous_save_that_cannot_be_put_in_place_is_rolled_back()
    {
        using var dir = new ScratchDirectory();
        string target = dir.File("folder.fits");
        Directory.CreateDirectory(target);   // the temporary file is written, then cannot be moved over a directory
        var image = ImageHdu.Create<short>([1, 2, 3], 3);
        var file = new FitsFile().Add(image);
        var cards = image.Header.Select(c => c.ToString()).ToList();

        await Assert.ThrowsAnyAsync<IOException>(() => file.SaveAsync(target, ChecksumPolicy.Always));

        Assert.True(Directory.Exists(target));
        Assert.Equal(["folder.fits"], dir.Names());                       // no temporary file left
        Assert.Equal(cards, image.Header.Select(c => c.ToString()));      // no CHECKSUM added
    }

    [Fact]
    public async Task CreateNew_refuses_an_existing_file_and_one_that_appears_during_an_asynchronous_save()
    {
        using var dir = new ScratchDirectory();
        string path = dir.File("taken.fits");
        File.WriteAllText(path, "not mine");
        var simple = new FitsFile().Add(ImageHdu.Create<short>([1, 2, 3], 3));
        var e = await Assert.ThrowsAsync<FitsFileExistsException>(() => simple.SaveAsync(path, ChecksumPolicy.Auto, FitsSaveMode.CreateNew));
        Assert.Equal(Path.GetFullPath(path), e.FileName);
        Assert.Equal("not mine", File.ReadAllText(path));

        string race = dir.File("race.fits");
        bool created = false;
        var plane = new HookedMemory<float>(new float[64], () =>
        {
            if (created) return;
            created = true;
            File.WriteAllText(race, "written by someone else");
        });
        var image = ImageHdu.CreateFromPlanes<float>([plane.Plane], 64, 1);
        var file = new FitsFile().Add(image);
        var cards = image.Header.Select(c => c.ToString()).ToList();

        var raced = await Assert.ThrowsAsync<FitsFileExistsException>(() => file.SaveAsync(race, ChecksumPolicy.Always, FitsSaveMode.CreateNew));

        Assert.IsAssignableFrom<IOException>(raced.InnerException);   // refused by the move itself
        Assert.Equal("written by someone else", File.ReadAllText(race));
        Assert.Equal(["race.fits", "taken.fits"], dir.Names());
        Assert.Equal(cards, image.Header.Select(c => c.ToString()));
    }

    [Fact]
    public async Task Concurrent_asynchronous_saves_in_CreateNew_mode_create_the_file_once()
    {
        using var dir = new ScratchDirectory();
        for (int round = 0; round < 10; round++)
        {
            string path = dir.File($"shared-{round}.fits");
            var files = Enumerable.Range(0, 4).Select(k => new FitsFile().Add(ImageHdu.Create<int>(Enumerable.Repeat(k, 50_000).ToArray(), 50_000))).ToList();
            var saves = files.Select(f => Task.Run(() => f.SaveAsync(path, ChecksumPolicy.Always, FitsSaveMode.CreateNew))).ToList();
            var outcome = await Task.WhenAll(saves.Select(async t =>
            {
                try
                {
                    await t;
                    return (Exception?)null;
                }
                catch (Exception e)
                {
                    return e;
                }
            }));

            Assert.Equal(1, outcome.Count(e => e is null));                    // one save created the file
            Assert.All(outcome.Where(e => e is not null), e => Assert.IsType<FitsFileExistsException>(e));
            int winner = Array.FindIndex(outcome, e => e is null);
            Assert.Equal(Planes.Save(files[winner]), File.ReadAllBytes(path)); // the file is the winner's, whole
        }
        Assert.Equal(10, dir.Names().Length);                                   // no temporary file left
    }

    /// <summary>A stream whose synchronous Read and Write fail: only the asynchronous calls work.</summary>
    private sealed class AsyncOnlyStream : MemoryStream
    {
        private readonly Action? _onRead;
        private readonly Action<AsyncOnlyStream>? _onWrite;
        private bool _inside;

        public AsyncOnlyStream(byte[] content, Action? onRead = null, Action<AsyncOnlyStream>? onWrite = null)
        {
            _onRead = onRead;
            _onWrite = onWrite;
            base.Write(content, 0, content.Length);
            Position = 0;
        }

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        private T Inside<T>(Func<T> action)
        {
            _inside = true;
            try
            {
                return action();
            }
            finally
            {
                _inside = false;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inside ? base.Read(buffer, offset, count) : throw new InvalidOperationException("synchronous read");

        public override int Read(Span<byte> buffer) =>
            _inside ? base.Read(buffer) : throw new InvalidOperationException("synchronous read");

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!_inside) throw new InvalidOperationException("synchronous write");
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (!_inside) throw new InvalidOperationException("synchronous write");
            base.Write(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            _onRead?.Invoke();
            return ValueTask.FromResult(Inside(() => base.Read(buffer.Span)));
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes++;
            Inside(() =>
            {
                base.Write(buffer.Span);
                return 0;
            });
            _onWrite?.Invoke(this);
            return ValueTask.CompletedTask;
        }
    }
}
