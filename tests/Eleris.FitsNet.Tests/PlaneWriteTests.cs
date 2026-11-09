using System.Numerics;

namespace Eleris.FitsNet.Tests;

/// <summary><see cref="ImageHdu.CreateFromPlanes{T}"/> writes an image plane by plane, without a copy of the cube (issue #9).</summary>
public class PlaneWriteTests
{
    private static T[][] RandomPlanes<T>(int planes, int perPlane, int seed) where T : unmanaged
    {
        var random = new Random(seed);
        return Enumerable.Range(0, planes).Select(_ =>
        {
            var bytes = new byte[perPlane * System.Runtime.CompilerServices.Unsafe.SizeOf<T>()];
            random.NextBytes(bytes);
            var values = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, T>(bytes).ToArray();
            if (typeof(T) == typeof(float)) for (int i = 0; i < values.Length; i++) values[i] = (T)(object)(float)(random.NextDouble() * 1e6 - 5e5);
            if (typeof(T) == typeof(double)) for (int i = 0; i < values.Length; i++) values[i] = (T)(object)(random.NextDouble() * 1e9 - 5e8);
            return values;
        }).ToArray();
    }

    /// <summary>The file written from planes and the file written from the same pixels in one array, with headers alike.</summary>
    private static (byte[] FromPlanes, byte[] FromArray) BothWays<T>(T[][] planes, long[] axes, ChecksumPolicy checksums) where T : unmanaged
    {
        var fromPlanes = ImageHdu.CreateFromPlanes<T>(Planes.AsMemory(planes), axes);
        var fromArray = ImageHdu.Create<T>(Planes.Concat(planes), axes);
        foreach (var image in new[] { fromPlanes, fromArray }) image.Header.Set("OBJECT", "M42", "target");
        // as an extension too: the data and its padding follow another HDU
        var a = new FitsFile().Add(fromPlanes).Add(ImageHdu.CreateFromPlanes<T>(Planes.AsMemory(planes), axes));
        var b = new FitsFile().Add(fromArray).Add(ImageHdu.Create<T>(Planes.Concat(planes), axes));
        return (Planes.Save(a, checksums), Planes.Save(b, checksums));
    }

    [Fact]
    public void Every_pixel_type_is_written_byte_for_byte_like_Create()
    {
        long[] axes = [37, 23, 3];   // planes that are not a multiple of anything, and data that does not fill the last block
        void Check<T>(int seed) where T : unmanaged
        {
            var planes = RandomPlanes<T>(3, 37 * 23, seed);
            foreach (var checksums in new[] { ChecksumPolicy.Auto, ChecksumPolicy.Always })
            {
                var (fromPlanes, fromArray) = BothWays(planes, axes, checksums);
                Assert.Equal(fromArray, fromPlanes);
            }
        }

        Check<byte>(1);
        Check<sbyte>(2);
        Check<short>(3);
        Check<ushort>(4);
        Check<int>(5);
        Check<uint>(6);
        Check<long>(7);
        Check<ulong>(8);
        Check<float>(9);
        Check<double>(10);
    }

    [Fact]
    public void Large_planes_spanning_several_blocks_are_written_byte_for_byte_like_Create()
    {
        // 3 planes of 700 x 500 doubles (2.8 MB each): many blocks per plane, and a block boundary inside a plane
        var planes = RandomPlanes<double>(3, 700 * 500, 42);
        var (fromPlanes, fromArray) = BothWays(planes, [700, 500, 3], ChecksumPolicy.Always);
        Assert.Equal(fromArray, fromPlanes);
    }

    [Fact]
    public void The_header_is_the_one_Create_writes()
    {
        var planes = RandomPlanes<ushort>(2, 6, 3);
        var a = ImageHdu.CreateFromPlanes<ushort>(Planes.AsMemory(planes), 3, 2, 2);
        var b = ImageHdu.Create<ushort>(Planes.Concat(planes), 3, 2, 2);
        Assert.Equal(b.Header.Select(c => c.ToString()), a.Header.Select(c => c.ToString()));
        Assert.Equal(BitPix.Int16, a.BitPix);
        Assert.Equal([3L, 2, 2], a.Axes);
        Assert.Equal(32768L, a.Header.Get<long>("BZERO"));
    }

    [Fact]
    public void A_2D_image_can_be_given_row_by_row()
    {
        var rows = Enumerable.Range(0, 4).Select(r => Enumerable.Range(0, 5).Select(c => r * 10 + c).ToArray()).ToArray();
        var image = ImageHdu.CreateFromPlanes<int>(Planes.AsMemory(rows), 5, 4);
        Assert.Equal(Planes.Concat(rows), image.ReadPixels<int>());
        Assert.Equal(Planes.Save(new FitsFile().Add(ImageHdu.Create<int>(Planes.Concat(rows), 5, 4))), Planes.Save(new FitsFile().Add(image)));
    }

    [Fact]
    public void Pixels_regions_and_checksums_read_from_the_planes_match_those_of_Create()
    {
        var planes = RandomPlanes<uint>(3, 40 * 30, 11);
        var a = ImageHdu.CreateFromPlanes<uint>(Planes.AsMemory(planes), 40, 30, 3);
        var b = ImageHdu.Create<uint>(Planes.Concat(planes), 40, 30, 3);

        Assert.Equal(b.ReadPixels<uint>(), a.ReadPixels<uint>());
        Assert.Equal(b.ReadRawPixels<int>(), a.ReadRawPixels<int>());
        Assert.Equal(b.ReadPixels<double>(), a.ReadPixels<double>());
        Assert.Equal(b.ReadRegion<uint>([3, 5, 1], [17, 11, 2]), a.ReadRegion<uint>([3, 5, 1], [17, 11, 2]));
        Assert.Equal(b.ComputeDataSum(), a.ComputeDataSum());
        Assert.Equal(b.Source.ReadAll(), a.Source.ReadAll());
        Assert.Equal(b.Compress().ReadPixels<uint>(), a.Compress().ReadPixels<uint>());
    }

    [Fact]
    public void Byte_ranges_that_start_or_end_inside_a_value_are_read_correctly()
    {
        static void Check<T>(int seed) where T : unmanaged
        {
            var planes = RandomPlanes<T>(3, 7, seed);
            var source = ImageHdu.CreateFromPlanes<T>(Planes.AsMemory(planes), 7, 3).Source;
            byte[] expected = ImageHdu.Create<T>(Planes.Concat(planes), 7, 3).Source.ReadAll();
            for (int offset = 0; offset < expected.Length; offset += 3)
                for (int length = 0; offset + length <= expected.Length; length += 5)
                {
                    var range = new byte[length];
                    source.ReadRange(offset, range);
                    Assert.Equal(expected.AsSpan(offset, length).ToArray(), range);
                }
        }

        Check<long>(5);
        Check<ulong>(6);    // the top bit is flipped inside the partial values too
        Check<uint>(7);
        Check<ushort>(8);
        Check<short>(9);
        Check<sbyte>(10);
        Check<double>(11);
    }

    [Fact]
    public void Planes_of_one_and_two_byte_values_larger_than_a_block_are_written_like_Create()
    {
        // 300 000 values: more than one block of 256 KB, and blocks that end on no 4-byte boundary of the checksum
        var (bytes, fromBytes) = BothWays(RandomPlanes<sbyte>(3, 300_001, 1), [300_001, 3], ChecksumPolicy.Always);
        Assert.Equal(fromBytes, bytes);
        var (shorts, fromShorts) = BothWays(RandomPlanes<ushort>(2, 200_001, 2), [200_001, 2], ChecksumPolicy.Always);
        Assert.Equal(fromShorts, shorts);
    }

    [Fact]
    public void The_planes_are_not_copied()
    {
        float[] first = [1, 2, 3, 4], second = [5, 6, 7, 8];
        var image = ImageHdu.CreateFromPlanes<float>([first, second], 2, 2, 2);
        Assert.Equal(32, image.DataLength);
        first[0] = 100;   // documented: the planes are read again each time the data is needed
        Assert.Equal([100f, 2, 3, 4, 5, 6, 7, 8], image.ReadPixels<float>());
    }

    [Fact]
    public void A_file_written_from_planes_reads_back_through_Open()
    {
        using var dir = new ScratchDirectory();
        var planes = Planes.Float(123, 45, 3);
        var file = new FitsFile().Add(ImageHdu.CreateFromPlanes<float>(Planes.AsMemory(planes), 123, 45, 3));
        file.Save(dir.File("cube.fits"), ChecksumPolicy.Always, FitsSaveMode.CreateNew);

        using var reread = FitsFile.Open(dir.File("cube.fits"));
        var image = (ImageHdu)reread.Primary;
        Assert.Equal([123L, 45, 3], image.Axes);
        Assert.Equal(Planes.Concat(planes), image.ReadPixels<float>());
        Assert.Equal(planes[1], image.ReadRegion<float>([0, 0, 1], [123, 45, 1]));
        var sums = image.VerifyChecksum();
        Assert.Equal(ChecksumStatus.Valid, sums.Checksum);
        Assert.Equal(ChecksumStatus.Valid, sums.DataSum);
    }

    [Fact]
    public void Planes_and_axes_that_do_not_agree_are_refused()
    {
        ReadOnlyMemory<float>[] two = [new float[6], new float[6]];
        Assert.Throws<ArgumentNullException>(() => ImageHdu.CreateFromPlanes<float>(null!, 3, 2, 2));
        Assert.Throws<ArgumentNullException>(() => ImageHdu.CreateFromPlanes<float>(two, null!));
        Assert.Throws<ArgumentException>(() => ImageHdu.CreateFromPlanes<float>(two));                  // no axis
        Assert.Throws<ArgumentException>(() => ImageHdu.CreateFromPlanes<float>(two, 3, 2, 3));         // 2 planes for NAXIS3 = 3
        Assert.Throws<ArgumentException>(() => ImageHdu.CreateFromPlanes<float>(two, 4, 2, 2));         // planes of 6 for 8 pixels
        Assert.Throws<ArgumentException>(() => ImageHdu.CreateFromPlanes<float>([new float[6], new float[5]], 3, 2, 2));
        Assert.Throws<ArgumentException>(() => ImageHdu.CreateFromPlanes<float>(two, -3, -2, 2));
        Assert.Throws<ArgumentException>(() => ImageHdu.CreateFromPlanes<float>(two, 1L << 40, 1L << 40, 2));
        Assert.Throws<ArgumentException>(() => ImageHdu.CreateFromPlanes<float>([], 1L << 40, 0));      // no plane, but planes too large
        Assert.Throws<ArgumentException>(() => ImageHdu.CreateFromPlanes<float>(two, [.. Enumerable.Repeat(1L, 999), 2]));
        Assert.Throws<NotSupportedException>(() => ImageHdu.CreateFromPlanes<char>([new char[6], new char[6]], 3, 2, 2));
        Assert.Throws<NotSupportedException>(() => ImageHdu.CreateFromPlanes<Vector2>([new Vector2[6], new Vector2[6]], 3, 2, 2));
    }

    [Fact]
    public void Empty_planes_make_an_image_without_data()
    {
        var image = ImageHdu.CreateFromPlanes<short>([ReadOnlyMemory<short>.Empty, ReadOnlyMemory<short>.Empty], 0, 2);
        Assert.Equal(0, image.DataLength);
        Assert.Equal(Planes.Save(new FitsFile().Add(ImageHdu.Create<short>([], 0, 2))), Planes.Save(new FitsFile().Add(image)));
    }

    [Fact]
    public void The_list_of_planes_is_copied()
    {
        var list = new List<ReadOnlyMemory<byte>> { new byte[] { 1, 2 }, new byte[] { 3, 4 } };
        var image = ImageHdu.CreateFromPlanes<byte>(list, 2, 2);
        list[0] = new byte[] { 9, 9 };
        Assert.Equal([1, 2, 3, 4], image.ReadPixels<byte>());
    }
}
