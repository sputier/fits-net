using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>
/// Guards the memory behaviour that the optimisations bought: reading and writing pixels allocate the result and nothing else, the
/// codecs work tile by tile in pooled buffers, a checksum allocates nothing. The bytes allocated by the thread are counted exactly, so
/// the limits are tight and the tests do not depend on the machine. Times are not tested here: they depend too much on the machine
/// and on its load (they are measured by the benchmarks in <c>benchmarks/</c>), but a regression to an intermediate array, which is
/// what makes an operation slower and heavier at the same time, is caught by these.
/// </summary>
public class PerformanceGuardTests
{
    // 4096 x 2048 pixels of 16 bits: 16 MB, the size of a frame of a small camera.
    private const int Width = 4096, Height = 2048;
    private const long DataBytes = (long)Width * Height * 2;

    private static readonly Lazy<ushort[]> Pixels = new(() =>
    {
        var random = new Random(1);
        var pixels = new ushort[Width * Height];
        // a smooth sky with noise: what the codecs meet in real frames
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(1000 + 200 * Math.Sin(i / 5000.0) + random.Next(-20, 21));
        return pixels;
    });

    private static ImageHdu Image() => ImageHdu.Create<ushort>(Pixels.Value, Width, Height);

    /// <summary>The bytes the current thread allocates in one call (after one call to warm up the pools, the tables and the JIT).</summary>
    private static long Allocated(Action action)
    {
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void AssertBelow(string what, long allocated, double limitInBytes)
    {
        Assert.True(allocated < limitInBytes, $"{what} allocated {allocated:N0} bytes, {allocated / (double)DataBytes:F2} times the data; the limit is {limitInBytes / DataBytes:F2} times.");
    }

    [Fact]
    public void Reading_pixels_allocates_the_result_and_nothing_else()
    {
        var image = Image();
        AssertBelow("ReadPixels<ushort>", Allocated(() => image.ReadPixels<ushort>()), DataBytes * 1.1);
        AssertBelow("ReadRawPixels<short>", Allocated(() => image.ReadRawPixels<short>()), DataBytes * 1.1);
        AssertBelow("ReadPixels<float>", Allocated(() => image.ReadPixels<float>()), DataBytes * 2 * 1.1);
        AssertBelow("ReadPixels<double>", Allocated(() => image.ReadPixels<double>()), DataBytes * 4 * 1.1);
    }

    [Fact]
    public void Reading_from_a_file_does_not_copy_the_whole_data_first()
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-perf-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            new FitsFile().Add(Image()).Save(path);
            using var file = FitsFile.Open(path);
            var image = (ImageHdu)file.Primary;
            AssertBelow("ReadPixels<ushort> from a file", Allocated(() => image.ReadPixels<ushort>()), DataBytes * 1.1);
            AssertBelow("ReadPixels<float> from a file", Allocated(() => image.ReadPixels<float>()), DataBytes * 2 * 1.1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Creating_an_image_allocates_the_bytes_once()
    {
        AssertBelow("Create<ushort>", Allocated(() => ImageHdu.Create<ushort>(Pixels.Value, Width, Height)), DataBytes * 1.1);
        short[] shorts = Pixels.Value.Select(v => (short)v).ToArray();
        AssertBelow("Create<short>", Allocated(() => ImageHdu.Create<short>(shorts, Width, Height)), DataBytes * 1.1);
        float[] floats = Pixels.Value.Select(v => v * 0.37f).ToArray();
        AssertBelow("Create<float>", Allocated(() => ImageHdu.Create<float>(floats, Width, Height)), DataBytes * 2 * 1.1);
    }

    [Fact]
    public void Summing_the_words_of_the_checksum_allocates_nothing()
    {
        byte[] data = new byte[DataBytes];
        new Random(3).NextBytes(data);
        Assert.True(Allocated(() => Checksum.Sum(data)) < 1024, "Checksum.Sum allocated");
    }

    [Fact]
    public void Tile_compression_stays_within_a_few_times_the_size_of_the_image()
    {
        var image = Image();
        var rice = new CompressionOptions { Algorithm = CompressionAlgorithm.Rice, MaxDegreeOfParallelism = 1 };   // one thread: the tiles run on this one
        var compressed = image.Compress(rice);
        // measured: 1.4 times the data for the decompression (the result and the compressed cells) and 3.8 for the compression
        AssertBelow("Rice decompression", Allocated(() => compressed.Decompress(1)), DataBytes * 2.0);
        AssertBelow("Rice compression", Allocated(() => image.Compress(rice)), DataBytes * 5);

        var floats = ImageHdu.Create<float>(Pixels.Value.Select(v => v * 0.37f).ToArray(), Width, Height);
        var quantized = floats.Compress(new CompressionOptions { TileShape = [Width, 16], MaxDegreeOfParallelism = 1 });
        // measured: 1.15 times the float data
        AssertBelow("quantized float decompression", Allocated(() => quantized.Decompress(1)), DataBytes * 2 * 1.6);
    }

    [Fact]
    public void ASCII_columns_are_parsed_without_a_string_for_each_cell()
    {
        const int rows = 100_000;
        var rng = new Random(5);
        var table = AsciiTableHdu.Create(
        [
            TableColumn.CreateAsciiFloat("X", Enumerable.Range(0, rows).Select(_ => rng.NextDouble() * 1000).ToArray(), "F12.5"),
            TableColumn.CreateAsciiInteger("N", Enumerable.Range(0, rows).Select(i => (long?)i).ToArray()),
        ]);
        Assert.True(Allocated(() => table.ReadColumn<double>("X")) < rows * 8 * 1.5, "an F column");
        Assert.True(Allocated(() => table.ReadIntegerColumn(1)) < rows * 16 * 1.5, "an integer column");
    }
}
