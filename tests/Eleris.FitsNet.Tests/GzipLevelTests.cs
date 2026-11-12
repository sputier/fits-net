using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

public class GzipLevelTests
{
    private static ImageHdu Image()
    {
        var rng = new Random(6);
        var pixels = new ushort[300 * 120];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (ushort)(1000 + 200 * Math.Sin(i / 700.0) + rng.Next(-25, 26));
        return ImageHdu.Create<ushort>(pixels, 300, 120);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Gzip1, 0)]
    [InlineData(CompressionAlgorithm.Gzip1, 1)]
    [InlineData(CompressionAlgorithm.Gzip1, 2)]
    [InlineData(CompressionAlgorithm.Gzip1, 9)]
    [InlineData(CompressionAlgorithm.Gzip2, 0)]
    [InlineData(CompressionAlgorithm.Gzip2, 3)]
    [InlineData(CompressionAlgorithm.Gzip2, 9)]
    public void Every_level_gives_a_file_that_reads_back_to_the_same_pixels(CompressionAlgorithm algorithm, int level)
    {
        var image = Image();
        var compressed = image.Compress(new CompressionOptions { Algorithm = algorithm, GzipLevel = level });
        Assert.Equal(image.ReadPixels<ushort>(), compressed.Decompress().ReadPixels<ushort>());
    }

    [Fact]
    public void The_level_changes_the_size_of_the_data_and_the_default_is_the_default_of_the_runtime()
    {
        var image = Image();
        long Size(int level) => image.Compress(new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip2, GzipLevel = level }).DataLength;
        Assert.True(Size(0) > Size(9) * 1.3, "level 0 stores the bytes, level 9 compresses them");
        Assert.True(Size(1) >= Size(-1), "the default level compresses at least as well as the fastest");
        Assert.Equal(Size(-1), image.Compress(new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip2 }).DataLength);
    }

    [Fact]
    public void Floating_point_pixels_that_are_not_quantized_use_the_level_too()
    {
        var pixels = Enumerable.Range(0, 3000).Select(i => (float)Math.Sin(i / 50.0)).ToArray();
        var image = ImageHdu.Create<float>(pixels, 3000);
        long Size(int level) => image.Compress(new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip1, Quantization = QuantizationMethod.None, GzipLevel = level }).DataLength;
        Assert.True(Size(0) > Size(9));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(10)]
    public void A_level_out_of_range_is_refused(int level) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Image().Compress(new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip1, GzipLevel = level }));
}
