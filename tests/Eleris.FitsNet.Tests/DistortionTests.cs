using System.Text.Json;
using Eleris.FitsNet.Wcs;

namespace Eleris.FitsNet.Tests;

/// <summary>The SIP and TPV distortion conventions against astropy (wcslib), which applies both.</summary>
public class DistortionTests
{
    private sealed record Point(double[] Pix, double[] World, double[]? Back);

    private sealed record Case(string Name, FitsHeader Header, Point[] Points);

    private static readonly Lazy<Dictionary<string, Case>> Cases = new(Load);

    private static Dictionary<string, Case> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "distortion_vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, Case>();
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var header = new FitsHeader();
            foreach (var card in c.GetProperty("cards").EnumerateArray())
            {
                var v = card[1];
                header.Set(card[0].GetString()!, v.ValueKind == JsonValueKind.String ? v.GetString()! : v.TryGetInt64(out long l) ? l : v.GetDouble());
            }
            var points = c.GetProperty("points").EnumerateArray().Select(p => new Point(
                p.GetProperty("pix").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                p.GetProperty("world").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                p.GetProperty("back").ValueKind == JsonValueKind.Array ? p.GetProperty("back").EnumerateArray().Select(x => x.GetDouble()).ToArray() : null)).ToArray();
            string name = c.GetProperty("name").GetString()!;
            result[name] = new Case(name, header, points);
        }
        return result;
    }

    public static IEnumerable<object[]> CaseNames() => Cases.Value.Keys.Select(k => new object[] { k });

    private static double Separation(double lon1, double lat1, double lon2, double lat2)
    {
        double d2r = Math.PI / 180, dlon = (lon1 - lon2) * d2r;
        double s1 = Math.Sin(lat1 * d2r), c1 = Math.Cos(lat1 * d2r), s2 = Math.Sin(lat2 * d2r), c2 = Math.Cos(lat2 * d2r);
        double y = Math.Sqrt(Math.Pow(c2 * Math.Sin(dlon), 2) + Math.Pow(c1 * s2 - s1 * c2 * Math.Cos(dlon), 2));
        return Math.Atan2(y, s1 * s2 + c1 * c2 * Math.Cos(dlon)) / d2r;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Pixel_to_world_matches_astropy(string name)
    {
        var c = Cases.Value[name];
        var wcs = WorldCoordinateSystem.FromHeader(c.Header);
        double worst = 0;
        foreach (var p in c.Points)
        {
            double[] world = wcs.PixelToWorld(p.Pix);
            worst = Math.Max(worst, Separation(world[0], world[1], p.World[0], p.World[1]));
        }
        // a micro-arcsecond is 2.8e-10 degree: the two implementations do the same arithmetic
        Assert.True(worst < 1e-9, $"{name}: worst difference {worst:E2} degree");
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void World_to_pixel_undoes_the_distortion_like_astropy(string name)
    {
        var c = Cases.Value[name];
        var wcs = WorldCoordinateSystem.FromHeader(c.Header);
        double worst = 0;
        foreach (var p in c.Points.Where(p => p.Back is not null))
        {
            double[] pixel = wcs.WorldToPixel(p.World);
            worst = Math.Max(worst, Math.Max(Math.Abs(pixel[0] - p.Back![0]), Math.Abs(pixel[1] - p.Back![1])));
        }
        Assert.True(worst < 1e-5, $"{name}: worst difference {worst:E2} pixel");
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_convention_is_reported_and_nothing_is_left_unapplied(string name)
    {
        var wcs = WorldCoordinateSystem.FromHeader(Cases.Value[name].Header);
        Assert.Equal(name.StartsWith("SIP", StringComparison.Ordinal) ? "SIP" : "TPV", wcs.Distortion);
        Assert.Equal("", wcs.UnappliedDistortion);
    }

    [Fact]
    public void Without_a_distortion_the_plain_projection_is_used()
    {
        var header = new FitsHeader()
            .Set("CRPIX1", 10.0).Set("CRPIX2", 10.0).Set("CRVAL1", 10.0).Set("CRVAL2", 20.0)
            .Set("CTYPE1", "RA---TAN").Set("CTYPE2", "DEC--TAN").Set("CDELT1", -0.001).Set("CDELT2", 0.001);
        var wcs = WorldCoordinateSystem.FromHeader(header);
        Assert.Equal("", wcs.Distortion);
        Assert.Equal("", wcs.UnappliedDistortion);
    }

    [Fact]
    public void A_SIP_suffix_without_coefficients_is_reported_as_unapplied()
    {
        var header = new FitsHeader()
            .Set("CRPIX1", 10.0).Set("CRPIX2", 10.0).Set("CRVAL1", 10.0).Set("CRVAL2", 20.0)
            .Set("CTYPE1", "RA---TAN-SIP").Set("CTYPE2", "DEC--TAN-SIP").Set("CDELT1", -0.001).Set("CDELT2", 0.001);
        var wcs = WorldCoordinateSystem.FromHeader(header);
        Assert.Equal("", wcs.Distortion);
        Assert.Equal("-SIP", wcs.UnappliedDistortion);
    }

    [Fact]
    public void Other_conventions_stay_unapplied()
    {
        var header = new FitsHeader()
            .Set("CRPIX1", 10.0).Set("CRPIX2", 10.0).Set("CRVAL1", 10.0).Set("CRVAL2", 20.0)
            .Set("CTYPE1", "RA---TAN-DSS").Set("CTYPE2", "DEC--TAN-DSS").Set("CDELT1", -0.001).Set("CDELT2", 0.001);
        var wcs = WorldCoordinateSystem.FromHeader(header);
        Assert.Equal("", wcs.Distortion);
        Assert.Equal("-DSS", wcs.UnappliedDistortion);
    }

    [Fact]
    public void A_SIP_description_that_is_not_the_primary_one_is_not_applied()
    {
        var header = new FitsHeader()
            .Set("CRPIX1A", 10.0).Set("CRPIX2A", 10.0).Set("CRVAL1A", 10.0).Set("CRVAL2A", 20.0)
            .Set("CTYPE1A", "RA---TAN-SIP").Set("CTYPE2A", "DEC--TAN-SIP").Set("CDELT1A", -0.001).Set("CDELT2A", 0.001)
            .Set("A_ORDER", 2).Set("B_ORDER", 2).Set("A_2_0", 1e-3);
        var wcs = WorldCoordinateSystem.FromHeader(header, 'A');
        Assert.Equal("", wcs.Distortion);
        Assert.Equal("-SIP", wcs.UnappliedDistortion);
    }

    private static FitsHeader Sip() => new FitsHeader()
        .Set("CRPIX1", 100.0).Set("CRPIX2", 100.0).Set("CRVAL1", 10.0).Set("CRVAL2", 20.0)
        .Set("CTYPE1", "RA---TAN-SIP").Set("CTYPE2", "DEC--TAN-SIP").Set("CDELT1", -0.001).Set("CDELT2", 0.001);

    [Fact]
    public void Inconsistent_or_excessive_SIP_keywords_are_refused()
    {
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Sip().Set("A_ORDER", 2)));
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Sip().Set("A_ORDER", 2).Set("B_ORDER", 2).Set("AP_ORDER", 2)));
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Sip().Set("A_ORDER", 99).Set("B_ORDER", 2)));
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Sip().Set("A_ORDER", -1).Set("B_ORDER", 2)));
    }

    [Fact]
    public void A_distortion_that_cannot_be_undone_gives_NaN()
    {
        // u - 0.5 u^2 never goes above 0.5: a position one pixel away from the reference along the first axis has no pixel
        var wcs = WorldCoordinateSystem.FromHeader(Sip().Set("A_ORDER", 2).Set("B_ORDER", 2).Set("A_2_0", -0.5).Set("B_0_2", -0.5));
        var plain = WorldCoordinateSystem.FromHeader(Sip().Set("CTYPE1", "RA---TAN").Set("CTYPE2", "DEC--TAN"));
        double[] unreachable = plain.PixelToWorld([101.0, 100.0]);
        double[] pixel = wcs.WorldToPixel(unreachable);
        Assert.True(double.IsNaN(pixel[0]) && double.IsNaN(pixel[1]));
        // while a reachable one still comes back
        Assert.Equal(100.2, wcs.WorldToPixel(wcs.PixelToWorld([100.2, 100.0]))[0], 1e-9);
    }

    [Fact]
    public void TPV_with_too_many_terms_is_refused_and_the_other_projections_keep_their_parameters()
    {
        var tpv = new FitsHeader()
            .Set("CRPIX1", 100.0).Set("CRPIX2", 100.0).Set("CRVAL1", 10.0).Set("CRVAL2", 20.0)
            .Set("CTYPE1", "RA---TPV").Set("CTYPE2", "DEC--TPV").Set("CD1_1", -0.001).Set("CD2_2", 0.001)
            .Set("PV1_1", 1.0).Set("PV2_1", 1.0);
        Assert.Equal("TPV", WorldCoordinateSystem.FromHeader(tpv).ProjectionCode);
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(tpv.Set("PV1_40", 1.0)));

        // for a conic projection PVi_m are the parameters of the projection, as before
        var conic = new FitsHeader()
            .Set("CRPIX1", 100.0).Set("CRPIX2", 100.0).Set("CRVAL1", 10.0).Set("CRVAL2", 20.0)
            .Set("CTYPE1", "RA---COP").Set("CTYPE2", "DEC--COP").Set("CDELT1", -0.01).Set("CDELT2", 0.01)
            .Set("PV2_1", 45.0);
        Assert.Equal("", WorldCoordinateSystem.FromHeader(conic).Distortion);
    }

    [Fact]
    public void Pixels_and_positions_make_a_round_trip_through_both_conventions()
    {
        var random = new Random(3);
        foreach (string name in Cases.Value.Keys)
        {
            var wcs = WorldCoordinateSystem.FromHeader(Cases.Value[name].Header);
            for (int i = 0; i < 200; i++)
            {
                double[] pixel = [random.NextDouble() * 2000, random.NextDouble() * 1500];
                double[] back = wcs.WorldToPixel(wcs.PixelToWorld(pixel));
                Assert.True(Math.Abs(back[0] - pixel[0]) < 1e-5 && Math.Abs(back[1] - pixel[1]) < 1e-5, $"{name}: {pixel[0]},{pixel[1]} came back as {back[0]},{back[1]}");
            }
        }
    }

    [Fact]
    public void Far_pixels_of_a_large_mosaic_come_back()
    {
        // offsets of thousands of pixels: the search must stop on a relative tolerance, not an absolute one
        var wcs = WorldCoordinateSystem.FromHeader(Sip().Set("A_ORDER", 3).Set("B_ORDER", 3).Set("A_2_0", 1e-6).Set("B_1_1", -5e-7).Set("A_0_3", 1e-10));
        var random = new Random(8);
        for (int i = 0; i < 5000; i++)
        {
            double[] pixel = [random.NextDouble() * 18000 - 4000, random.NextDouble() * 12000 - 3000];
            double[] back = wcs.WorldToPixel(wcs.PixelToWorld(pixel));
            Assert.False(double.IsNaN(back[0]), $"{pixel[0]},{pixel[1]} came back as NaN");
            Assert.True(Math.Abs(back[0] - pixel[0]) < 1e-5 && Math.Abs(back[1] - pixel[1]) < 1e-5, $"{pixel[0]},{pixel[1]} came back as {back[0]},{back[1]}");
        }
    }

    [Fact]
    public void SIP_gives_the_same_result_with_PC_and_CDELT_as_with_the_equivalent_CD()
    {
        FitsHeader Common() => new FitsHeader()
            .Set("CRPIX1", 100.0).Set("CRPIX2", 100.0).Set("CRVAL1", 10.0).Set("CRVAL2", 20.0)
            .Set("CTYPE1", "RA---TAN-SIP").Set("CTYPE2", "DEC--TAN-SIP")
            .Set("A_ORDER", 2).Set("B_ORDER", 2).Set("A_2_0", 2e-6).Set("B_0_2", -1e-6);
        var withPc = WorldCoordinateSystem.FromHeader(Common().Set("CDELT1", -0.002).Set("CDELT2", 0.001).Set("PC1_1", 0.9).Set("PC1_2", 0.1).Set("PC2_1", -0.1).Set("PC2_2", 0.9));
        var withCd = WorldCoordinateSystem.FromHeader(Common().Set("CD1_1", -0.0018).Set("CD1_2", -0.0002).Set("CD2_1", -0.0001).Set("CD2_2", 0.0009));
        foreach (double[] pixel in new[] { new[] { 1.0, 1.0 }, [250.5, 40.25], [180.0, 190.0] })
        {
            double[] a = withPc.PixelToWorld(pixel), b = withCd.PixelToWorld(pixel);
            Assert.True(Separation(a[0], a[1], b[0], b[1]) < 1e-12);
        }
    }

    [Fact]
    public void SIP_is_applied_with_a_third_axis_and_reported_when_the_celestial_axes_are_not_the_first_two()
    {
        var cube = Sip().Set("WCSAXES", 3).Set("CTYPE3", "FREQ").Set("CRPIX3", 1.0).Set("CRVAL3", 1e9).Set("CDELT3", 1e6)
            .Set("A_ORDER", 2).Set("B_ORDER", 2).Set("A_2_0", 2e-6);
        var wcs = WorldCoordinateSystem.FromHeader(cube);
        Assert.Equal("SIP", wcs.Distortion);
        double[] world = wcs.PixelToWorld([150.0, 120.0, 4.0]);
        double[] back = wcs.WorldToPixel(world);
        Assert.Equal(150.0, back[0], 6);
        Assert.Equal(4.0, back[2], 6);

        var shifted = new FitsHeader()
            .Set("WCSAXES", 3).Set("CTYPE1", "FREQ").Set("CRPIX1", 1.0).Set("CRVAL1", 1e9).Set("CDELT1", 1e6)
            .Set("CTYPE2", "RA---TAN-SIP").Set("CTYPE3", "DEC--TAN-SIP").Set("CRPIX2", 100.0).Set("CRPIX3", 100.0)
            .Set("CRVAL2", 10.0).Set("CRVAL3", 20.0).Set("CDELT2", -0.001).Set("CDELT3", 0.001)
            .Set("A_ORDER", 2).Set("B_ORDER", 2).Set("A_2_0", 2e-6);
        var unapplied = WorldCoordinateSystem.FromHeader(shifted);
        Assert.Equal("", unapplied.Distortion);
        Assert.Equal("-SIP", unapplied.UnappliedDistortion);
    }

    [Fact]
    public void TPV_with_a_SIP_suffix_applies_only_the_TPV_polynomials()
    {
        var header = new FitsHeader()
            .Set("CRPIX1", 100.0).Set("CRPIX2", 100.0).Set("CRVAL1", 10.0).Set("CRVAL2", 20.0)
            .Set("CTYPE1", "RA---TPV-SIP").Set("CTYPE2", "DEC--TPV-SIP").Set("CD1_1", -0.001).Set("CD2_2", 0.001)
            .Set("PV1_1", 1.0).Set("PV2_1", 1.0).Set("A_ORDER", 2).Set("B_ORDER", 2).Set("A_2_0", 1e-3);
        var wcs = WorldCoordinateSystem.FromHeader(header);
        Assert.Equal("TPV", wcs.Distortion);
        Assert.Equal("-SIP", wcs.UnappliedDistortion);
    }

    [Fact]
    public void The_parameters_of_the_other_projections_beyond_the_fourth_longitude_axis_value_are_still_refused()
    {
        var header = new FitsHeader()
            .Set("CRPIX1", 100.0).Set("CRPIX2", 100.0).Set("CRVAL1", 10.0).Set("CRVAL2", 20.0)
            .Set("CTYPE1", "RA---COP").Set("CTYPE2", "DEC--COP").Set("CDELT1", -0.01).Set("CDELT2", 0.01).Set("PV2_1", 45.0)
            .Set("PV1_5", 1.0);
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(header));
    }
}
