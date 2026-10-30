using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FitsNet.Compression;

namespace FitsNet.Tests;

/// <summary>Locates the sample data of the repository (<c>data/</c> at its root).</summary>
internal static class TestData
{
    public static string Vespera(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "data", "vespera", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"data/vespera/{name} was not found above {AppContext.BaseDirectory}");
    }
}

/// <summary>
/// Tests on real raw images from a Vaonis Vespera Pro 2 (3536 x 3536, 12 bit data stored as unsigned 16 bit).
/// The reference values were computed with astropy.
/// </summary>
public class RealFilesTests
{
    public static IEnumerable<object[]> Images() =>
    [
        ["img-0022-M42-broadband.fits", 105, 4095, 3751898317UL, new[] { 251, 384, 247, 251, 236 }, "2d79f73b6496adba", new[] { 217, 362, 221, 367, 335, 225, 300, 214, 223, 360, 242, 355 }, "M42", null!],
        ["img-0078-M45.fits", 0, 4095, 3957094706UL, new[] { 253, 474, 220, 224, 236 }, "54de793248fdb230", new[] { 308, 352, 246, 366, 284, 204, 309, 244, 298, 350, 252, 347 }, "M45", null!],
        ["img-1200-M42-dualband.fits", 176, 4095, 4898344766UL, new[] { 358, 447, 364, 348, 287 }, "8de57d98ed2483ae", new[] { 299, 440, 329, 413, 458, 300, 427, 325, 289, 410, 316, 415 }, "M42", "DUAL"],
    ];

    [Theory]
    [MemberData(nameof(Images))]
    public void Pixels_and_header_match_astropy(string file, int min, int max, ulong sum, int[] samples, string sha, int[] region, string obj, string? filter)
    {
        using var f = FitsFile.Open(TestData.Vespera(file), new FitsReadOptions(true));
        var image = Assert.IsType<ImageHdu>(f.Primary);
        Assert.Equal(BitPix.Int16, image.BitPix);
        Assert.Equal(new long[] { 3536, 3536 }, image.Axes.ToArray());
        Assert.Equal(32768L, image.Header.Get<long>("BZERO"));

        ushort[] pixels = image.ReadPixels<ushort>();
        Assert.Equal(3536 * 3536, pixels.Length);
        Assert.Equal(min, pixels.Min());
        Assert.Equal(max, pixels.Max());
        ulong total = 0;
        foreach (ushort p in pixels) total += p;
        Assert.Equal(sum, total);
        int[] at = [0, 1, 12345, 3536 * 1768 + 1768, pixels.Length - 1];
        Assert.Equal(samples, at.Select(i => (int)pixels[i]));
        if (BitConverter.IsLittleEndian)
            Assert.StartsWith(sha, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(pixels.AsSpan()))).ToLowerInvariant());

        // header cards written by the camera
        Assert.Equal(obj, image.Header.Get<string>("OBJECT"));
        Assert.Equal("GBRG", image.Header.Get<string>("BAYERPAT"));
        Assert.InRange(image.Header.Get<double>("EXPOSURE"), 0.1, 600);
        Assert.InRange(image.Header.Get<int>("GAIN"), 1, 1000);
        Assert.True(image.Header.TryGetValue("HCG", out bool _));
        Assert.Equal(2.0, image.Header.Get<double>("PIXSZ"));
        Assert.StartsWith("2026-", image.Header.Get<string>("DATE-OBS"));
        Assert.Contains("vesperapro2", image.Header.Get<string>("INSTRUME"));
        Assert.Equal(filter, image.Header.Get<string>("FILTER"));

        // a region read from the file equals the same region of the whole image
        Assert.Equal(region, image.ReadRegion<ushort>([100, 200], [4, 3]).Select(v => (int)v));
    }

    [Theory]
    [InlineData("img-0022-M42-broadband.fits")]
    [InlineData("img-0078-M45.fits")]
    [InlineData("img-1200-M42-dualband.fits")]
    public void Real_images_survive_read_save_read_with_identical_pixels_and_valid_checksums(string file)
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-real-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            using (var f = FitsFile.Open(TestData.Vespera(file)))
                f.Save(path, ChecksumPolicy.Always);
            using var original = FitsFile.Open(TestData.Vespera(file));
            using var copy = FitsFile.Open(path, new FitsReadOptions(true));
            Assert.Equal(((ImageHdu)original[0]).ReadRawPixels<short>(), ((ImageHdu)copy[0]).ReadRawPixels<short>());
            Assert.All(copy.VerifyChecksums(), r => Assert.Equal(new ChecksumResult(ChecksumStatus.Valid, ChecksumStatus.Valid), r));
            Assert.Equal(original[0].Header.Get<string>("OBJECT"), copy[0].Header.Get<string>("OBJECT"));
            Assert.Equal(new FileInfo(TestData.Vespera(file)).Length, new FileInfo(path).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Rice)]
    [InlineData(CompressionAlgorithm.Gzip1)]
    [InlineData(CompressionAlgorithm.Gzip2)]
    [InlineData(CompressionAlgorithm.HCompress)]
    [InlineData(CompressionAlgorithm.NoCompress)]
    public void Real_image_compresses_losslessly_with_every_algorithm(CompressionAlgorithm algorithm)
    {
        using var f = FitsFile.Open(TestData.Vespera("img-0022-M42-broadband.fits"));
        var image = (ImageHdu)f.Primary;
        ushort[] expected = image.ReadPixels<ushort>();

        var comp = image.Compress(new CompressionOptions { Algorithm = algorithm });
        Assert.Equal(expected, comp.ReadPixels<ushort>());

        // through a file as well
        var ms = new MemoryStream();
        new FitsFile().Add(comp).Save(ms);
        ms.Position = 0;
        using var back = FitsFile.Read(ms, new FitsReadOptions(true));
        Assert.Equal(expected, ((CompressedImageHdu)back[1]).ReadPixels<ushort>());
        Assert.Equal("GBRG", ((CompressedImageHdu)back[1]).Header.Get<string>("BAYERPAT"));

        long raw = (long)expected.Length * 2;
        if (algorithm != CompressionAlgorithm.NoCompress) Assert.True(comp.DataLength < raw * 0.8, $"{algorithm}: {comp.DataLength} of {raw} bytes");
    }

    [Fact]
    public void Real_image_as_a_plio_mask_of_physical_values_roundtrips()
    {
        using var f = FitsFile.Open(TestData.Vespera("img-0078-M45.fits"));
        ushort[] pixels = ((ImageHdu)f.Primary).ReadPixels<ushort>();
        int[] physical = pixels.Select(v => (int)v).ToArray();
        var comp = CompressedImageHdu.Create<int>(physical, [3536, 3536], new CompressionOptions { Algorithm = CompressionAlgorithm.Plio });
        Assert.Equal(physical, comp.ReadPixels<int>());
    }

    [Fact]
    public void Real_image_quantized_to_float_stays_within_the_quantization_step()
    {
        using var f = FitsFile.Open(TestData.Vespera("img-1200-M42-dualband.fits"));
        float[] pixels = ((ImageHdu)f.Primary).ReadPixels<float>();
        var comp = CompressedImageHdu.Create<float>(pixels, [3536, 3536], new CompressionOptions { TileShape = [3536, 16] });
        float[] back = comp.ReadPixels<float>();
        double step = comp.ReadColumn<double>("ZSCALE").Max();
        Assert.InRange(step, 1e-3, 100);
        for (int i = 0; i < pixels.Length; i += 7) Assert.InRange(Math.Abs(pixels[i] - back[i]), 0, step * 0.51 + 1e-3);
    }

    [Fact]
    public void Compression_of_a_real_image_is_identical_sequentially_and_in_parallel()
    {
        using var f = FitsFile.Open(TestData.Vespera("img-0022-M42-broadband.fits"));
        var image = (ImageHdu)f.Primary;
        var a = image.Compress(new CompressionOptions { MaxDegreeOfParallelism = 1 });
        var b = image.Compress(new CompressionOptions { MaxDegreeOfParallelism = -1 });
        Assert.Equal(a.Source.ReadAll(), b.Source.ReadAll());
    }

    [Fact]
    public void A_cutout_of_a_real_image_keeps_its_header_and_a_consistent_crpix()
    {
        using var f = FitsFile.Open(TestData.Vespera("img-1200-M42-dualband.fits"));
        var image = (ImageHdu)f.Primary;
        var cut = image.Subimage([1000, 2000], [512, 256]);
        Assert.Equal(new long[] { 512, 256 }, cut.Axes.ToArray());
        Assert.Equal("M42", cut.Header.Get<string>("OBJECT"));
        ushort[] whole = image.ReadPixels<ushort>();
        ushort[] part = cut.ReadPixels<ushort>();
        for (int y = 0; y < 256; y += 37)
            for (int x = 0; x < 512; x += 53)
                Assert.Equal(whole[(2000 + y) * 3536 + 1000 + x], part[y * 512 + x]);
    }
}
