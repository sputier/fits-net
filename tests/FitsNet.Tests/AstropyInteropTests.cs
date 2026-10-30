using System.Diagnostics;
using FitsNet.Compression;

namespace FitsNet.Tests;

/// <summary>Files written by FitsNet must be accepted by astropy (needs python with astropy; FITSNET_SKIP_INTEROP=1 opts out).</summary>
public class AstropyInteropTests
{
    private static string ToolPath(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tools", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(name);
    }

    private static (int ExitCode, string Output) Python(params string[] args)
    {
        var psi = new ProcessStartInfo("python") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-I");
        foreach (string a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (-1, "python not found");
        }
    }

    private static void WriteCompressed(string dir)
    {
        const int n = 37 * 23;
        short[] ramp = Enumerable.Range(0, n).Select(i => (short)((i * 31) % 4000 - 2000 + (i % 7))).ToArray();
        float[] flt = Enumerable.Range(0, n).Select(i => (float)(1000 + 50 * Math.Sin(i / 40.0) + ((i * 7919) % 1000) / 250.0)).ToArray();
        long[] axes = [37, 23];

        foreach (var (name, algo) in new[]
                 {
                     ("rice", CompressionAlgorithm.Rice), ("gzip1", CompressionAlgorithm.Gzip1),
                     ("gzip2", CompressionAlgorithm.Gzip2), ("none", CompressionAlgorithm.NoCompress),
                 })
        {
            var comp = CompressedImageHdu.Create<short>(ramp, axes, new CompressionOptions { Algorithm = algo, TileShape = [16, 8] });
            new FitsFile().Add(new ImageHdu()).Add(comp).Save(Path.Combine(dir, $"c_{name}_i16.fits"));
        }

        var lossless = CompressedImageHdu.Create<float>(flt, axes,
            new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip2, Quantization = QuantizationMethod.None, TileShape = [16, 8] });
        new FitsFile().Add(new ImageHdu()).Add(lossless).Save(Path.Combine(dir, "c_gzip2_f32_lossless.fits"));

        foreach (var (name, method) in new[]
                 {
                     ("nodither", QuantizationMethod.NoDither), ("dither1", QuantizationMethod.SubtractiveDither1),
                     ("dither2", QuantizationMethod.SubtractiveDither2),
                 })
        {
            var comp = CompressedImageHdu.Create<float>(flt, axes,
                new CompressionOptions { Quantization = method, QuantizeLevel = 8, TileShape = [16, 8], DitherSeed = 9 });
            new FitsFile().Add(new ImageHdu()).Add(comp).Save(Path.Combine(dir, $"c_{name}_f32.fits"));
            var bytes = new byte[n * 4];
            float[] decoded = comp.ReadPixels<float>();
            for (int i = 0; i < n; i++) System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(bytes.AsSpan(i * 4), decoded[i]);
            File.WriteAllBytes(Path.Combine(dir, $"c_{name}_f32.raw"), bytes);
        }

        int[] mask = new int[n];
        for (int i = 0; i < n; i++) mask[i] = i % 37 < 10 ? 0 : (i / 37) % 3 == 0 ? 7 : 300;
        var plio = CompressedImageHdu.Create<int>(mask, axes, new CompressionOptions { Algorithm = CompressionAlgorithm.Plio, TileShape = [16, 8] });
        new FitsFile().Add(new ImageHdu()).Add(plio).Save(Path.Combine(dir, "c_plio.fits"));

        int[] i32 = Enumerable.Range(0, n).Select(i => (i * 977) % 100000 - 50000 + i).ToArray();
        var hc = CompressedImageHdu.Create<int>(i32, axes, new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [16, 16] });
        new FitsFile().Add(new ImageHdu()).Add(hc).Save(Path.Combine(dir, "c_hcomp_i32.fits"));

        var lossy = CompressedImageHdu.Create<int>(i32, axes,
            new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [16, 16], HCompressScale = 25 });
        new FitsFile().Add(new ImageHdu()).Add(lossy).Save(Path.Combine(dir, "c_hcomp_lossy.fits"));
        var lossyRaw = new byte[n * 4];
        int[] lossyPixels = lossy.ReadPixels<int>();
        for (int i = 0; i < n; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(lossyRaw.AsSpan(i * 4), lossyPixels[i]);
        File.WriteAllBytes(Path.Combine(dir, "c_hcomp_lossy.raw"), lossyRaw);

        var hcf = CompressedImageHdu.Create<float>(flt, axes,
            new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [16, 16], QuantizeLevel = 8, HCompressScale = 2 });
        new FitsFile().Add(new ImageHdu()).Add(hcf).Save(Path.Combine(dir, "c_hcomp_f32.fits"));
        var hcfRaw = new byte[n * 4];
        float[] hcfPixels = hcf.ReadPixels<float>();
        for (int i = 0; i < n; i++) System.Buffers.Binary.BinaryPrimitives.WriteSingleBigEndian(hcfRaw.AsSpan(i * 4), hcfPixels[i]);
        File.WriteAllBytes(Path.Combine(dir, "c_hcomp_f32.raw"), hcfRaw);

        var u16 = CompressedImageHdu.Create<ushort>([0, 1, 65535, 40000], [2, 2]);
        new FitsFile().Add(new ImageHdu()).Add(u16).Save(Path.Combine(dir, "c_u16.fits"));
    }

    [Fact]
    public void Astropy_reads_compressed_versions_of_a_real_camera_image()
    {
        var (code, _) = Python("-c", "import astropy, numpy");
        if (code != 0)
        {
            Assert.True(Environment.GetEnvironmentVariable("FITSNET_SKIP_INTEROP") == "1",
                "python with astropy+numpy is required for this test (set FITSNET_SKIP_INTEROP=1 to skip it).");
            return;
        }

        string original = TestData.Vespera("img-0022-M42-broadband.fits");
        string dir = Path.Combine(Path.GetTempPath(), "fitsnet-real-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var source = FitsFile.Open(original);
            var image = (ImageHdu)source.Primary;
            foreach (var (name, algorithm) in new[]
                     {
                         ("rice", CompressionAlgorithm.Rice), ("gzip1", CompressionAlgorithm.Gzip1), ("gzip2", CompressionAlgorithm.Gzip2),
                         ("hcompress", CompressionAlgorithm.HCompress), ("nocompress", CompressionAlgorithm.NoCompress),
                     })
                new FitsFile().Add(new ImageHdu()).Add(image.Compress(new CompressionOptions { Algorithm = algorithm }))
                    .Save(Path.Combine(dir, $"real_{name}.fits"), ChecksumPolicy.Always);

            var (exit, output) = Python(ToolPath("verify_real_with_astropy.py"), dir, original);
            Assert.True(exit == 0, output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Astropy_reads_what_FitsNet_writes()
    {
        var (code, _) = Python("-c", "import astropy, numpy");
        if (code != 0)
        {
            // xUnit 2 cannot skip dynamically; opt out explicitly rather than pass without checking anything.
            Assert.True(Environment.GetEnvironmentVariable("FITSNET_SKIP_INTEROP") == "1",
                "python with astropy+numpy is required for this test (set FITSNET_SKIP_INTEROP=1 to skip it).");
            return;
        }

        string dir = Path.Combine(Path.GetTempPath(), "fitsnet-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var primary = ImageHdu.Create<float>([1f, 2f, 3f, 4f, 5f, 6f], 3, 2);
            primary.Header.Set("OBSERVER", "O'Brien", "who");
            var i16 = ImageHdu.Create<short>([-1, 0, 32767], 3);
            i16.Header.Set("EXPTIME", 12.5, "seconds").Set("LONGKEY", new string('x', 150));
            var file = new FitsFile()
                .Add(primary)
                .Add(i16)
                .Add(ImageHdu.Create<ushort>([0, 1, 65535], 3))
                .Add(ImageHdu.Create<ulong>([0, 1UL << 63, ulong.MaxValue], 3))
                .Add(ImageHdu.Create<double>([0, 1, 2, 3, 4, 5, 6, 7], 2, 2, 2));
            file.Save(Path.Combine(dir, "written.fits"));

            new FitsFile().Add(RandomGroupsHdu.Create<float>(
                [1f, 10f, 0f, 1f, 2f, 3f, 2f, 20f, 4f, 5f, 6f, 7f], 2, 2, 2, 2).Also(g => g.Header.Set("PTYPE1", "U").Set("PTYPE2", "V")))
                .Save(Path.Combine(dir, "groups.fits"));

            var events = BinaryTableHdu.Create(
            [
                TableColumn.Create<int>("ID", [1, -2, 3], unit: "count"),
                TableColumn.CreateLogical("FLAG", [true, false, false]),
                TableColumn.CreateBits("BITS", [true, false, false, false, false, false, false, true, ..new bool[16]], 8),
                TableColumn.CreateString("NAME", ["abc", "de", ""], 5),
                TableColumn.Create<ushort>("U16", [0, 1, 65535]),
                TableColumn.Create<ulong>("U64", [0, 1UL << 63, ulong.MaxValue]),
                TableColumn.Create<float>("XY", [1, 2, 3, 4, 5, 6], repeat: 2),
                TableColumn.CreateComplex("CPLX", [new System.Numerics.Complex(1, 2), new System.Numerics.Complex(3, -4), 0]),
                TableColumn.Create<int>("GRID", [.. Enumerable.Range(0, 18)], repeat: 6, dimensions: [3, 2]),
                TableColumn.CreateVariable<int>("VARJ", [[1, 2, 3], [4], []]),
                TableColumn.CreateVariable<double>("VARQ", [[0.5], [], [1.5, 2.5]], use64BitDescriptors: true),
            ]);
            events.Header.Set("EXTNAME", "EVT");
            var ascii = AsciiTableHdu.Create(
            [
                TableColumn.CreateAsciiString("NAME", ["alpha", "beta", "gamma"]),
                TableColumn.CreateAsciiInteger("N", [1, -22, 333]),
                TableColumn.CreateAsciiFloat("X", [1.5, -2.25, 1000.125], "F10.3"),
                TableColumn.CreateAsciiFloat("Y", [1.5e10, -2.5e-5, 0], "E15.7"),
            ]);
            new FitsFile().Add(new ImageHdu()).Add(events).Add(ascii).Save(Path.Combine(dir, "tables.fits"));

            new FitsFile()
                .Add(ImageHdu.Create<short>([1, 2, 3, 4, 5, 6], 3, 2))
                .Add(ImageHdu.Create<float>([0.5f, 1.5f], 2))
                .Add(BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2, 3]), TableColumn.CreateString("S", ["x", "yy", "zzz"], 4)]))
                .Add(AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("N", [1, 22, 333]), TableColumn.CreateAsciiString("T", ["a", "bb", "ccc"])]))
                .Save(Path.Combine(dir, "checksums.fits"), ChecksumPolicy.Always);

            WriteCompressed(dir);

            var (exit, output) = Python(ToolPath("verify_with_astropy.py"), dir);
            Assert.True(exit == 0, output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
