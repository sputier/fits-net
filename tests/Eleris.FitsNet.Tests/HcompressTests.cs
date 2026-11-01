using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

public class HcompressBasicTests
{
    [Fact]
    public void Ramp4_encoding_matches_astropy_bytes()
    {
        int[] ramp = Enumerable.Range(0, 16).ToArray();
        string mine = Convert.ToHexString(HcompressCodec.Encode(ramp, 4, 4, 0)).ToLowerInvariant();
        const string astropy = "dd990000000400000004000000000000000000000040060400f3ff7affbfeffbfeffbf3fefcffbfeff800000";
        Assert.Equal(astropy, mine);
    }

    [Fact]
    public void Ramp4_astropy_stream_decodes_to_the_ramp()
    {
        byte[] stream = Convert.FromHexString("dd990000000400000004000000000000000000000040060400f3ff7affbfeffbfeffbf3fefcffbfeff800000");
        Assert.Equal(Enumerable.Range(0, 16), HcompressCodec.Decode(stream, 4, 4, false));
    }

    [Fact]
    public void Roundtrip_random_squares()
    {
        var rng = new Random(2);
        foreach (var (w, h) in new[] { (4, 4), (8, 8), (16, 16), (5, 7), (37, 23), (2, 2), (3, 3), (16, 5) })
        {
            int[] data = Enumerable.Range(0, w * h).Select(_ => rng.Next(-1000, 1000)).ToArray();
            Assert.Equal(data, HcompressCodec.Decode(HcompressCodec.Encode(data, w, h, 0), w, h, false));
        }
    }
}

public class HcompressCoefficientTests
{
    [Fact]
    public void Entropy_decoded_coefficients_equal_the_forward_transform_of_the_pixels()
    {
        int[] pixels = [188, 125, 136, 179, 115, 155, 166, 45, 11, 60, 57, 174, 182, 1, 99, 164];
        byte[] stream = Convert.FromHexString("dd9900000004000000040000000000000000000003a8080808f8f5e7f7ebf7ff7fdf1ebc7f3eff7f3ff7fdfafcf3f7effeffbf5f5f9e3fefcfaf6075a6");
        var (coeffs, _, _, _) = HcompressCodec.DecodeCoefficients(stream, 4, 4);
        var forward = pixels.Select(v => (long)v).ToArray();
        HcompressCodec.Htrans(forward, 4, 4);
        Assert.Equal(string.Join(",", forward), string.Join(",", coeffs));
    }
}

public class HcompressVectorTests
{
    public static IEnumerable<object[]> Vectors()
    {
        foreach (string line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "hcompress_vectors.txt")))
        {
            var p = line.Split(' ');
            yield return [int.Parse(p[0]), int.Parse(p[1]), p[2], p[3], p[4]];
        }
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Decodes_astropy_streams(int w, int h, string kind, string hex, string values)
    {
        _ = kind;
        int[] expected = values.Split(',').Select(int.Parse).ToArray();
        Assert.Equal(expected, HcompressCodec.Decode(Convert.FromHexString(hex), w, h, false));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Encodes_byte_for_byte_like_cfitsio(int w, int h, string kind, string hex, string values)
    {
        _ = kind;
        int[] pixels = values.Split(',').Select(int.Parse).ToArray();
        Assert.Equal(hex, Convert.ToHexString(HcompressCodec.Encode(pixels, w, h, 0)).ToLowerInvariant());
    }
}

public class HcompressLossyTests
{
    public static IEnumerable<object[]> Vectors()
    {
        foreach (string line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "hcompress_lossy_vectors.txt")))
        {
            var p = line.Split(' ');
            yield return [int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), p[3], p[4], p[5]];
        }
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Lossy_streams_decode_like_cfitsio(int w, int h, int scale, string hex, string original, string decoded)
    {
        _ = (scale, original);
        int[] expected = decoded.Split(',').Select(int.Parse).ToArray();
        Assert.Equal(expected, HcompressCodec.Decode(Convert.FromHexString(hex), w, h, false));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Lossy_encoding_is_byte_identical_to_cfitsio(int w, int h, int scale, string hex, string original, string decoded)
    {
        _ = decoded;
        int[] pixels = original.Split(',').Select(int.Parse).ToArray();
        Assert.Equal(hex, Convert.ToHexString(HcompressCodec.Encode(pixels, w, h, scale)).ToLowerInvariant());
    }
}
