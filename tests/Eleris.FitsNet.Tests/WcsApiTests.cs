using Eleris.FitsNet.Wcs;

namespace Eleris.FitsNet.Tests;

/// <summary>The public surface of the WCS: construction, errors, alternates, hostile headers.</summary>
public class WcsApiTests
{
    private static FitsHeader Tan()
    {
        var h = new FitsHeader();
        h.Set("CTYPE1", "RA---TAN").Set("CTYPE2", "DEC--TAN").Set("CRPIX1", 100.5).Set("CRPIX2", 100.5)
            .Set("CRVAL1", 83.8).Set("CRVAL2", -5.4).Set("CDELT1", -0.001).Set("CDELT2", 0.001);
        return h;
    }

    [Fact]
    public void The_reference_pixel_maps_to_the_reference_value_and_back()
    {
        var wcs = WorldCoordinateSystem.FromHeader(Tan());
        Assert.True(wcs.IsCelestial);
        Assert.Equal("TAN", wcs.ProjectionCode);
        Assert.Equal(0, wcs.LongitudeAxis);
        Assert.Equal(1, wcs.LatitudeAxis);
        double[] world = wcs.PixelToWorld([100.5, 100.5]);
        Assert.Equal(83.8, world[0], 12);
        Assert.Equal(-5.4, world[1], 12);
        double[] pixel = wcs.WorldToPixel([83.8, -5.4]);
        Assert.Equal(100.5, pixel[0], 9);
        Assert.Equal(100.5, pixel[1], 9);
    }

    [Fact]
    public void An_image_without_wcs_keywords_has_identity_coordinates()
    {
        var image = ImageHdu.Create<short>(new short[12], 4, 3);
        var wcs = image.GetWorldCoordinateSystem();
        Assert.False(wcs.IsCelestial);
        Assert.Equal(2, wcs.AxisCount);
        Assert.Equal([2.0, 3.0], wcs.PixelToWorld([2.0, 3.0]));
        Assert.Equal([2.0, 3.0], wcs.WorldToPixel([2.0, 3.0]));
    }

    [Fact]
    public void Alternate_descriptions_are_listed_and_independent()
    {
        var h = Tan();
        h.Set("WCSNAMEA", "other").Set("CTYPE1A", "GLON-CAR").Set("CTYPE2A", "GLAT-CAR").Set("CRVAL1A", 10.0).Set("CRVAL2A", 20.0)
            .Set("CRPIX1A", 1.0).Set("CRPIX2A", 1.0).Set("CDELT1A", 1.0).Set("CDELT2A", 1.0).Set("CRVAL1B", 5.0);
        Assert.Equal(['A', 'B'], WorldCoordinateSystem.AlternatesIn(h));
        var a = WorldCoordinateSystem.FromHeader(h, 'A');
        Assert.Equal("other", a.Name);
        Assert.Equal("CAR", a.ProjectionCode);
        var at = a.PixelToWorld([1.0, 1.0]);
        Assert.Equal(10.0, at[0], 9);
        Assert.Equal(20.0, at[1], 9);
        Assert.Equal("TAN", WorldCoordinateSystem.FromHeader(h).ProjectionCode);
        Assert.Throws<ArgumentException>(() => WorldCoordinateSystem.FromHeader(h, 'a'));
        Assert.Throws<ArgumentException>(() => WorldCoordinateSystem.FromHeader(h, '1'));
    }

    [Fact]
    public void The_reference_frame_and_equinox_are_exposed()
    {
        var h = Tan();
        h.Set("RADESYS", "FK5").Set("EQUINOX", 2000.0);
        var wcs = WorldCoordinateSystem.FromHeader(h);
        Assert.Equal("FK5", wcs.RadeSys);
        Assert.Equal(2000.0, wcs.Equinox);
        h = Tan();
        h.Set("RADECSYS", "FK4").Set("EPOCH", 1950.0); // the old names
        wcs = WorldCoordinateSystem.FromHeader(h);
        Assert.Equal("FK4", wcs.RadeSys);
        Assert.Equal(1950.0, wcs.Equinox);
    }

    [Fact]
    public void Points_outside_the_projection_have_no_image_and_are_nan()
    {
        var h = Tan();
        h.Set("CRVAL2", 90.0);
        var wcs = WorldCoordinateSystem.FromHeader(h);
        Assert.All(wcs.WorldToPixel([0.0, -30.0]), v => Assert.True(double.IsNaN(v)));
        var sin = WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "RA---SIN").Set("CTYPE2", "DEC--SIN"));
        Assert.All(sin.PixelToWorld([1e6, 1e6]), v => Assert.True(double.IsNaN(v)));
    }

    [Fact]
    public void Inconsistent_or_unsupported_descriptions_are_reported()
    {
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE2", "LINEAR")));            // no latitude axis
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE2", "DEC--SIN")));          // two projections
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "RA---").Set("CTYPE2", "DEC--"))); // no code
        Assert.Throws<NotSupportedException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "RA---XYZ").Set("CTYPE2", "DEC--XYZ")));
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CDELT1", 0.0)));                  // singular
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CUNIT1", "furlong").Set("CUNIT2", "furlong")));
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CUNIT1", "arcsec")));              // axes disagree
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "RA---ZPN").Set("CTYPE2", "DEC--ZPN"))); // no PV
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "RA---COP").Set("CTYPE2", "DEC--COP"))); // PV2_1 needed
    }

    [Fact]
    public void Distortion_suffixes_are_not_confused_with_the_projection()
    {
        var wcs = WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "RA---TAN-SIP").Set("CTYPE2", "DEC--TAN-SIP"));
        Assert.Equal("TAN", wcs.ProjectionCode);
    }

    [Fact]
    public void Arguments_are_checked()
    {
        var wcs = WorldCoordinateSystem.FromHeader(Tan());
        Assert.Throws<ArgumentException>(() => wcs.PixelToWorld([1.0]));
        Assert.Throws<ArgumentException>(() => wcs.WorldToPixel([1.0, 2.0, 3.0]));
        Assert.Throws<ArgumentNullException>(() => WorldCoordinateSystem.FromHeader(null!));
        Assert.Throws<ArgumentNullException>(() => WorldCoordinateSystem.AlternatesIn(null!));
    }

    [Fact]
    public void Spectral_conversions_need_the_rest_value_but_linear_axes_do_not()
    {
        var h = new FitsHeader();
        h.Set("CTYPE1", "VRAD").Set("CRVAL1", 1000.0).Set("CDELT1", 1.0).Set("CRPIX1", 1.0);
        Assert.Equal(1004.0, WorldCoordinateSystem.FromHeader(h).PixelToWorld([5.0])[0]);   // sampled linearly in its own units: no rest value needed
        h.Set("CTYPE1", "VRAD-W2F");
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(h));              // a conversion does
        h.Set("RESTFRQ", 1.42e9);
        Assert.Equal(1000.0, WorldCoordinateSystem.FromHeader(h).PixelToWorld([1.0])[0], 6);
        h.Set("CTYPE1", "WAVE-TAB");
        Assert.Throws<NotSupportedException>(() => WorldCoordinateSystem.FromHeader(h));
        h.Set("CTYPE1", "WAVE-F2A");                                                        // the algorithm must produce the coordinate named by the type
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(h));
        h.Set("CTYPE1", "FREQ").Set("CUNIT1", "parsec");
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(h));
    }

    [Theory]
    [InlineData("VELOCITY")]
    [InlineData("WAVELENGTH")]
    [InlineData("ENERGY")]
    [InlineData("WAVNUM")]
    [InlineData("FREQ-LSR")]
    [InlineData("VELO-LSR")]
    [InlineData("WAVE-ABC")]
    [InlineData("FELO-LSR")]
    public void Unconventional_spectral_names_are_ordinary_linear_axes_and_do_not_spoil_the_celestial_axes(string ctype)
    {
        var h = Tan();
        h.Set("CTYPE3", ctype).Set("CRPIX3", 1.0).Set("CRVAL3", 5.0).Set("CDELT3", 2.0).Set("WCSAXES", 3);
        var wcs = WorldCoordinateSystem.FromHeader(h);
        Assert.True(wcs.IsCelestial);
        Assert.Equal(WorldAxisKind.Linear, wcs.AxisKinds[2]);
        Assert.Equal(9.0, wcs.PixelToWorld([100.5, 100.5, 3.0])[2]);
    }

    [Fact]
    public void A_celestial_failure_leaves_the_other_axes_of_a_cube_alone()
    {
        var h = Tan().Set("CTYPE1", "RA---SIN").Set("CTYPE2", "DEC--SIN").Set("CTYPE3", "FREQ").Set("CRPIX3", 1.0).Set("CRVAL3", 1.4e9).Set("CDELT3", 1e5).Set("WCSAXES", 3);
        var wcs = WorldCoordinateSystem.FromHeader(h);
        double[] world = wcs.PixelToWorld([1e6, 1e6, 5.0]);
        Assert.True(double.IsNaN(world[0]) && double.IsNaN(world[1]));
        Assert.Equal(1.4e9 + 4e5, world[2]);
        double[] pixel = wcs.WorldToPixel([263.8, 84.6, 1.4e9 + 4e5]);   // the antipode of the reference position: no image
        Assert.True(double.IsNaN(pixel[0]) && double.IsNaN(pixel[1]));
        Assert.Equal(5.0, pixel[2], 9);
    }

    [Fact]
    public void Relativistic_velocity_axes_near_the_speed_of_light_are_computed()
    {
        const double c = 299792458.0;
        foreach (double fraction in new[] { -0.99, 0.5, 0.99, 0.9999999 })
        {
            var h = new FitsHeader();
            double cdelt = Math.Min(1000.0, 1e-3 * (c - Math.Abs(fraction * c)));   // a pixel much smaller than the distance to the speed of light
            h.Set("CTYPE1", "VELO-F2V").Set("CRVAL1", fraction * c).Set("CDELT1", cdelt).Set("CRPIX1", 1.0).Set("RESTFRQ", 1.42e9);
            var wcs = WorldCoordinateSystem.FromHeader(h);
            Assert.Equal(fraction * c, wcs.PixelToWorld([1.0])[0], 3);
            // the local scale is the one of the header: one pixel is CDELT in velocity
            double step = (wcs.PixelToWorld([1.001])[0] - wcs.PixelToWorld([1.0])[0]) / 0.001;
            // (a velocity this close to c is only known to about 1e-8 m/s as a double: the step cannot be checked more closely)
            Assert.Equal(cdelt, step, cdelt * (fraction > 0.9999 ? 5e-2 : 1e-5));
            Assert.Equal(2.0, wcs.WorldToPixel(wcs.PixelToWorld([2.0]))[0], fraction > 0.9999 ? 3 : 6);
        }
        var beta = new FitsHeader();
        beta.Set("CTYPE1", "BETA-F2V").Set("CRVAL1", 0.999).Set("CDELT1", 1e-6).Set("CRPIX1", 1.0).Set("RESTFRQ", 1.42e9);
        Assert.Equal(0.999, WorldCoordinateSystem.FromHeader(beta).PixelToWorld([1.0])[0], 12);
    }

    // ---- findings of the review ---------------------------------------------------------------------------------------

    private static FitsHeader Projected(string code, double lon, double lat, params (string, double)[] pv)
    {
        var h = new FitsHeader();
        h.Set("CTYPE1", "RA---" + code).Set("CTYPE2", "DEC--" + code).Set("CRPIX1", 100.0).Set("CRPIX2", 100.0).Set("CRVAL1", lon).Set("CRVAL2", lat)
            .Set("CDELT1", -1.0).Set("CDELT2", 1.0);
        foreach (var (k, v) in pv) h.Set(k, v);
        return h;
    }

    [Theory]
    [InlineData("AZP", 0.0, 0.0)]
    [InlineData("AZP", 2.0, 20.0)]
    [InlineData("SZP", 0.0, 0.0)]
    [InlineData("SZP", 0.5, 30.0)]
    [InlineData("SIN", 0.3, -0.2)]
    [InlineData("SIN", 0.0, 0.0)]
    [InlineData("TAN", 0.0, 0.0)]
    public void The_far_side_of_a_perspective_projection_has_no_image(string code, double p1, double p2)
    {
        var h = Projected(code, 45.0, 45.0, ("PV2_1", p1), ("PV2_2", p2));
        var wcs = WorldCoordinateSystem.FromHeader(h);
        // the antipode and the hemisphere around it
        foreach (double[] world in new[] { new[] { 225.0, -45.0 }, new[] { 225.0, -10.0 }, new[] { 200.0, -60.0 }, new[] { 45.0, -80.0 }, new[] { 270.0, -30.0 } })
            Assert.All(wcs.WorldToPixel(world), v => Assert.True(double.IsNaN(v) || Math.Abs(v) > 1e6, $"{code} {p1} {p2} at [{world[0]}, {world[1]}] gave {v}"));
    }

    [Theory]
    [InlineData("SZP", 0.0, 0.0)]
    [InlineData("SIN", 0.3, -0.2)]
    [InlineData("AZP", 1.0, 0.0)]
    [InlineData("AZP", 2.0, 20.0)]
    [InlineData("TAN", 0.0, 0.0)]
    public void Points_next_to_the_reference_position_are_not_refused(string code, double p1, double p2)
    {
        var wcs = WorldCoordinateSystem.FromHeader(Projected(code, 150.0, 30.0, ("PV2_1", p1), ("PV2_2", p2)));
        foreach (double offset in new[] { 1e-7, 1e-6, 1e-5, 1e-4, 1e-3 })
        {
            double[] pixel = wcs.WorldToPixel([150.0 + offset, 30.0]);
            Assert.False(double.IsNaN(pixel[0]), $"{code}: {offset} degrees from CRVAL");
            Assert.Equal(100.0 - offset * Math.Cos(30 * Math.PI / 180), pixel[0], 3);
            Assert.Equal(100.0, pixel[1], 3);
        }
    }

    [Fact]
    public void The_pole_of_the_parabolic_projection_is_reachable_from_the_pixels()
    {
        var wcs = WorldCoordinateSystem.FromHeader(Projected("PAR", 0.0, 0.0));
        double[] world = wcs.PixelToWorld([100.0, 190.0]);
        Assert.Equal(90.0, world[1], 9);
        double[] pixel = wcs.WorldToPixel([0.0, 90.0]);
        Assert.Equal(100.0, pixel[0], 6);
        Assert.Equal(190.0, pixel[1], 6);
    }

    [Theory]
    [InlineData(85.0)]
    [InlineData(89.0)]
    [InlineData(89.99)]
    [InlineData(89.9999)]
    [InlineData(89.999999)]
    public void The_airy_projection_stays_accurate_when_theta_b_approaches_90(double thetaB)
    {
        var exact = WorldCoordinateSystem.FromHeader(Projected("AIR", 150.0, 30.0, ("PV2_1", 90.0)));
        var near = WorldCoordinateSystem.FromHeader(Projected("AIR", 150.0, 30.0, ("PV2_1", thetaB)));
        double[] a = exact.PixelToWorld([130.0, 120.0]), b = near.PixelToWorld([130.0, 120.0]);
        Assert.False(double.IsNaN(b[0]));
        // the dependence on theta_b is smooth: a change of (90 - theta_b) degrees moves the point by a similarly small amount
        double separation = Math.Sqrt(Math.Pow((a[0] - b[0]) * Math.Cos(30 * Math.PI / 180), 2) + Math.Pow(a[1] - b[1], 2));
        Assert.True(separation < (90.0 - thetaB) * 0.05 + 1e-9, $"theta_b {thetaB}: moved by {separation} degrees");
    }

    [Fact]
    public void Crota_is_the_rotation_of_the_latitude_axis_only()
    {
        // the rotation is used when it belongs to the latitude axis of a celestial description
        var rotated = Tan().Set("CROTA2", 30.0);
        var plain = Tan();
        double[] r = WorldCoordinateSystem.FromHeader(rotated).PixelToWorld([110.5, 100.5]);
        double[] p = WorldCoordinateSystem.FromHeader(plain).PixelToWorld([110.5, 100.5]);
        Assert.NotEqual(r[1], p[1], 6);
        // CROTA1 (on the longitude axis) and CROTA on a non-celestial pair are ignored
        double[] ignored = WorldCoordinateSystem.FromHeader(Tan().Set("CROTA1", 30.0)).PixelToWorld([110.5, 100.5]);
        Assert.Equal(p, ignored);
        var linear = new FitsHeader();
        linear.Set("CTYPE1", "X").Set("CTYPE2", "Y").Set("CRPIX1", 1.0).Set("CRPIX2", 1.0).Set("CROTA2", 45.0);
        Assert.Equal([4.0, 2.0], WorldCoordinateSystem.FromHeader(linear).PixelToWorld([5.0, 3.0]));   // no rotation: world = pixel - CRPIX
    }

    [Fact]
    public void Pv3_of_the_longitude_axis_overrides_lonpole()
    {
        var withKeyword = Projected("CAR", 120.0, 20.0).Set("LONPOLE", 60.0);
        var overridden = Projected("CAR", 120.0, 20.0).Set("LONPOLE", 0.0).Set("PV1_3", 60.0);
        double[] a = WorldCoordinateSystem.FromHeader(withKeyword).PixelToWorld([90.0, 110.0]);
        double[] b = WorldCoordinateSystem.FromHeader(overridden).PixelToWorld([90.0, 110.0]);
        Assert.Equal(a, b);
        Assert.Equal(60.0, WorldCoordinateSystem.FromHeader(overridden).LongitudePole);
    }

    [Fact]
    public void Unusable_celestial_parameters_are_refused()
    {
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE2", "GLAT-TAN")));     // RA with GLAT
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "GLON-TAN").Set("CTYPE2", "ELAT-TAN")));
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("PV1_5", 1.0)));               // only PVi_0 to PVi_4 on the longitude axis
        Assert.Throws<FitsException>(() => WorldCoordinateSystem.FromHeader(Tan().Set("PV1_0", 1.0).Set("PV1_1", 0.0).Set("PV1_2", 95.0)));
        Assert.Equal("TAN", WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "GLON-TAN").Set("CTYPE2", "GLAT-TAN")).ProjectionCode);
        Assert.Equal("TAN", WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "HPLN-TAN").Set("CTYPE2", "HPLT-TAN")).ProjectionCode);
    }

    [Fact]
    public void The_exposed_values_cannot_be_used_to_change_the_wcs()
    {
        var wcs = WorldCoordinateSystem.FromHeader(Tan());
        Assert.False(wcs.ReferencePixel is double[]);
        Assert.False(wcs.ReferenceValue is double[]);
        Assert.False(wcs.CoordinateTypes is string[]);
        Assert.False(wcs.Units is string[]);
        Assert.False(wcs.AxisKinds is WorldAxisKind[]);
    }

    [Fact]
    public void The_reference_system_defaults_follow_the_standard_and_distortions_are_reported()
    {
        Assert.Equal("ICRS", WorldCoordinateSystem.FromHeader(Tan()).RadeSys);
        Assert.Equal("FK4", WorldCoordinateSystem.FromHeader(Tan().Set("EQUINOX", 1950.0)).RadeSys);
        Assert.Equal("FK5", WorldCoordinateSystem.FromHeader(Tan().Set("EQUINOX", 2000.0)).RadeSys);
        Assert.Equal("FK5", WorldCoordinateSystem.FromHeader(Tan().Set("RADESYS", "FK5")).RadeSys);
        Assert.Null(WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "GLON-TAN").Set("CTYPE2", "GLAT-TAN")).RadeSys);
        Assert.Equal("-SIP", WorldCoordinateSystem.FromHeader(Tan().Set("CTYPE1", "RA---TAN-SIP").Set("CTYPE2", "DEC--TAN-SIP")).UnappliedDistortion);
        Assert.Equal("", WorldCoordinateSystem.FromHeader(Tan()).UnappliedDistortion);
    }

    [Fact]
    public void The_span_forms_give_the_same_answers_as_the_array_forms()
    {
        var wcs = WorldCoordinateSystem.FromHeader(Projected("MOL", 10.0, 20.0));
        double[] pixels = [100, 100, 120.5, 80.25, 60, 140, 1000, 1000];
        var world = new double[pixels.Length];
        wcs.PixelToWorld(pixels, world);
        for (int i = 0; i < pixels.Length; i += 2)
            Assert.Equal(wcs.PixelToWorld(pixels.AsSpan(i, 2)), world.AsSpan(i, 2).ToArray());
        var back = new double[pixels.Length];
        wcs.WorldToPixel(world, back);
        for (int i = 0; i < pixels.Length; i += 2)
            Assert.Equal(wcs.WorldToPixel(world.AsSpan(i, 2)), back.AsSpan(i, 2).ToArray());
        Assert.Throws<ArgumentException>(() => wcs.PixelToWorld(new double[3], new double[3]));
        Assert.Throws<ArgumentException>(() => wcs.PixelToWorld(new double[4], new double[2]));
        Assert.Throws<ArgumentException>(() => wcs.WorldToPixel(new double[4], new double[2]));
    }

    [Fact]
    public void A_wcs_read_from_a_saved_file_works()
    {
        var image = ImageHdu.Create<float>(new float[100 * 100], 100, 100);
        foreach (var card in Tan()) image.Header.Add(card);
        var ms = new MemoryStream();
        new FitsFile().Add(image).Save(ms);
        using var file = FitsFile.Read(new MemoryStream(ms.ToArray()));
        var wcs = file.Primary.GetWorldCoordinateSystem();
        Assert.Equal(83.8, wcs.PixelToWorld([100.5, 100.5])[0], 12);
    }

    [Fact]
    public void Random_headers_only_produce_fits_errors_and_never_hang_or_crash()
    {
        string[] codes = ["AZP", "SZP", "TAN", "STG", "SIN", "ARC", "ZPN", "ZEA", "AIR", "CYP", "CEA", "CAR", "MER", "SFL", "PAR", "MOL", "AIT",
            "COP", "COE", "COD", "COO", "BON", "PCO", "TSC", "CSC", "QSC", "HPX"];
        string[] spectral = ["FREQ", "WAVE-F2W", "VRAD-W2F", "AWAV-F2A", "VELO-F2V", "FREQ-LOG", "ZOPT-F2W", "BETA", "ENER", "WAVN"];
        double[] wild = [0.0, 1.0, -1.0, 0.5, 90.0, -90.0, 180.0, 360.0, 1e-300, 1e300, 45.0, 1e-9, -1e10, 89.999999, 1e5];
        double[] coordinates = [.. wild, double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        var rng = new Random(77);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int built = 0, refused = 0;
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FITSNET_FUZZ_ROUNDS"), out int deep) ? deep * 20 : 6000;
        for (int round = 0; round < rounds; round++)
        {
            var h = new FitsHeader();
            string code = codes[rng.Next(codes.Length)];
            bool spec = round % 5 == 0;
            double W() => rng.Next(3) == 0 ? wild[rng.Next(wild.Length)] : rng.NextDouble() * 400 - 200;
            if (spec)
            {
                h.Set("CTYPE1", spectral[rng.Next(spectral.Length)]).Set("CRPIX1", W()).Set("CRVAL1", W()).Set("CDELT1", W());
                h.Set("RESTFRQ", W()).Set("RESTWAV", W());
            }
            else
            {
                h.Set("CTYPE1", "RA---" + code).Set("CTYPE2", "DEC--" + code).Set("CRPIX1", W()).Set("CRPIX2", W()).Set("CRVAL1", W()).Set("CRVAL2", W())
                    .Set("CDELT1", W()).Set("CDELT2", W());
                for (int m = 0; m < 6; m++) if (rng.Next(3) == 0) h.Set($"PV2_{m}", W());
                if (rng.Next(4) == 0) h.Set("LONPOLE", W());
                if (rng.Next(4) == 0) h.Set("LATPOLE", W());
                if (rng.Next(6) == 0) h.Set("PC1_2", W());
                if (rng.Next(8) == 0) { h.Set("PV1_0", 1.0).Set("PV1_1", W()).Set("PV1_2", W()); }
            }
            try
            {
                var wcs = WorldCoordinateSystem.FromHeader(h);
                built++;
                for (int k = 0; k < 4; k++)
                {
                    double[] p = new double[wcs.AxisCount];
                    for (int i = 0; i < p.Length; i++) p[i] = rng.Next(4) == 0 ? coordinates[rng.Next(coordinates.Length)] : W();
                    double[] w = wcs.PixelToWorld(p);
                    Assert.Equal(p.Length, w.Length);
                    _ = wcs.WorldToPixel(w);
                    _ = wcs.WorldToPixel(p);
                }
            }
            catch (Exception e) when (e is FitsException or NotSupportedException or ArgumentException)
            {
                refused++;
            }
        }
        Assert.True(built > 500 && refused > 100, $"built {built}, refused {refused}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(rounds / 100.0 + 30), $"took {sw.Elapsed}");
    }
}
