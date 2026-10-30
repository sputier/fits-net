using System.Text.Json;
using FitsNet.Wcs;

namespace FitsNet.Tests;

/// <summary>Time coordinates (section 9): ISO-8601, JD/MJD, time scales, the global time keywords.</summary>
public class TimeTests
{
    private static readonly Lazy<JsonDocument> Vectors = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "time_vectors.json"))));

    private static TimeScale ScaleOf(string name) => TimeScales.Parse(name)!.Value;

    private static FitsTime FromJd(JsonElement parts, TimeScale scale) => FitsTime.FromJdParts(parts[0].GetDouble(), parts[1].GetDouble(), scale);

    // ---- ISO-8601 and Julian dates ----------------------------------------------------------------------------------

    [Fact]
    public void Datetimes_match_erfa_and_survive_formatting()
    {
        int n = 0;
        foreach (var c in Vectors.Value.RootElement.GetProperty("cases").EnumerateArray())
        {
            string iso = c.GetProperty("iso").GetString()!;
            var t = FitsTime.Parse(iso, TimeScale.UTC);
            var utc = c.GetProperty("utc");
            Assert.True(Math.Abs(t.SecondsSince(FitsTime.FromJdParts(utc.GetProperty("jd1").GetDouble(), utc.GetProperty("jd2").GetDouble(), TimeScale.UTC))) < 1e-6, iso);
            Assert.True(Math.Abs(t.Mjd - utc.GetProperty("mjd").GetDouble()) < 2e-11, iso);
            Assert.Equal(iso, t.ToIsoString(9));
            n++;
        }
        Assert.True(n > 300);
    }

    [Fact]
    public void Well_known_dates_have_their_known_julian_dates()
    {
        Assert.Equal(0.0, FitsTime.Parse("-04713-11-24T12:00:00", TimeScale.TT).Jd);
        Assert.Equal(0.0, FitsTime.Parse("1858-11-17", TimeScale.TT).Mjd);
        Assert.Equal(2451545.0, FitsTime.Parse("2000-01-01T12:00:00", TimeScale.TT).Jd);
        Assert.Equal(40587.0, FitsTime.Parse("1970-01-01T00:00:00", TimeScale.TT).Mjd);
        Assert.Equal(1721059.5, FitsTime.Parse("0000-01-01T00:00:00", TimeScale.TT).Jd); // year 0 is a leap year: 1 CE starts at 1721425.5
        Assert.Equal(2299160.5, FitsTime.Parse("1582-10-15", TimeScale.TT).Jd); // first Gregorian day
        Assert.Equal(2299159.5, FitsTime.Parse("1582-10-14", TimeScale.TT).Jd); // proleptic: the day before it, there was no 10-05..10-14 gap
    }

    [Theory]
    [InlineData("+99999-12-31T23:59:59")]
    [InlineData("-99999-01-01T00:00:00")]
    [InlineData("+10000-06-15T01:02:03.5")]
    [InlineData("-00001-02-28T00:00:00")]
    [InlineData("0000-02-29T23:59:59")]
    [InlineData("9999-12-31T23:59:59")]
    public void Five_digit_years_and_the_extremes_round_trip(string text)
    {
        var t = FitsTime.Parse(text, TimeScale.TT);
        Assert.Equal(text, t.ToIsoString(text.Contains('.') ? 1 : 0));
    }

    [Theory]
    [InlineData("2016-13-01")]
    [InlineData("2016-02-30")]
    [InlineData("2015-02-29")]
    [InlineData("2016-12-31T24:00:00")]
    [InlineData("2016-12-31T12:60:00")]
    [InlineData("2016-12-31T12:00:60")]   // not a leap-second day end? 2016-12-31 is: see the dedicated test; 12:00:60 is not
    [InlineData("2016-12-31T12:00:00Z")]
    [InlineData("2016-12-31T12:00:00+01:00")]
    [InlineData("16-12-31")]
    [InlineData("2016-1-1")]
    [InlineData("2016-12-31T12:00")]
    [InlineData("2016-12-31 12:00:00")]
    [InlineData("12016-01-01")]
    [InlineData("+2016-01-01")]
    [InlineData("2016-12-31T12:00:00.")]
    [InlineData("")]
    [InlineData("not a date")]
    public void Invalid_datetimes_are_refused(string text)
    {
        Assert.Throws<FormatException>(() => FitsTime.Parse(text, TimeScale.UTC));
        Assert.False(FitsTime.TryParse(text, TimeScale.UTC, out _));
    }

    [Fact]
    public void Second_sixty_exists_only_at_the_end_of_a_utc_day_with_a_leap_second()
    {
        Assert.Equal("2016-12-31T23:59:60.25", FitsTime.Parse("2016-12-31T23:59:60.25", TimeScale.UTC).ToIsoString(2));
        Assert.Throws<FormatException>(() => FitsTime.Parse("2016-12-30T23:59:60", TimeScale.UTC));
        Assert.Throws<FormatException>(() => FitsTime.Parse("2016-12-31T23:59:60", TimeScale.TAI));
        Assert.Throws<FormatException>(() => FitsTime.Parse("2016-12-31T23:59:61", TimeScale.UTC));
    }

    [Fact]
    public void Formatting_carries_a_rounded_up_second_into_the_next_day()
    {
        var t = FitsTime.Parse("2015-12-31T23:59:59.9996", TimeScale.TAI);
        Assert.Equal("2016-01-01T00:00:00.000", t.ToIsoString(3));
        Assert.Equal("2015-12-31T23:59:59.9996", t.ToIsoString(4));
        Assert.Equal("2015-12-31T23:59:59", FitsTime.Parse("2015-12-31T23:59:59.4", TimeScale.TAI).ToIsoString(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.ToIsoString(10));
    }

    [Fact]
    public void Mjd_and_jd_constructors_agree_with_each_other()
    {
        var a = FitsTime.FromMjdParts(55197.0, 0.25, TimeScale.TT);
        Assert.Equal(55197.25, a.Mjd);
        Assert.Equal(2455197.75, a.Jd);
        Assert.Equal("2010-01-01T06:00:00", a.ToIsoString(0));
        Assert.Equal(a, FitsTime.FromJd(2455197.75, TimeScale.TT));
        Assert.Equal(a, FitsTime.FromJdParts(2455197.0, 0.75, TimeScale.TT));
        Assert.Equal(a, FitsTime.FromMjdParts(55196.0, 1.25, TimeScale.TT)); // the fraction may exceed a day
        Assert.Equal(FitsTime.FromMjd(-0.5, TimeScale.TT), FitsTime.FromMjdParts(-1.0, 0.5, TimeScale.TT));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.FromMjdParts(double.NaN, 0, TimeScale.TT));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.FromMjdParts(1e15, 0, TimeScale.TT));
    }

    // ---- time scales ------------------------------------------------------------------------------------------------

    [Fact]
    public void Conversions_between_the_scales_match_erfa()
    {
        int n = 0;
        foreach (var c in Vectors.Value.RootElement.GetProperty("cases").EnumerateArray())
        {
            var utc = FitsTime.Parse(c.GetProperty("iso").GetString()!, TimeScale.UTC);
            foreach (var target in c.GetProperty("to").EnumerateObject())
            {
                var jd = target.Value;
                var want = FitsTime.FromJdParts(jd.GetProperty("jd1").GetDouble(), jd.GetProperty("jd2").GetDouble(), ScaleOf(target.Name));
                var got = utc.To(ScaleOf(target.Name));
                Assert.True(Math.Abs(got.SecondsSince(want)) < 5e-8, $"{c.GetProperty("iso").GetString()} -> {target.Name}: {got.SecondsSince(want)} s");
                n++;
            }
        }
        Assert.True(n > 1000);
    }

    [Fact]
    public void Conversions_from_any_linear_scale_match_erfa_and_come_back()
    {
        int n = 0;
        foreach (var c in Vectors.Value.RootElement.GetProperty("reverse").EnumerateArray())
        {
            var from = ScaleOf(c.GetProperty("from").GetString()!);
            var to = ScaleOf(c.GetProperty("to").GetString()!);
            var source = FromJd(c.GetProperty("fromJd"), from);
            var want = FromJd(c.GetProperty("toJd"), to);
            var got = source.To(to);
            Assert.True(Math.Abs(got.SecondsSince(want)) < 5e-8, $"{from} -> {to}: {got.SecondsSince(want)} s");
            Assert.True(Math.Abs(got.To(from).SecondsSince(source)) < 5e-8, $"back {to} -> {from}");
            n++;
        }
        Assert.True(n > 2000);
    }

    [Fact]
    public void Fixed_offsets_between_the_atomic_scales()
    {
        var tai = FitsTime.Parse("2020-06-15T12:00:00", TimeScale.TAI);
        Assert.Equal("2020-06-15T12:00:32.184", tai.To(TimeScale.TT).ToIsoString(3));
        Assert.Equal("2020-06-15T11:59:41", tai.To(TimeScale.GPS).ToIsoString(0));
        Assert.Equal("2020-06-15T11:59:23", tai.To(TimeScale.UTC).ToIsoString(0));       // TAI - UTC = 37 s since 2017
        Assert.Equal("2017-01-01T00:00:37", FitsTime.Parse("2017-01-01T00:00:00", TimeScale.UTC).To(TimeScale.TAI).ToIsoString(0));
        // the leap second 2016-12-31T23:59:60 UTC is 2017-01-01T00:00:36 TAI (TAI - UTC was 36 s before it, 37 s after)
        Assert.Equal("2017-01-01T00:00:36", FitsTime.Parse("2016-12-31T23:59:60", TimeScale.UTC).To(TimeScale.TAI).ToIsoString(0));
        Assert.Equal("2016-12-31T23:59:60", FitsTime.Parse("2017-01-01T00:00:36", TimeScale.TAI).To(TimeScale.UTC).ToIsoString(0));
        Assert.Equal("2016-12-31T23:59:59", FitsTime.Parse("2017-01-01T00:00:35", TimeScale.TAI).To(TimeScale.UTC).ToIsoString(0));
        Assert.Equal("2017-01-01T00:00:00", FitsTime.Parse("2017-01-01T00:00:37", TimeScale.TAI).To(TimeScale.UTC).ToIsoString(0));
    }

    [Fact]
    public void Adding_seconds_crosses_a_leap_second_in_utc()
    {
        var t = FitsTime.Parse("2016-12-31T23:59:59", TimeScale.UTC);
        Assert.Equal("2016-12-31T23:59:60", t.AddSeconds(1).ToIsoString(0));
        Assert.Equal("2017-01-01T00:00:00", t.AddSeconds(2).ToIsoString(0));
        Assert.Equal("2016-12-31T23:59:59", t.AddSeconds(2).AddSeconds(-2).ToIsoString(0));
        Assert.Equal(2.0, t.AddSeconds(2).SecondsSince(t));
        Assert.Equal(86401.0, FitsTime.Parse("2017-01-01T00:00:00", TimeScale.UTC).SecondsSince(FitsTime.Parse("2016-12-31T00:00:00", TimeScale.UTC)));
        Assert.Throws<ArgumentException>(() => t.SecondsSince(t.To(TimeScale.TAI)));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.AddSeconds(double.NaN));
    }

    [Fact]
    public void Conversions_that_need_external_data_are_refused()
    {
        var utc = FitsTime.Parse("2020-01-01", TimeScale.UTC);
        Assert.Throws<NotSupportedException>(() => utc.To(TimeScale.UT1));
        Assert.Throws<NotSupportedException>(() => utc.To(TimeScale.Local));
        Assert.Throws<NotSupportedException>(() => utc.To(TimeScale.TDB));   // needs a time ephemeris
        Assert.Throws<NotSupportedException>(() => FitsTime.Parse("1971-12-31", TimeScale.UTC).To(TimeScale.TAI));   // rubber seconds
        Assert.Throws<NotSupportedException>(() => FitsTime.Parse("1960-01-01", TimeScale.TAI).To(TimeScale.UTC));
        Assert.True(TimeScales.CanConvert(TimeScale.TAI, TimeScale.TCG));
        Assert.True(TimeScales.CanConvert(TimeScale.TDB, TimeScale.TCB));
        Assert.False(TimeScales.CanConvert(TimeScale.TT, TimeScale.TDB));
        Assert.True(TimeScales.CanConvert(TimeScale.UT1, TimeScale.UT1));
        Assert.Equal(utc, utc.To(TimeScale.UTC));
    }

    [Theory]
    [InlineData("TAI", TimeScale.TAI)]
    [InlineData("IAT", TimeScale.TAI)]
    [InlineData("TT", TimeScale.TT)]
    [InlineData("TDT", TimeScale.TT)]
    [InlineData("ET", TimeScale.TT)]
    [InlineData("UTC", TimeScale.UTC)]
    [InlineData("GMT", TimeScale.UTC)]
    [InlineData("utc", TimeScale.UTC)]
    [InlineData("TT(TAI)", TimeScale.TT)]
    [InlineData("TT(BIPM08)", TimeScale.TT)]
    [InlineData("UTC(NIST)", TimeScale.UTC)]
    [InlineData("GPS", TimeScale.GPS)]
    [InlineData("TCG", TimeScale.TCG)]
    [InlineData("TCB", TimeScale.TCB)]
    [InlineData("TDB", TimeScale.TDB)]
    [InlineData("UT1", TimeScale.UT1)]
    [InlineData("LOCAL", TimeScale.Local)]
    public void Time_scale_names_and_synonyms(string name, TimeScale expected) => Assert.Equal(expected, TimeScales.Parse(name));

    [Fact]
    public void Unknown_scale_names_are_null()
    {
        Assert.Null(TimeScales.Parse("MET"));
        Assert.Null(TimeScales.Parse(null));
        Assert.Null(TimeScales.Parse(""));
    }

    // ---- the frame of a header ---------------------------------------------------------------------------------------

    private static FitsHeader H(params (string Key, object Value)[] cards)
    {
        var h = new FitsHeader();
        foreach (var (k, v) in cards) h.Set(k, v);
        return h;
    }

    [Fact]
    public void Defaults_follow_the_standard()
    {
        var f = TimeFrame.FromHeader(H());
        Assert.Equal(TimeScale.UTC, f.Scale);
        Assert.Equal("s", f.Unit);
        Assert.Equal(1.0, f.SecondsPerUnit);
        Assert.Equal("TOPOCENTER", f.ReferencePosition);
        Assert.Equal("DE405", f.Ephemeris);
        Assert.Equal(0.5, f.TimePixelReference);
        Assert.Equal(0.0, f.TimeOffset);
        Assert.Equal(0.0, f.SystematicError);
        Assert.Null(f.TimeResolution);
        Assert.False(f.HasExplicitReference);
        Assert.Equal(0.0, f.ReferenceTime!.Value.Mjd);
        Assert.Null(f.Start);
        Assert.Null(f.Observation);
    }

    [Fact]
    public void The_reference_time_follows_the_precedence_rules()
    {
        var mjd = TimeFrame.FromHeader(H(("TIMESYS", "TT"), ("MJDREF", 55197.0), ("JDREF", 2400000.5), ("DATEREF", "1990-01-01")));
        Assert.Equal(55197.0, mjd.ReferenceTime!.Value.Mjd);
        Assert.True(mjd.HasExplicitReference);
        var jd = TimeFrame.FromHeader(H(("JDREF", 2455197.5), ("DATEREF", "1990-01-01")));
        Assert.Equal(55197.0, jd.ReferenceTime!.Value.Mjd);
        var date = TimeFrame.FromHeader(H(("DATEREF", "2010-01-01T00:00:00")));
        Assert.Equal(55197.0, date.ReferenceTime!.Value.Mjd);
        var parts = TimeFrame.FromHeader(H(("MJDREFI", 55197L), ("MJDREFF", 0.000000001)));
        Assert.Equal(55197L, parts.ReferenceTime!.Value.Day);
        Assert.Equal(8.64e-5, parts.ReferenceTime.Value.SecondOfDay, 12);
        // parts win over the single value when both parts are there, the single value wins over one part
        var both = TimeFrame.FromHeader(H(("MJDREF", 1.0), ("MJDREFI", 55197L), ("MJDREFF", 0.5)));
        Assert.Equal(55197.5, both.ReferenceTime!.Value.Mjd);
        var one = TimeFrame.FromHeader(H(("MJDREF", 1.0), ("MJDREFI", 55197L)));
        Assert.Equal(1.0, one.ReferenceTime!.Value.Mjd);
        var jdParts = TimeFrame.FromHeader(H(("JDREFI", 2455197L), ("JDREFF", 0.5)));
        Assert.Equal(55197.0, jdParts.ReferenceTime!.Value.Mjd);
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("DATEREF", "not a date"))));
    }

    [Fact]
    public void Time_values_are_relative_to_the_reference_time_and_offset()
    {
        var f = TimeFrame.FromHeader(H(("TIMESYS", "TDB"), ("MJDREF", 55197.0), ("TIMEUNIT", "d"), ("TIMEOFFS", 2.0), ("TSTART", 10.5), ("TSTOP", 20.0)));
        Assert.Equal(TimeScale.TDB, f.Scale);
        Assert.Equal(86400.0, f.SecondsPerUnit);
        Assert.Equal(55197.0 + 2 + 8.5, f.ToTime(8.5).Mjd);   // the offset is added to the value
        Assert.Equal(55197.0 + 2 + 10.5, f.Start!.Value.Mjd);
        Assert.Equal(55197.0 + 2 + 20.0, f.Stop!.Value.Mjd);
        Assert.Equal(10.5, f.ToValue(f.Start.Value), 12);
        Assert.Equal(TimeScale.TDB, f.Start.Value.Scale);
    }

    [Theory]
    [InlineData("s", 1.0)]
    [InlineData("min", 60.0)]
    [InlineData("h", 3600.0)]
    [InlineData("d", 86400.0)]
    [InlineData("a", 31557600.0)]
    [InlineData("yr", 31557600.0)]
    [InlineData("cy", 3155760000.0)]
    public void Time_units(string unit, double seconds) => Assert.Equal(seconds, TimeFrame.FromHeader(H(("TIMEUNIT", unit))).SecondsPerUnit);

    [Fact]
    public void Tropical_and_besselian_years_have_the_lengths_of_the_standard()
    {
        // at J2000 the tropical year is 365.24219040211236 d, at J1900 the Besselian year is 365.2421987817 d
        var ta = TimeFrame.FromHeader(H(("TIMEUNIT", "ta"), ("JDREF", 2451545.0)));
        Assert.Equal(365.24219040211236 * 86400.0, ta.SecondsPerUnit, 6);
        var ba = TimeFrame.FromHeader(H(("TIMEUNIT", "Ba"), ("JDREF", 2415020.0)));
        Assert.Equal(365.2421987817 * 86400.0, ba.SecondsPerUnit, 6);
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("TIMEUNIT", "fortnight"))));
    }

    [Fact]
    public void A_local_or_unknown_scale_has_no_reference_time()
    {
        var f = TimeFrame.FromHeader(H(("TIMESYS", "MET"), ("MJDREF", 55197.0), ("TSTART", 5.0)));
        Assert.Null(f.Scale);
        Assert.Null(f.ReferenceTime);
        Assert.Null(f.Start);
        Assert.Throws<InvalidOperationException>(() => f.ToTime(1.0));
        Assert.Throws<InvalidOperationException>(() => f.ToValue(FitsTime.FromMjd(1.0, TimeScale.TT)));
        Assert.Null(TimeFrame.FromHeader(H(("TIMESYS", "LOCAL"))).ReferenceTime);
    }

    [Fact]
    public void Global_dates_prefer_mjd_over_date_keywords_and_use_the_time_scale()
    {
        var f = TimeFrame.FromHeader(H(("TIMESYS", "TAI"), ("DATE-OBS", "2020-01-01T00:00:00"), ("MJD-OBS", 58850.5), ("DATE-BEG", "2020-01-01T12:00:00"),
            ("DATE-END", "2020-01-02"), ("MJD-AVG", 58850.75), ("XPOSURE", 2.0), ("TELAPSE", 3.0), ("TIMEUNIT", "d")));
        Assert.Equal(58850.5, f.Observation!.Value.Mjd);
        Assert.Equal(TimeScale.TAI, f.Observation.Value.Scale);
        Assert.Equal("2020-01-01T12:00:00", f.Begin!.Value.ToIsoString(0));
        Assert.Equal("2020-01-02T00:00:00", f.End!.Value.ToIsoString(0));
        Assert.Equal(58850.75, f.Average!.Value.Mjd);
        Assert.Equal(2 * 86400.0, f.Exposure);
        Assert.Equal(3 * 86400.0, f.ElapsedTime);
    }

    [Fact]
    public void Julian_and_besselian_epochs()
    {
        Assert.Equal(2451545.0, TimeFrame.FromJulianEpoch(2000.0).Jd);
        Assert.Equal(TimeScale.TDB, TimeFrame.FromJulianEpoch(2000.0).Scale);
        Assert.Equal(2433282.4235, TimeFrame.FromBesselianEpoch(1950.0).Jd, 4);
        Assert.Equal(TimeScale.TT, TimeFrame.FromBesselianEpoch(1950.0).Scale);
        var f = TimeFrame.FromHeader(H(("JEPOCH", 2015.5), ("BEPOCH", 1950.0)));
        Assert.Equal(2451545.0 + 15.5 * 365.25, f.JulianEpoch!.Value.Jd, 6);
        Assert.Equal(2433282.4235, f.BesselianEpoch!.Value.Jd, 4);
    }

    [Fact]
    public void Axis_and_column_keywords_override_the_global_ones()
    {
        var h = H(("TIMESYS", "UTC"), ("TIMEUNIT", "s"), ("MJDREF", 50000.0), ("TREFPOS", "GEOCENTER"), ("TIMSYER", 1.0),
            ("CTYPE3", "TT"), ("CUNIT3", "d"), ("CSYER3", 0.25), ("CRDER3", 0.125),
            ("TCTYP2", "TAI"), ("TCUNI2", "min"), ("TRPOS2", "BARYCENTER"), ("TRDIR2", "RA,DEC"));
        var axis = TimeFrame.ForAxis(h, 3);
        Assert.Equal(TimeScale.TT, axis.Scale);
        Assert.Equal(86400.0, axis.SecondsPerUnit);
        Assert.Equal(0.25, axis.SystematicError);
        Assert.Equal(0.125, axis.RandomError);
        Assert.Equal("GEOCENTER", axis.ReferencePosition);
        Assert.Equal(50000.0, axis.ReferenceTime!.Value.Mjd);
        var column = TimeFrame.ForColumn(h, 2);
        Assert.Equal(TimeScale.TAI, column.Scale);
        Assert.Equal(60.0, column.SecondsPerUnit);
        Assert.Equal("BARYCENTER", column.ReferencePosition);
        Assert.Equal("RA,DEC", column.ReferenceDirection);
        Assert.Equal(1.0, column.SystematicError);
        // CTYPE 'TIME' takes the scale of TIMESYS
        h.Set("CTYPE3", "TIME").Set("TIMESYS", "TCG");
        Assert.Equal(TimeScale.TCG, TimeFrame.ForAxis(h, 3).Scale);
        h.Set("CTYPE3", "RA---TAN");
        Assert.Throws<ArgumentException>(() => TimeFrame.ForAxis(h, 3));
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("TIMEPIXR", 1.5))));
    }

    [Fact]
    public void A_time_axis_in_a_wcs_is_a_linear_axis_of_time_values()
    {
        var h = H(("CTYPE1", "TIME"), ("CRPIX1", 1.0), ("CRVAL1", 100.0), ("CDELT1", 2.0), ("MJDREF", 55197.0), ("TIMESYS", "TT"), ("CUNIT1", "s"),
            ("CTYPE2", "PHASE"), ("CRPIX2", 1.0), ("CRVAL2", 0.0), ("CDELT2", 0.1), ("CZPHS2", 5.0), ("CPERI2", 3.5),
            ("CTYPE3", "FREQUENCY"), ("CRPIX3", 1.0), ("CRVAL3", 0.0), ("CDELT3", 1.0),
            ("CTYPE4", "TIMELAG"), ("CRPIX4", 1.0), ("CRVAL4", 0.0), ("CDELT4", 1.0), ("WCSAXES", 4));
        var wcs = WorldCoordinateSystem.FromHeader(h);
        Assert.Equal([WorldAxisKind.Time, WorldAxisKind.Phase, WorldAxisKind.TemporalFrequency, WorldAxisKind.TimeLag], wcs.AxisKinds);
        Assert.Equal(0, wcs.TimeAxis);
        Assert.Equal(5.0, wcs.PhaseZeroPoint(1));
        Assert.Equal(3.5, wcs.PhasePeriod(1));
        Assert.Null(wcs.PhaseZeroPoint(0));
        double[] world = wcs.PixelToWorld([6.0, 1.0, 1.0, 1.0]);
        Assert.Equal(110.0, world[0]);
        var frame = TimeFrame.ForAxis(h, 1);
        Assert.Equal(55197.0 + 110.0 / 86400.0, frame.ToTime(world[0]).Mjd, 9);
    }

    // ---- findings of the review --------------------------------------------------------------------------------------

    [Fact]
    public void The_default_utc_frame_works_with_a_reference_before_1972()
    {
        // TIMESYS defaults to UTC and MJDREF to 0: the very defaults of the standard
        var f = TimeFrame.FromHeader(H(("TSTART", 100L)));
        Assert.Equal(100.0, f.Start!.Value.SecondsSince(f.ReferenceTime!.Value));
        Assert.Equal(TimeScale.UTC, f.Start.Value.Scale);
        var unix = TimeFrame.FromHeader(H(("MJDREF", 40587.0), ("TSTART", 100L), ("TSTOP", 200L)));
        Assert.Equal("1970-01-01T00:01:40", unix.Start!.Value.ToIsoString(0));
        Assert.Equal("1970-01-01T00:03:20", unix.ToTime(200.0).ToIsoString(0));
        Assert.Equal(200.0, unix.ToValue(unix.Stop!.Value));
        // arithmetic works before 1972; converting to another scale does not (the rubber-second offsets are not carried)
        Assert.Throws<NotSupportedException>(() => unix.Start.Value.To(TimeScale.TAI));
        var across = FitsTime.Parse("1971-12-31T23:59:50", TimeScale.UTC);
        Assert.Equal("1972-01-01T00:00:10", across.AddSeconds(20).ToIsoString(0));
        Assert.Equal(20.0, across.AddSeconds(20).SecondsSince(across));
    }

    [Fact]
    public void Hostile_time_values_are_fits_errors_never_overflows()
    {
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("TIMESYS", "TT"), ("TSTART", 1e30))));
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("TIMESYS", "TT"), ("MJDREFI", 1L), ("MJDREFF", 1e300))));
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("MJD-OBS", 1e20))));
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("JEPOCH", 1e30))));
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("MJD-OBS", 1e11))));
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("TIMESYS", "TT"), ("TSTART", 1e15), ("MJDREF", 1e11))));
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("MJDREF", "yesterday"))));
        Assert.Throws<FitsException>(() => TimeFrame.FromHeader(H(("DATE-OBS", "not a date"))));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.FromDateTime(long.MaxValue / 2, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.FromDateTime(long.MinValue, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.FromDateTime(100000, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.FromMjdParts(0, 1e300, TimeScale.TT));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.FromMjdParts(0, 1e15, TimeScale.TT));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.FromMjd(1e12, TimeScale.TT));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.FromMjd(5e7, TimeScale.TT));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.Parse("2000-01-01", TimeScale.TT).AddSeconds(1e30));
        Assert.Throws<ArgumentOutOfRangeException>(() => FitsTime.Parse("2000-01-01", TimeScale.TT).AddSeconds(4e15));
        // and the ends of the range hold
        Assert.Equal("+99999-12-31T00:00:00", FitsTime.FromDateTime(99999, 12, 31, scale: TimeScale.TT).ToIsoString(0));
        Assert.Equal("-99999-01-01T00:00:00", FitsTime.FromDateTime(-99999, 1, 1, scale: TimeScale.TT).ToIsoString(0));
    }

    [Fact]
    public void Instants_stay_canonical_after_conversions_and_round_trips_compare_equal()
    {
        foreach (string text in new[] { "2020-01-01T00:00:32.184", "2020-01-01T00:00:00", "2016-12-31T23:59:59.5", "2000-02-29T12:00:00.184", "1999-12-31T23:59:59.816" })
        {
            foreach (var scale in new[] { TimeScale.TAI, TimeScale.TT, TimeScale.GPS, TimeScale.TCG, TimeScale.UTC })
            {
                var start = FitsTime.Parse(text, scale == TimeScale.UTC ? TimeScale.UTC : TimeScale.TAI);
                foreach (var target in new[] { TimeScale.TAI, TimeScale.TT, TimeScale.GPS, TimeScale.UTC })
                {
                    var converted = start.To(target);
                    Assert.True(converted.SecondOfDay >= 0.0 && converted.SecondOfDay < 86400.0 + (converted.Scale == TimeScale.UTC ? 1.0 : 0.0),
                        $"{text} {start.Scale} -> {target}: {converted.SecondOfDay}");
                }
            }
        }
        var midnight = FitsTime.Parse("2020-01-01T00:00:00", TimeScale.TAI);
        Assert.Equal(midnight, midnight.To(TimeScale.TT).To(TimeScale.TAI));
        var x184 = FitsTime.Parse("2020-01-01T00:00:32.184", TimeScale.TT);
        Assert.Equal(0.0, x184.To(TimeScale.TAI).SecondOfDay);
        Assert.Equal(x184, x184.To(TimeScale.TAI).To(TimeScale.TT));
        Assert.Equal(58849, x184.To(TimeScale.TAI).Day);
        Assert.Equal(("2020-01-01T00:00:00"), x184.To(TimeScale.TAI).ToIsoString(0));
    }

    [Fact]
    public void Instants_compare_by_time_on_one_scale()
    {
        var a = FitsTime.Parse("2020-01-01", TimeScale.TAI);
        var b = a.AddSeconds(0.5);
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.Equal(0, a.CompareTo(a));
        Assert.Throws<ArgumentException>(() => a.CompareTo(a.To(TimeScale.TT)));
    }

    [Fact]
    public void Seconds_just_below_a_minute_stay_valid()
    {
        var t = FitsTime.Parse("2020-01-01T12:00:59.99999999999999999", TimeScale.UTC);
        Assert.True(t.SecondOfDay <= 12 * 3600 + 60);
        Assert.Equal("2020-01-01T12:01:00", t.ToIsoString(0));
        var end = FitsTime.Parse("2020-01-01T23:59:59.99999999999999999", TimeScale.UTC);
        Assert.True(end.SecondOfDay < 86400.0);
    }

    [Fact]
    public void An_axis_or_column_unit_changes_only_its_own_values()
    {
        var h = H(("TIMESYS", "TT"), ("MJDREF", 50000.0), ("TSTART", 100L), ("TSTOP", 200L), ("XPOSURE", 50.0), ("TCUNI3", "d"), ("CTYPE1", "TT"), ("CUNIT1", "d"),
            ("TIMEOFFS", 10.0), ("TCTYP3", "TT"));
        var global = TimeFrame.FromHeader(h);
        var column = TimeFrame.ForColumn(h, 3);
        var axis = TimeFrame.ForAxis(h, 1);
        // TSTART, TSTOP and XPOSURE are in TIMEUNIT (seconds) whatever the frame
        Assert.Equal(global.Start, column.Start);
        Assert.Equal(global.Stop, column.Stop);
        Assert.Equal(50.0, column.Exposure);
        Assert.Equal(86400.0, column.SecondsPerUnit);
        Assert.Equal(50000.0 + 110.0 / 86400.0, global.Start!.Value.Mjd, 9);
        // the values of the column are in days; TIMEOFFS (seconds) still shifts the reference
        Assert.Equal(50000.0 + 10.0 / 86400.0 + 2.0, column.ToTime(2.0).Mjd, 9);
        Assert.Equal(2.0, column.ToValue(column.ToTime(2.0)), 9);
        // TIMEOFFS is for tables, not for images
        Assert.Equal(0.0, axis.TimeOffset);
        Assert.Equal(50000.0 + 2.0, axis.ToTime(2.0).Mjd, 9);
        Assert.Equal(10.0, global.TimeOffset);
    }

    [Fact]
    public void Dates_of_the_header_are_in_the_time_system_of_timesys()
    {
        var h = H(("TIMESYS", "TT"), ("DATE-OBS", "2020-01-01T00:00:00"), ("TCTYP2", "TAI"));
        Assert.Equal(TimeScale.TT, TimeFrame.ForColumn(h, 2).Observation!.Value.Scale);
        Assert.Equal(TimeScale.TAI, TimeFrame.ForColumn(h, 2).Scale);
    }

    [Theory]
    [InlineData("S")]
    [InlineData("MIN")]
    [InlineData("H")]
    [InlineData("D")]
    [InlineData("CY")]
    public void Time_unit_letters_are_not_case_sensitive(string unit) =>
        Assert.True(TimeFrame.FromHeader(H(("TIMEUNIT", unit))).SecondsPerUnit > 0);

    [Fact]
    public void Random_time_headers_and_strings_only_produce_documented_errors()
    {
        var rng = new Random(2026);
        double[] wild = [0.0, 1.0, -1.0, 0.5, 1e-300, 1e300, -1e300, 1e9, 1e10, 1e11, 1e12, 1e15, 1e20, 1e30, 41317.0, 40587.0, 51544.5, -1e6, 2451545.0, 99999.0, 1e5, 3.6e7, -3.7e7];
        string[] scales = ["UTC", "TT", "TAI", "TDB", "TCB", "TCG", "GPS", "UT1", "LOCAL", "MET", "TT(TAI)", ""];
        string[] units = ["s", "d", "a", "yr", "cy", "min", "h", "ta", "Ba", "fortnight", ""];
        string[] keys = ["MJDREF", "JDREF", "MJDREFI", "MJDREFF", "JDREFI", "JDREFF", "TSTART", "TSTOP", "TIMEOFFS", "TIMEDEL", "XPOSURE", "TELAPSE", "MJD-OBS",
            "MJD-BEG", "MJD-AVG", "MJD-END", "JEPOCH", "BEPOCH", "TIMSYER", "TIMRDER", "TIMEPIXR"];
        string[] texts = ["2020-01-01T00:00:00", "-99999-12-31T23:59:59.5", "garbage", "", "1582-10-15", "0000-02-29", "9999-13-01", "2016-12-31T23:59:60", "+100000-01-01"];
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FITSNET_FUZZ_ROUNDS"), out int deep) ? deep * 20 : 5000;
        int built = 0, refused = 0;
        for (int round = 0; round < rounds; round++)
        {
            var h = new FitsHeader();
            if (rng.Next(2) == 0) h.Set("TIMESYS", scales[rng.Next(scales.Length)]);
            if (rng.Next(2) == 0) h.Set("TIMEUNIT", units[rng.Next(units.Length)]);
            foreach (string key in keys)
                if (rng.Next(5) == 0) h.Set(key, rng.Next(4) == 0 ? (object)((long)Math.Clamp(wild[rng.Next(wild.Length)], -1e15, 1e15) % 1000000) : wild[rng.Next(wild.Length)]);
            foreach (string key in new[] { "DATEREF", "DATE-OBS", "DATE-BEG", "DATE-AVG", "DATE-END" })
                if (rng.Next(6) == 0) h.Set(key, texts[rng.Next(texts.Length)]);
            try
            {
                var f = TimeFrame.FromHeader(h);
                built++;
                for (int k = 0; k < 3; k++)
                {
                    double v = rng.Next(4) == 0 ? double.NaN : wild[rng.Next(wild.Length)];
                    try
                    {
                        var t = f.ToTime(v);
                        _ = f.ToValue(t);
                        _ = t.ToIsoString(rng.Next(10));
                        _ = t.To(TimeScale.TAI).To(t.Scale);
                    }
                    catch (Exception e) when (e is ArgumentOutOfRangeException or NotSupportedException or InvalidOperationException)
                    {
                    }
                }
            }
            catch (FitsException)
            {
                refused++;
            }
        }
        Assert.True(built > 100 && refused > 100, $"built {built}, refused {refused}");

        // parsing and building instants straight from hostile numbers and text
        for (int round = 0; round < rounds; round++)
        {
            string text = rng.Next(3) == 0 ? texts[rng.Next(texts.Length)] : new string(Enumerable.Range(0, rng.Next(0, 30)).Select(_ => "0123456789-+T:. Z"[rng.Next(17)]).ToArray());
            try
            {
                var t = FitsTime.Parse(text, (TimeScale)rng.Next(9));
                Assert.True(FitsTime.TryParse(t.ToIsoString(rng.Next(10)), t.Scale, out _));
            }
            catch (FormatException)
            {
            }
            try
            {
                var t = FitsTime.FromMjd(wild[rng.Next(wild.Length)] * (rng.NextDouble() + 0.5), (TimeScale)rng.Next(9));
                _ = t.AddSeconds(wild[rng.Next(wild.Length)]).ToIsoString(3);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
    }
}
