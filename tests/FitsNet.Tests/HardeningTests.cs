using System.IO.Compression;
using FitsNet.Compression;

namespace FitsNet.Tests;

/// <summary>Limits and failure behaviour: sizes announced by a file, streams of unknown length, saves that fail half way.</summary>
public class HardeningTests
{
    private static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(data);
        return ms.ToArray();
    }

    // ---- gzip output length ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_gzip_stream_must_decompress_to_exactly_the_announced_length()
    {
        byte[] data = Enumerable.Range(0, 1000).Select(i => (byte)(i % 7)).ToArray();
        byte[] packed = Gzip(data);
        Assert.Equal(data, GzipCodec.Decompress(packed, 1000));
        Assert.Throws<FitsException>(() => GzipCodec.Decompress(packed, 999));    // one byte too many in the stream
        Assert.Throws<FitsException>(() => GzipCodec.Decompress(packed, 1001));   // one byte too few
        Assert.Throws<FitsException>(() => GzipCodec.Decompress(packed, 0));
        Assert.Empty(GzipCodec.Decompress(Gzip([]), 0));
        Assert.Throws<FitsException>(() => GzipCodec.Decompress(Gzip([1]), 0));
    }

    [Fact]
    public void A_compressed_tile_with_an_extra_byte_is_refused()
    {
        var image = CompressedImageHdu.Create<short>(Enumerable.Range(0, 64).Select(i => (short)i).ToArray(), [8, 8],
            new CompressionOptions { Algorithm = CompressionAlgorithm.Gzip1, TileShape = [8, 8] });
        Assert.Equal(64, image.ReadPixels<short>().Length);
        // the same tile announced for a smaller image: the data is longer than the tile
        image.Header.Set("ZNAXIS1", 8L).Set("ZNAXIS2", 7L).Set("ZTILE1", 8L).Set("ZTILE2", 7L);
        Assert.Throws<FitsException>(() => image.ReadPixels<short>());
    }

    // ---- streams of unknown length ----------------------------------------------------------------------------------------

    private sealed class EndlessStream : Stream
    {
        public long Served;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)' ', offset, count);
            Served += count;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NonSeekable(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 1000));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void An_endless_stream_stops_at_the_input_budget_before_the_memory_is_taken()
    {
        var endless = new EndlessStream();
        var ex = Assert.Throws<FitsException>(() => FitsFile.Read(endless, new FitsReadOptions(MaxInputBytes: 1 << 20)));
        Assert.Contains("larger than", ex.Message);
        Assert.True(endless.Served <= (1 << 20) + 81920, $"{endless.Served} bytes were read");
    }

    [Fact]
    public void A_stream_that_cannot_seek_is_read_when_it_fits_the_budget()
    {
        var ms = new MemoryStream();
        new FitsFile().Add(ImageHdu.Create<short>(Enumerable.Range(0, 5000).Select(i => (short)i).ToArray(), 5000)).Save(ms);
        byte[] bytes = ms.ToArray();
        using var file = FitsFile.Read(new NonSeekable(bytes), new FitsReadOptions(MaxInputBytes: bytes.Length));
        Assert.Equal(5000, ((ImageHdu)file.Primary).ReadPixels<short>().Length);
        Assert.Throws<FitsException>(() => FitsFile.Read(new NonSeekable(bytes), new FitsReadOptions(MaxInputBytes: bytes.Length - 1)));
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(bytes), new FitsReadOptions(MaxInputBytes: bytes.Length - 1)));   // seekable too
    }

    // ---- sizes and ranges -------------------------------------------------------------------------------------------------

    [Fact]
    public void Alignment_and_tile_counts_report_overflow_as_fits_errors()
    {
        Assert.Equal(0, HduLayout.Padded(0));
        Assert.Equal(2880, HduLayout.Padded(2880));
        Assert.Equal(5760, HduLayout.Padded(2881));
        Assert.Throws<FitsException>(() => HduLayout.Padded(long.MaxValue));
        Assert.Throws<FitsException>(() => HduLayout.Padded(-1));
        Assert.Equal(long.MaxValue - long.MaxValue % 2880, HduLayout.Padded(long.MaxValue - long.MaxValue % 2880));
        Assert.Throws<FitsException>(() => TileGrid.Count([long.MaxValue, 2]));
        Assert.Throws<FitsException>(() => TileGrid.Count([4, -1]));
        Assert.Equal(12, TileGrid.Count([3, 4]));
        Assert.Equal(0, TileGrid.Count([3, 0]));
    }

    [Fact]
    public void Ranges_close_to_the_largest_offsets_are_refused_with_the_same_error()
    {
        var buffer = new byte[16];
        var sources = new DataSource[]
        {
            new MemoryDataSource(new byte[100]),
            new SegmentDataSource(new byte[200], 50, 100),
        };
        foreach (var source in sources)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadRange(long.MaxValue, buffer));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadRange(long.MaxValue - 8, buffer));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadRange(-1, buffer));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.ReadRange(85, buffer));
            source.ReadRange(84, buffer);
            source.ReadRange(100, []);
        }
        using var stream = new MemoryStream(new byte[100]);
        var streamed = new StreamDataSource(stream, 0, 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => streamed.ReadRange(long.MaxValue, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => streamed.ReadRange(long.MaxValue - 8, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => streamed.ReadRange(85, buffer));
        streamed.ReadRange(84, buffer);
    }

    // ---- quantization -----------------------------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_quantization_method_is_not_read_as_no_dither()
    {
        var floats = Enumerable.Range(0, 64).Select(i => (float)(100 + Math.Sin(i))).ToArray();
        var image = CompressedImageHdu.Create<float>(floats, [8, 8], new CompressionOptions { TileShape = [8, 8] });
        Assert.Equal(64, image.ReadPixels<float>().Length);
        image.Header.Set("ZQUANTIZ", "SOMETHING_NEW");
        Assert.Throws<FitsException>(() => image.ReadPixels<float>());
        image.Header.Set("ZQUANTIZ", "NO_DITHER");
        Assert.Equal(64, image.ReadPixels<float>().Length);
        image.Header.Remove("ZQUANTIZ");                                  // absent: the default is no dithering
        Assert.Equal(64, image.ReadPixels<float>().Length);
        image.Header.Set("ZQUANTIZ", "");                                 // empty: the same
        Assert.Equal(64, image.ReadPixels<float>().Length);

        // an integer image has no use for it: a stray value does not make the image unreadable
        var integers = CompressedImageHdu.Create<short>(Enumerable.Range(0, 64).Select(i => (short)i).ToArray(), [8, 8]);
        integers.Header.Set("ZQUANTIZ", "SOMETHING_NEW");
        Assert.Equal(64, integers.ReadPixels<short>().Length);
        Assert.Equal(BitPix.Int16, integers.BitPix);
    }

    // ---- a failed save leaves everything as it was ------------------------------------------------------------------------

    private sealed class Scratch : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "fitsnet-hard-" + Guid.NewGuid().ToString("N"));

        public Scratch() => System.IO.Directory.CreateDirectory(Directory);

        public string Path_(string name) => Path.Combine(Directory, name);

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static string[] HeaderText(Hdu hdu) => hdu.Header.Select(c => c.ToString()).ToArray();

    [Fact]
    public void When_the_file_cannot_be_put_in_place_the_model_is_exactly_as_before()
    {
        using var scratch = new Scratch();
        string target = scratch.Path_("target.fits");
        File.WriteAllText(target, "occupied");
        var image = ImageHdu.Create<short>([1, 2, 3, 4], 2, 2);
        var file = new FitsFile().Add(image);
        var before = HeaderText(image);

        // another process holds the target open: the replacement fails after the temporary file was written
        using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            if (OperatingSystem.IsWindows()) Assert.ThrowsAny<IOException>(() => file.Save(target, ChecksumPolicy.Always));
            else return;
        }
        Assert.Equal(before, HeaderText(image));
        Assert.False(image.Header.Contains("CHECKSUM"));
        Assert.DoesNotContain(Directory.GetFiles(scratch.Directory), p => p.Contains(".tmp-"));

        // and the next save, once the file is free, is a complete one
        file.Save(target, ChecksumPolicy.Always);
        using var back = FitsFile.Open(target);
        Assert.Equal(ChecksumStatus.Valid, back.Primary.VerifyChecksum().Checksum);
        Assert.Equal(new short[] { 1, 2, 3, 4 }, ((ImageHdu)back.Primary).ReadPixels<short>());
    }

    [Fact]
    public void A_failed_replacement_of_the_opened_file_keeps_the_instance_usable_and_unchanged()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scratch = new Scratch();
        string path = scratch.Path_("opened.fits");
        new FitsFile().Add(ImageHdu.Create<short>([1, 2, 3], 3)).Save(path);
        using var file = FitsFile.Open(path);
        var image = (ImageHdu)file.Primary;
        var before = HeaderText(image);
        image.Header.Set("EDITED", true);
        var edited = HeaderText(image);

        // a second reader without delete sharing makes the replacement fail once this instance has released the file
        using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.ThrowsAny<IOException>(() => file.Save(path, ChecksumPolicy.Always));
        }
        Assert.Equal(edited, HeaderText(image));                       // the edit is still there, nothing else was added
        Assert.False(image.Header.Contains("CHECKSUM"));
        Assert.Equal(new short[] { 1, 2, 3 }, image.ReadPixels<short>());    // the data is still read from the original file
        file.Save(path);
        Assert.Equal(new short[] { 1, 2, 3 }, image.ReadPixels<short>());
        Assert.True(HeaderText(image).Length > before.Length);
    }

    [Fact]
    public void Saving_to_a_stream_that_fails_leaves_the_headers_alone()
    {
        var image = ImageHdu.Create<short>([1, 2, 3, 4], 2, 2);
        var file = new FitsFile().Add(image);
        var before = HeaderText(image);
        Assert.ThrowsAny<Exception>(() => file.Save(new FailingStream(), ChecksumPolicy.Always));
        Assert.Equal(before, HeaderText(image));
        file.Save(new MemoryStream(), ChecksumPolicy.Always);
        Assert.True(image.Header.Contains("CHECKSUM"));
    }

    [Fact]
    public void A_failed_save_does_not_make_an_unedited_file_look_edited()
    {
        var ms = new MemoryStream();
        new FitsFile().Add(ImageHdu.Create<short>([1, 2, 3, 4], 2, 2)).Save(ms, ChecksumPolicy.Always);
        byte[] bytes = ms.ToArray();
        using var file = FitsFile.Read(new MemoryStream(bytes));
        Assert.ThrowsAny<Exception>(() => file.Save(new FailingStream()));
        var again = new MemoryStream();
        file.Save(again);
        Assert.Equal(bytes, again.ToArray());   // still written back byte for byte, checksums untouched
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("disk full");
    }

    // ---- headers ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_options_are_checked_and_the_messages_do_not_depend_on_the_culture()
    {
        var bytes = new MemoryStream();
        new FitsFile().Add(ImageHdu.Create<byte>([0], 1)).Save(bytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsFile.Read(new MemoryStream(bytes.ToArray()), new FitsReadOptions(MaxInputBytes: -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsFile.Read(new MemoryStream(bytes.ToArray()), new FitsReadOptions(MaxTotalHeaderBlocks: 0)));
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            var ex = Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(bytes.ToArray()), new FitsReadOptions(MaxInputBytes: 1000)));
            Assert.Contains("1,000 bytes (MaxInputBytes)", ex.Message);   // the user's own budget, not a suggestion to use Open
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = old;
        }
    }

    [Fact]
    public void A_file_with_more_headers_than_the_budget_is_refused_by_open_as_well()
    {
        // already covered by the next test for Open; here a huge number of tiny extensions
        var file = new FitsFile().Add(ImageHdu.Create<byte>([0], 1));
        for (int i = 0; i < 30; i++) file.Add(ImageHdu.Create<byte>([0], 1));
        var ms = new MemoryStream();
        file.Save(ms);
        Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(ms.ToArray()), new FitsReadOptions(MaxTotalHeaderBlocks: 10)));
        using (FitsFile.Read(new MemoryStream(ms.ToArray()), new FitsReadOptions(MaxTotalHeaderBlocks: 31))) { }
    }

    [Fact]
    public void An_empty_tile_and_a_tile_followed_by_other_data_are_handled()
    {
        Assert.Empty(GzipCodec.Decompress(Gzip([]), 0));
        byte[] one = Gzip([1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, GzipCodec.Decompress([.. one, .. new byte[] { 9, 9, 9 }], 3));   // bytes after the stream are not part of the tile
    }

    [Fact]
    public void The_headers_of_a_whole_file_share_one_budget()
    {
        var file = new FitsFile().Add(ImageHdu.Create<byte>([0], 1));
        for (int i = 0; i < 40; i++)
        {
            var ext = ImageHdu.Create<byte>([0], 1);
            for (int c = 0; c < 80; c++) ext.Header.Set("K" + c, c);   // three blocks of cards per extension
            file.Add(ext);
        }
        var ms = new MemoryStream();
        file.Save(ms);
        byte[] bytes = ms.ToArray();
        using (FitsFile.Read(new MemoryStream(bytes))) { }
        var ex = Assert.Throws<FitsException>(() => FitsFile.Read(new MemoryStream(bytes), new FitsReadOptions(MaxTotalHeaderBlocks: 100)));
        Assert.Contains("MaxTotalHeaderBlocks", ex.Message);
        using var path = new Scratch();
        string p = path.Path_("many.fits");
        File.WriteAllBytes(p, bytes);
        Assert.Throws<FitsException>(() => FitsFile.Open(p, new FitsReadOptions(MaxTotalHeaderBlocks: 100)));
    }
}
