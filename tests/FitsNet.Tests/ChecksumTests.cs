using System.Globalization;
using System.Text;

namespace FitsNet.Tests;

public class ChecksumTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static FitsFile Sample() => new FitsFile()
        .Add(ImageHdu.Create<short>([1, 2, 3, 4, 5, 6], 3, 2))
        .Add(ImageHdu.Create<float>([0.5f, 1.5f], 2))
        .Add(BinaryTableHdu.Create([TableColumn.Create<int>("A", [1, 2, 3]), TableColumn.CreateString("S", ["x", "yy", "zzz"], 4)]))
        .Add(AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("N", [1, 22, 333]), TableColumn.CreateAsciiString("T", ["a", "bb", "ccc"])]));

    private static byte[] SaveBytes(FitsFile f, ChecksumPolicy policy)
    {
        var ms = new MemoryStream();
        f.Save(ms, policy);
        return ms.ToArray();
    }

    [Fact]
    public void Encoding_matches_astropy_for_reference_values()
    {
        string[] lines = File.ReadAllLines(Fixture("checksum_vectors.txt"));
        Assert.True(lines.Length >= 10);
        foreach (string line in lines)
        {
            var parts = line.Split(' ');
            uint value = uint.Parse(parts[0], CultureInfo.InvariantCulture);
            Assert.Equal(parts[1], Checksum.Encode(value, complement: false));
        }
    }

    [Fact]
    public void Encoded_checksums_never_contain_punctuation()
    {
        var rng = new Random(1234);
        for (int i = 0; i < 20_000; i++)
        {
            string s = Checksum.Encode((uint)rng.NextInt64(0, 1L << 32), complement: true);
            Assert.Equal(16, s.Length);
            Assert.All(s, c => Assert.True(char.IsAsciiLetterOrDigit(c), s));
        }
    }

    [Fact]
    public void Sum_uses_end_around_carry()
    {
        Assert.Equal(1u, Checksum.Sum([0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 1]));
        Assert.Equal(0xFFFFFFFFu, Checksum.Sum([0xFF, 0xFF, 0xFF, 0xFF]));
        Assert.Equal(0x01020300u, Checksum.Sum([1, 2, 3])); // a partial word is zero padded
        Assert.Equal(6u, Checksum.Sum([0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3]));
    }

    [Fact]
    public void ChecksumStream_gives_the_same_result_whatever_the_chunking()
    {
        var data = new byte[10_007];
        new Random(7).NextBytes(data);
        uint expected = Checksum.Sum(data);
        foreach (int chunk in new[] { 1, 2, 3, 4, 5, 7, 64, 1000, 10_007 })
        {
            using var s = new ChecksumStream();
            for (int i = 0; i < data.Length; i += chunk) s.Write(data.AsSpan(i, Math.Min(chunk, data.Length - i)));
            Assert.Equal(expected, s.Result);
        }
    }

    [Fact]
    public void Verifies_checksums_written_by_astropy_for_every_hdu_type()
    {
        using var f = FitsFile.Open(Fixture("checksum.fits"));
        Assert.Equal(4, f.Count);
        Assert.All(f.VerifyChecksums(), r => Assert.Equal(new ChecksumResult(ChecksumStatus.Valid, ChecksumStatus.Valid), r));
    }

    [Fact]
    public void Reports_corruption_of_the_data_and_only_in_the_damaged_hdu()
    {
        using var f = FitsFile.Open(Fixture("checksum_corrupt.fits"));
        var results = f.VerifyChecksums();
        Assert.Equal(new ChecksumResult(ChecksumStatus.Invalid, ChecksumStatus.Invalid), results[0]);
        Assert.All(results.Skip(1), r => Assert.Equal(new ChecksumResult(ChecksumStatus.Valid, ChecksumStatus.Valid), r));
    }

    [Fact]
    public void Header_corruption_invalidates_checksum_but_not_datasum()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("checksum.fits"));
        int at = Encoding.ASCII.GetString(bytes, 0, 2880).IndexOf("checksum test", StringComparison.Ordinal);
        Assert.True(at > 0);
        bytes[at] = (byte)'C';
        using var f = FitsFile.Read(new MemoryStream(bytes));
        Assert.Equal(new ChecksumResult(ChecksumStatus.Invalid, ChecksumStatus.Valid), f[0].VerifyChecksum());
    }

    [Fact]
    public void Files_without_checksum_keywords_report_missing()
    {
        using var f = FitsFile.Open(Fixture("multi.fits"));
        Assert.All(f.VerifyChecksums(), r => Assert.Equal(new ChecksumResult(ChecksumStatus.Missing, ChecksumStatus.Missing), r));
    }

    [Fact]
    public void Always_writes_valid_checksums_in_every_hdu()
    {
        var bytes = SaveBytes(Sample(), ChecksumPolicy.Always);
        using var back = FitsFile.Read(new MemoryStream(bytes), new FitsReadOptions(true));
        Assert.Equal(4, back.Count);
        Assert.All(back.VerifyChecksums(), r => Assert.Equal(new ChecksumResult(ChecksumStatus.Valid, ChecksumStatus.Valid), r));
        Assert.Equal(16, back[1].Header.Get<string>("CHECKSUM")!.Length);
    }

    [Fact]
    public void The_whole_file_sums_to_minus_zero_in_every_hdu()
    {
        byte[] bytes = SaveBytes(Sample(), ChecksumPolicy.Always);
        using var f = FitsFile.Read(new MemoryStream(bytes));
        // Each HDU (header + data) must sum to 0xFFFFFFFF on its own.
        long offset = 0;
        foreach (var hdu in f)
        {
            long length = hdu.Header.SerializedLength + HduLayout.Padded(hdu.DataLength);
            Assert.Equal(0xFFFFFFFFu, Checksum.Sum(bytes.AsSpan((int)offset, (int)length)));
            offset += length;
        }
        Assert.Equal(bytes.Length, offset);
    }

    [Fact]
    public void Auto_policy_refreshes_existing_checksums_after_edits_and_leaves_others_alone()
    {
        using var f = FitsFile.Open(Fixture("checksum.fits"));
        ((ImageHdu)f[0]).SetPixels<int>([1, 2, 3], 3);
        f[1].Header.Set("EDITED", true);
        var bytes = SaveBytes(f, ChecksumPolicy.Auto);
        using var back = FitsFile.Read(new MemoryStream(bytes));
        Assert.All(back.VerifyChecksums(), r => Assert.Equal(new ChecksumResult(ChecksumStatus.Valid, ChecksumStatus.Valid), r));
        Assert.Equal(new[] { 1, 2, 3 }, ((ImageHdu)back[0]).ReadPixels<int>());

        var plain = SaveBytes(new FitsFile().Add(ImageHdu.Create<byte>([1], 1)), ChecksumPolicy.Auto);
        using var plainBack = FitsFile.Read(new MemoryStream(plain));
        Assert.False(plainBack[0].Header.Contains("CHECKSUM"));
        Assert.False(plainBack[0].Header.Contains("DATASUM"));
    }

    [Fact]
    public void Never_policy_removes_both_keywords()
    {
        using var f = FitsFile.Open(Fixture("checksum.fits"));
        var bytes = SaveBytes(f, ChecksumPolicy.Never);
        using var back = FitsFile.Read(new MemoryStream(bytes));
        Assert.All(back, h => Assert.False(h.Header.Contains("CHECKSUM") || h.Header.Contains("DATASUM")));
    }

    [Fact]
    public void Unmodified_astropy_file_saved_with_auto_keeps_valid_checksums()
    {
        using var f = FitsFile.Open(Fixture("checksum.fits"));
        var bytes = SaveBytes(f, ChecksumPolicy.Auto);
        using var back = FitsFile.Read(new MemoryStream(bytes), new FitsReadOptions(true));
        Assert.All(back.VerifyChecksums(), r => Assert.Equal(new ChecksumResult(ChecksumStatus.Valid, ChecksumStatus.Valid), r));
        Assert.Equal(5898340L, long.Parse(back[0].Header.Get<string>("DATASUM")!, CultureInfo.InvariantCulture)); // same data sum as astropy
    }

    [Fact]
    public void Saving_does_not_corrupt_state_when_the_policy_fails_midway()
    {
        var bad = ImageHdu.Create<byte>([1, 2], 2);
        bad.Header.Set("NAXIS1", 9);
        var file = new FitsFile().Add(ImageHdu.Create<byte>([1], 1)).Add(bad);
        Assert.Throws<FitsException>(() => file.Save(new MemoryStream(), ChecksumPolicy.Always));
        Assert.False(file[0].Header.Contains("CHECKSUM"));
    }

    [Fact]
    public void Checksum_stays_valid_after_a_lazy_save_over_the_opened_file()
    {
        string path = Path.Combine(Path.GetTempPath(), "fitsnet-" + Guid.NewGuid().ToString("N") + ".fits");
        try
        {
            File.Copy(Fixture("checksum.fits"), path);
            using var f = FitsFile.Open(path);
            f[1].Header.Set("NOTE", "changed");
            f.Save(path, ChecksumPolicy.Auto);
            Assert.All(f.VerifyChecksums(), r => Assert.Equal(new ChecksumResult(ChecksumStatus.Valid, ChecksumStatus.Valid), r));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
