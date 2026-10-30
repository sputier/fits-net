using System.Globalization;

namespace FitsNet.Wcs;

/// <summary>
/// The time coordinate frame of an HDU (standard section 9): time scale, unit, reference time and position, errors, the global time
/// keywords, and the means to turn the time values of the data (an image axis, a table column) into <see cref="FitsTime"/> instants.
/// </summary>
public sealed class TimeFrame
{
    private TimeFrame(FitsHeader h, string scaleName, string unit, string? referencePosition, string? referenceDirection, double? systematicError,
        double? randomError, bool imageAxis)
    {
        TimeSystem = scaleName;
        Scale = TimeScales.Parse(scaleName);
        Unit = unit;
        ReferencePosition = referencePosition ?? "TOPOCENTER";
        ReferenceDirection = referenceDirection;
        Ephemeris = h.Get<string>("PLEPHEM")?.Trim() ?? "DE405";
        SystematicError = systematicError ?? (Number(h, "TIMSYER", out double sy) ? sy : 0.0);
        RandomError = randomError ?? (Number(h, "TIMRDER", out double rd) ? rd : 0.0);
        // TIMEOFFS is for tables: it must not be used in images
        TimeOffset = !imageAxis && Number(h, "TIMEOFFS", out double offs) ? offs : 0.0;
        TimeResolution = Number(h, "TIMEDEL", out double del) ? del : null;
        TimePixelReference = Number(h, "TIMEPIXR", out double pixr) ? pixr : 0.5;
        if (TimePixelReference is < 0.0 or > 1.0) throw new FitsException($"TIMEPIXR {TimePixelReference} must be between 0 and 1.");

        // the reference time is only applied to the recognised scales (a local clock such as MET has its own zero)
        ReferenceTime = Scale is { } s and not TimeScale.Local ? ReadReference(h, s) : null;
        HasExplicitReference = ReferenceTime is not null && ReadReferenceIsExplicit(h);

        // TSTART, TSTOP, XPOSURE, TELAPSE and TIMEDEL are in the global unit; an axis or a column only changes the unit of its own values
        SecondsPerUnit = UnitSeconds(unit, ReferenceTime);
        double globalUnit = UnitSeconds(h.Get<string>("TIMEUNIT")?.Trim() ?? "s", ReferenceTime);
        _globalSecondsPerUnit = globalUnit;

        // the dates of the header are in the time system of TIMESYS
        var dateScale = TimeScales.Parse(h.Get<string>("TIMESYS")?.Trim() ?? "UTC");
        FitsTime? Global(string date, string mjd)
        {
            if (dateScale is not { } sc) return null;
            if (Number(h, mjd, out double m)) return Guarded(mjd, () => FitsTime.FromMjd(m, sc));
            if (h.Get<string>(date) is { } text)
                return FitsTime.TryParse(text, sc, out var parsed) ? parsed : throw new FitsException($"{date} '{text}' is not a FITS datetime.");
            return null;
        }
        Observation = Global("DATE-OBS", "MJD-OBS");
        Begin = Global("DATE-BEG", "MJD-BEG");
        Average = Global("DATE-AVG", "MJD-AVG");
        End = Global("DATE-END", "MJD-END");
        Exposure = Number(h, "XPOSURE", out double xp) ? xp * globalUnit : null;
        ElapsedTime = Number(h, "TELAPSE", out double te) ? te * globalUnit : null;
        Start = Number(h, "TSTART", out double t0) && ReferenceTime is not null ? Guarded("TSTART", () => ToGlobalTime(t0)) : null;
        Stop = Number(h, "TSTOP", out double t1) && ReferenceTime is not null ? Guarded("TSTOP", () => ToGlobalTime(t1)) : null;
        JulianEpoch = Number(h, "JEPOCH", out double je) ? Guarded("JEPOCH", () => FromJulianEpoch(je)) : null;
        BesselianEpoch = Number(h, "BEPOCH", out double be) ? Guarded("BEPOCH", () => FromBesselianEpoch(be)) : null;
    }

    private readonly double _globalSecondsPerUnit;

    private FitsTime ToGlobalTime(double value) => ReferenceTime!.Value.AddSeconds((TimeOffset + value) * _globalSecondsPerUnit);

    /// <summary>A numeric keyword; a keyword that is present but not a number is an error.</summary>
    private static bool Number(FitsHeader h, string key, out double value)
    {
        value = 0.0;
        if (h.Find(key) is not { } card) return false;
        switch (card.Value)
        {
            case long l: value = l; return true;
            case double d: value = d; return true;
            case System.Numerics.BigInteger b: value = (double)b; return true;
            default: throw new FitsException($"{key} must be a number.");
        }
    }

    /// <summary>Runs a computation on a keyword value, turning range errors into a <see cref="FitsException"/> that names the keyword.</summary>
    private static FitsTime Guarded(string keyword, Func<FitsTime> compute)
    {
        try
        {
            return compute();
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or OverflowException or NotSupportedException)
        {
            throw new FitsException($"The value of {keyword} is out of range for a time: {e.Message}", e);
        }
    }

    private static TimeFrame Build(FitsHeader h, string scaleName, string unit, string? referencePosition, string? referenceDirection,
        double? systematicError, double? randomError, bool imageAxis)
    {
        try
        {
            return new TimeFrame(h, scaleName, unit, referencePosition, referenceDirection, systematicError, randomError, imageAxis);
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or OverflowException or NotSupportedException)
        {
            throw new FitsException($"A time keyword is out of range: {e.Message}", e);
        }
    }

    private static bool ReadReferenceIsExplicit(FitsHeader h) =>
        new[] { "MJDREF", "MJDREFI", "MJDREFF", "JDREF", "JDREFI", "JDREFF", "DATEREF" }.Any(h.Contains);

    /// <summary>The value of TIMESYS (or of the axis override), for example <c>UTC</c> or <c>TT(TAI)</c>.</summary>
    public string TimeSystem { get; }

    /// <summary>The recognised time scale, or null for a local or unknown scale.</summary>
    public TimeScale? Scale { get; }

    /// <summary>The time unit (TIMEUNIT, CUNITia or TCUNIn), <c>s</c> by default.</summary>
    public string Unit { get; }

    /// <summary>Length of the time unit in SI seconds (evaluated at the reference time for the tropical and Besselian years).</summary>
    public double SecondsPerUnit { get; }

    /// <summary>The reference time all times are relative to (MJDREF, JDREF or DATEREF; MJD 0 when none is given); null for a local scale.</summary>
    public FitsTime? ReferenceTime { get; }

    /// <summary>Whether the header gives a reference time (otherwise MJD 0 is assumed).</summary>
    public bool HasExplicitReference { get; }

    /// <summary>TIMEOFFS: the offset added to the reference time, in units of TIMEUNIT. It is for tables: images ignore it, as the standard requires.</summary>
    public double TimeOffset { get; }

    /// <summary>TREFPOS (or TRPOSn): where the times are valid, <c>TOPOCENTER</c> by default.</summary>
    public string ReferencePosition { get; }

    /// <summary>TREFDIR (or TRDIRn): the keywords holding the reference direction, if any.</summary>
    public string? ReferenceDirection { get; }

    /// <summary>PLEPHEM: the Solar System ephemeris, <c>DE405</c> by default.</summary>
    public string Ephemeris { get; }

    /// <summary>TIMEDEL: the time resolution in units of <see cref="Unit"/>.</summary>
    public double? TimeResolution { get; }

    /// <summary>TIMEPIXR: where in the pixel the time stamp is, from 0 to 1 (0.5 by default).</summary>
    public double TimePixelReference { get; }

    /// <summary>TIMSYER (or CSYERia): absolute time error.</summary>
    public double SystematicError { get; }

    /// <summary>TIMRDER (or CRDERia): relative time error.</summary>
    public double RandomError { get; }

    /// <summary>DATE-OBS or MJD-OBS.</summary>
    public FitsTime? Observation { get; }

    /// <summary>DATE-BEG or MJD-BEG.</summary>
    public FitsTime? Begin { get; }

    /// <summary>DATE-AVG or MJD-AVG.</summary>
    public FitsTime? Average { get; }

    /// <summary>DATE-END or MJD-END.</summary>
    public FitsTime? End { get; }

    /// <summary>TSTART as an instant.</summary>
    public FitsTime? Start { get; }

    /// <summary>TSTOP as an instant.</summary>
    public FitsTime? Stop { get; }

    /// <summary>XPOSURE in seconds.</summary>
    public double? Exposure { get; }

    /// <summary>TELAPSE in seconds.</summary>
    public double? ElapsedTime { get; }

    /// <summary>JEPOCH as an instant on the TDB scale.</summary>
    public FitsTime? JulianEpoch { get; }

    /// <summary>BEPOCH as an instant on the TT scale (ET).</summary>
    public FitsTime? BesselianEpoch { get; }

    // ---- Construction --------------------------------------------------------------------------------------------

    /// <summary>The global time frame of a header (TIMESYS, TIMEUNIT, TREFPOS, ...).</summary>
    public static TimeFrame FromHeader(FitsHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return Build(header, header.Get<string>("TIMESYS")?.Trim() ?? "UTC", header.Get<string>("TIMEUNIT")?.Trim() ?? "s",
            header.Get<string>("TREFPOS")?.Trim(), header.Get<string>("TREFDIR")?.Trim(), null, null, imageAxis: false);
    }

    /// <summary>
    /// The frame of a time axis of an image or random groups: CTYPEia may name the time scale, CUNITia overrides the unit, CSYERia and
    /// CRDERia the errors; the global keywords supply the rest.
    /// </summary>
    public static TimeFrame ForAxis(FitsHeader header, int axis, char alternate = ' ')
    {
        ArgumentNullException.ThrowIfNull(header);
        string a = alternate == ' ' ? "" : alternate.ToString();
        string ctype = header.Get<string>($"CTYPE{axis}{a}")?.Trim() ?? "TIME";
        if (!IsTimeCoordinateType(ctype)) throw new ArgumentException($"CTYPE{axis}{a} '{ctype}' is not a time coordinate type.", nameof(axis));
        string scale = ctype.Equals("TIME", StringComparison.OrdinalIgnoreCase) ? header.Get<string>("TIMESYS")?.Trim() ?? "UTC" : ctype;
        return Build(header, scale, header.Get<string>($"CUNIT{axis}{a}")?.Trim() ?? header.Get<string>("TIMEUNIT")?.Trim() ?? "s",
            header.Get<string>("TREFPOS")?.Trim(), header.Get<string>("TREFDIR")?.Trim(),
            Number(header, $"CSYER{axis}{a}", out double sy) ? sy : null, Number(header, $"CRDER{axis}{a}", out double rd) ? rd : null, imageAxis: true);
    }

    /// <summary>The frame of a binary table column: TCTYPn, TCUNIn, TRPOSn and TRDIRn override the global keywords.</summary>
    public static TimeFrame ForColumn(FitsHeader header, int column)
    {
        ArgumentNullException.ThrowIfNull(header);
        string? ctype = header.Get<string>("TCTYP" + column)?.Trim();
        string scale = ctype is null || ctype.Equals("TIME", StringComparison.OrdinalIgnoreCase) ? header.Get<string>("TIMESYS")?.Trim() ?? "UTC" : ctype;
        return Build(header, scale, header.Get<string>("TCUNI" + column)?.Trim() ?? header.Get<string>("TIMEUNIT")?.Trim() ?? "s",
            header.Get<string>("TRPOS" + column)?.Trim() ?? header.Get<string>("TREFPOS")?.Trim(),
            header.Get<string>("TRDIR" + column)?.Trim() ?? header.Get<string>("TREFDIR")?.Trim(),
            Number(header, "TCSYE" + column, out double sy) ? sy : null, Number(header, "TCRDE" + column, out double rd) ? rd : null, imageAxis: false);
    }

    /// <summary>Whether a CTYPE names a time axis: <c>TIME</c> or a time scale of Table 30 of the standard.</summary>
    public static bool IsTimeCoordinateType(string? ctype)
    {
        if (string.IsNullOrWhiteSpace(ctype)) return false;
        string c = ctype.Trim();
        return c.Equals("TIME", StringComparison.OrdinalIgnoreCase) || (TimeScales.Parse(c) is not null && !c.Equals("UT", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Reference time --------------------------------------------------------------------------------------------

    private static FitsTime ReadReference(FitsHeader h, TimeScale scale)
    {
        bool hasM = h.Contains("MJDREF") || h.Contains("MJDREFI") || h.Contains("MJDREFF");
        if (hasM)
        {
            bool parts = Number(h, "MJDREFI", out double mi) & Number(h, "MJDREFF", out double mf);
            if (parts) return FitsTime.FromMjdParts(mi, mf, scale);
            if (Number(h, "MJDREF", out double m)) return FitsTime.FromMjd(m, scale);
            return FitsTime.FromMjdParts(Number(h, "MJDREFI", out double i) ? i : 0.0, Number(h, "MJDREFF", out double f) ? f : 0.0, scale);
        }
        if (h.Contains("JDREF") || h.Contains("JDREFI") || h.Contains("JDREFF"))
        {
            bool parts = Number(h, "JDREFI", out double ji) & Number(h, "JDREFF", out double jf);
            if (parts) return FitsTime.FromJdParts(ji, jf, scale);
            if (Number(h, "JDREF", out double j)) return FitsTime.FromJd(j, scale);
            return FitsTime.FromJdParts(Number(h, "JDREFI", out double i) ? i : 0.0, Number(h, "JDREFF", out double f) ? f : 0.0, scale);
        }
        if (h.Get<string>("DATEREF") is { } date)
        {
            if (!FitsTime.TryParse(date, scale, out var t)) throw new FitsException($"DATEREF '{date}' is not a FITS datetime.");
            return t;
        }
        return FitsTime.FromMjdParts(0.0, 0.0, scale);
    }

    // ---- Units ------------------------------------------------------------------------------------------------------

    private static double UnitSeconds(string unit, FitsTime? reference)
    {
        const double day = 86400.0;
        switch (unit.ToLowerInvariant())
        {
            case "s": return 1.0;
            case "min": return 60.0;
            case "h": return 3600.0;
            case "d": return day;
            case "a" or "yr": return 365.25 * day;
            case "cy": return 36525.0 * day;
            case "ta":
            {
                double t = reference is { } r ? (r.Jd - 2451545.0) / 36525.0 : 0.0; // Julian centuries since J2000
                return (365.24219040211236 - 0.00000615251349 * t - 6.0921e-10 * t * t + 2.6525e-10 * t * t * t) * day;
            }
            case "ba":
            {
                double t = reference is { } r ? (r.Jd - 2415020.0) / 36525.0 : 0.0; // Julian centuries since J1900
                return (365.2421987817 - 0.00000785423 * t) * day;
            }
            default:
                throw new FitsException($"'{unit}' is not a time unit of the standard (s, min, h, d, a, yr, cy, ta, Ba).");
        }
    }

    // ---- Values -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The instant of a time value of the data: the reference time, plus TIMEOFFS (in the global TIMEUNIT), plus <paramref name="value"/> in
    /// the unit of this frame (the unit of the axis or column when it has one).
    /// </summary>
    /// <exception cref="InvalidOperationException">The scale is local or unknown, so no reference time applies.</exception>
    public FitsTime ToTime(double value)
    {
        if (ReferenceTime is not { } reference)
            throw new InvalidOperationException($"The time scale '{TimeSystem}' is not a recognised one: its values have no reference time.");
        return reference.AddSeconds(TimeOffset * _globalSecondsPerUnit + value * SecondsPerUnit);
    }

    /// <summary>The inverse of <see cref="ToTime"/>: the time value of the data for an instant on the same scale.</summary>
    public double ToValue(FitsTime time)
    {
        if (ReferenceTime is not { } reference)
            throw new InvalidOperationException($"The time scale '{TimeSystem}' is not a recognised one: its values have no reference time.");
        return (time.SecondsSince(reference) - TimeOffset * _globalSecondsPerUnit) / SecondsPerUnit;
    }

    /// <summary>Instant of a Julian epoch such as 2000.0 (TDB).</summary>
    public static FitsTime FromJulianEpoch(double epoch) => FitsTime.FromJd(2451545.0 + (epoch - 2000.0) * 365.25, TimeScale.TDB);

    /// <summary>Instant of a Besselian epoch such as 1950.0 (ET, that is TT).</summary>
    public static FitsTime FromBesselianEpoch(double epoch) => FitsTime.FromJd(2415020.31352 + (epoch - 1900.0) * 365.242198781, TimeScale.TT);
}
