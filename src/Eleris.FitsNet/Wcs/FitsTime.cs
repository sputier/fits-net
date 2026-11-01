using System.Globalization;
using System.Text;

namespace Eleris.FitsNet.Wcs;

/// <summary>The time scales of Table 30 of the standard (synonyms are folded: TDT and ET into TT, IAT into TAI, GMT into UTC).</summary>
public enum TimeScale
{
    /// <summary>International Atomic Time (also <c>IAT</c>).</summary>
    TAI,

    /// <summary>Terrestrial Time (also <c>TDT</c> and <c>ET</c>).</summary>
    TT,

    /// <summary>Coordinated Universal Time (also <c>GMT</c>); leap seconds are supported from 1972.</summary>
    UTC,

    /// <summary>Universal Time; it cannot be converted without Earth orientation data.</summary>
    UT1,

    /// <summary>GPS time, TAI - 19 s.</summary>
    GPS,

    /// <summary>Geocentric Coordinate Time.</summary>
    TCG,

    /// <summary>Barycentric Coordinate Time.</summary>
    TCB,

    /// <summary>Barycentric Dynamical Time.</summary>
    TDB,

    /// <summary>A local clock (simulations, free-running clocks): no conversion exists.</summary>
    Local,
}

/// <summary>Names, synonyms and conversions of time scales.</summary>
public static class TimeScales
{
    /// <summary>The scale named by a TIMESYS-like value, ignoring a realization in parentheses such as <c>TT(TAI)</c>; null if unknown.</summary>
    /// <param name="name">The name, for example <c>UTC</c> or <c>TT(TAI)</c>.</param>
    public static TimeScale? Parse(string? name)
    {
        if (name is null) return null;
        string n = name.Trim();
        int paren = n.IndexOf('(');
        if (paren >= 0) n = n[..paren];
        return n.ToUpperInvariant() switch
        {
            "TAI" or "IAT" => TimeScale.TAI,
            "TT" or "TDT" or "ET" => TimeScale.TT,
            "UTC" or "GMT" => TimeScale.UTC,
            "UT1" or "UT" => TimeScale.UT1,
            "GPS" => TimeScale.GPS,
            "TCG" => TimeScale.TCG,
            "TCB" => TimeScale.TCB,
            "TDB" => TimeScale.TDB,
            "LOCAL" => TimeScale.Local,
            _ => null,
        };
    }

    internal const double Lg = 6.969290134e-10;
    internal const double Lb = 1.550519768e-8;
    internal const double Tdb0 = -6.55e-5;
    internal const double Mjd0 = 2443144.5003725 - 2400000.5; // 1977-01-01T00:00:32.184 TT

    // TAI - UTC since 1972: the first day it is in force (MJD) and its value in seconds.
    private static readonly (long Mjd, int Seconds)[] Leaps =
    [
        (41317, 10), (41499, 11), (41683, 12), (42048, 13), (42413, 14), (42778, 15), (43144, 16), (43509, 17), (43874, 18), (44239, 19),
        (44786, 20), (45151, 21), (45516, 22), (46247, 23), (47161, 24), (47892, 25), (48257, 26), (48804, 27), (49169, 28), (49534, 29),
        (50083, 30), (50630, 31), (51179, 32), (53736, 33), (54832, 34), (56109, 35), (57204, 36), (57754, 37),
    ];

    /// <summary>First day of the table, 1972-01-01.</summary>
    internal const long FirstUtcDay = 41317;

    /// <summary>
    /// TAI - UTC in seconds in force during a UTC day. Before 1972 the offset is not an integer number of seconds: the value of 1972 is used,
    /// which is right for arithmetic on UTC itself; <see cref="RequireOfficialUtc"/> guards the conversions to other scales.
    /// </summary>
    internal static int TaiMinusUtc(long utcDay)
    {
        int value = Leaps[0].Seconds;
        foreach (var (mjd, seconds) in Leaps)
        {
            if (utcDay < mjd) break;
            value = seconds;
        }
        return value;
    }

    /// <summary>UTC can be converted to or from another scale from 1972 only (the rubber-second offsets of earlier years are not carried).</summary>
    internal static void RequireOfficialUtc(long utcDay)
    {
        if (utcDay < FirstUtcDay)
            throw new NotSupportedException("Converting UTC before 1972-01-01 (when leap seconds began) is not supported: it needs the rubber-second offsets of Rots et al. (2015).");
    }

    /// <summary>Length of a UTC day in seconds: 86401 when a leap second ends it.</summary>
    internal static int UtcDayLength(long utcDay) => utcDay < FirstUtcDay ? 86400 : 86400 + (TaiMinusUtc(utcDay + 1) - TaiMinusUtc(utcDay));

    /// <summary>Whether <see cref="FitsTime.To"/> can convert between two scales.</summary>
    /// <param name="from">The scale to convert from.</param>
    /// <param name="to">The scale to convert to.</param>
    public static bool CanConvert(TimeScale from, TimeScale to)
    {
        if (from == to) return true;
        static int Group(TimeScale s) => s switch
        {
            TimeScale.TAI or TimeScale.TT or TimeScale.UTC or TimeScale.GPS or TimeScale.TCG => 1,
            TimeScale.TCB or TimeScale.TDB => 2,
            _ => 0,
        };
        return Group(from) != 0 && Group(from) == Group(to);
    }
}

/// <summary>
/// An instant on a time scale (standard section 9): a day number and the seconds into the day, which keeps nanoseconds over any
/// span of years. ISO-8601 datetime strings, Julian and Modified Julian Dates and conversions between the linear scales are supported.
/// </summary>
public readonly struct FitsTime : IEquatable<FitsTime>, IComparable<FitsTime>
{
    private const double DayLength = 86400.0;

    /// <summary>The instants that can be written as FITS datetimes: from -99999-01-01 to +99999-12-31.</summary>
    private static readonly long MinDay = DaysFromCivil(-99999, 1, 1) + 40587, MaxDay = DaysFromCivil(99999, 12, 31) + 40587;

    private static long CheckDay(long day)
    {
        if (day < MinDay || day > MaxDay)
            throw new ArgumentOutOfRangeException(nameof(day), "The date is outside the years -99999 to +99999 that FITS can express.");
        return day;
    }

    internal FitsTime(TimeScale scale, long day, double secondOfDay)
    {
        Scale = scale;
        Day = day;
        SecondOfDay = secondOfDay;
    }

    /// <summary>The time scale the instant is expressed in.</summary>
    public TimeScale Scale { get; }

    /// <summary>Modified Julian Day number (the instant is during this day of its scale).</summary>
    public long Day { get; }

    /// <summary>Seconds elapsed since the start of <see cref="Day"/> (up to 86400 plus the leap second in UTC).</summary>
    public double SecondOfDay { get; }

    private double Length => Scale == TimeScale.UTC && Day >= TimeScales.FirstUtcDay ? TimeScales.UtcDayLength(Day) : DayLength;

    /// <summary>The instant as a Modified Julian Date (JD - 2400000.5). On a UTC leap day the fraction of the day is scaled, as usual.</summary>
    public double Mjd => Day + SecondOfDay / Length;

    /// <summary>The instant as a Julian Date.</summary>
    public double Jd => Day + 2400000.5 + SecondOfDay / Length;

    /// <summary>Creates an instant from a Modified Julian Date.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The date is not a number or lies outside the years -99999 to +99999.</exception>
    /// <param name="mjd">The Modified Julian Date (JD - 2400000.5).</param>
    /// <param name="scale">The time scale the date is expressed in.</param>
    public static FitsTime FromMjd(double mjd, TimeScale scale = TimeScale.UTC)
    {
        if (!double.IsFinite(mjd)) throw new ArgumentOutOfRangeException(nameof(mjd), "The date is not a number.");
        return FromMjdParts(Math.Floor(mjd), mjd - Math.Floor(mjd), scale);
    }

    /// <summary>
    /// Creates an instant from the integer and fractional parts of a Modified Julian Date (the fraction may be any size). The separate name
    /// avoids confusing it with <see cref="FromMjd"/> and a time scale.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A part is not a number or the date lies outside the years -99999 to +99999.</exception>
    /// <param name="integer">The integer part of the Modified Julian Date.</param>
    /// <param name="fraction">The fractional part, which may exceed one day.</param>
    /// <param name="scale">The time scale the date is expressed in.</param>
    public static FitsTime FromMjdParts(double integer, double fraction, TimeScale scale = TimeScale.UTC)
    {
        if (!double.IsFinite(integer) || !double.IsFinite(fraction) || Math.Abs(integer) > 1e9 || Math.Abs(fraction) > 1e9)
            throw new ArgumentOutOfRangeException(nameof(integer), "The date is not a representable number.");
        double whole = Math.Floor(integer);
        fraction += integer - whole;
        double extra = Math.Floor(fraction);
        fraction -= extra;
        long day = CheckDay((long)whole + (long)extra);
        double length = scale == TimeScale.UTC && day >= TimeScales.FirstUtcDay ? TimeScales.UtcDayLength(day) : DayLength;
        return new FitsTime(scale, day, Math.Min(fraction * length, Math.BitDecrement(length)));
    }

    /// <summary>Creates an instant from a Julian Date (its precision is that of a double: about 40 microseconds today).</summary>
    /// <param name="jd">The Julian Date.</param>
    /// <param name="scale">The time scale the date is expressed in.</param>
    public static FitsTime FromJd(double jd, TimeScale scale = TimeScale.UTC) => FromMjd(jd - 2400000.5, scale);

    /// <summary>Creates an instant from the two parts of a Julian Date (for example its integer and fractional parts).</summary>
    /// <param name="part1">The first part of the Julian Date, for example its integer part.</param>
    /// <param name="part2">The second part of the Julian Date, for example its fraction; the date is the sum of the two parts.</param>
    /// <param name="scale">The time scale the date is expressed in.</param>
    public static FitsTime FromJdParts(double part1, double part2, TimeScale scale = TimeScale.UTC)
    {
        double mjd1 = part1 - 2400000.5;
        return FromMjdParts(Math.Floor(mjd1), mjd1 - Math.Floor(mjd1) + part2, scale);
    }

    /// <summary>Creates an instant from a calendar date and time (proleptic Gregorian calendar, year zero exists as in ISO-8601).</summary>
    /// <param name="year">The year, from -99999 to 99999 (year 0 is 1 BCE).</param>
    /// <param name="month">The month, 1 to 12.</param>
    /// <param name="day">The day of the month, starting at 1.</param>
    /// <param name="hour">The hour, 0 to 23.</param>
    /// <param name="minute">The minute, 0 to 59.</param>
    /// <param name="second">The seconds with their decimals, at least 0 and less than 60; 60 is accepted only for a leap second.</param>
    /// <param name="scale">The time scale the date and time are expressed in.</param>
    public static FitsTime FromDateTime(long year, int month, int day, int hour = 0, int minute = 0, double second = 0.0, TimeScale scale = TimeScale.UTC)
    {
        if (year is < -99999 or > 99999) throw new ArgumentOutOfRangeException(nameof(year), "FITS datetimes cover the years -99999 to +99999.");
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        if (day < 1 || day > DaysInMonth(year, month)) throw new ArgumentOutOfRangeException(nameof(day));
        if (hour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        if (minute is < 0 or > 59) throw new ArgumentOutOfRangeException(nameof(minute));
        long mjd = DaysFromCivil(year, month, day) + 40587;
        double sod = hour * 3600.0 + minute * 60.0 + second;
        if (second < 60.0 && sod >= hour * 3600.0 + minute * 60.0 + 60.0) sod = Math.BitDecrement(hour * 3600.0 + minute * 60.0 + 60.0);   // a second that ends in a run of nines
        double length = scale == TimeScale.UTC && mjd >= TimeScales.FirstUtcDay ? TimeScales.UtcDayLength(mjd) : DayLength;
        // second 60 exists only as 23:59:60 at the end of a UTC day that has a leap second
        bool leapSecond = second >= 60.0 && scale == TimeScale.UTC && hour == 23 && minute == 59;
        if (!(second >= 0.0) || sod >= length || (second >= 60.0 && !leapSecond))
            throw new ArgumentOutOfRangeException(nameof(second), second >= 60.0 && second < 61.0 && scale == TimeScale.UTC
                ? "There is no leap second at the end of this day."
                : "The seconds are out of range.");
        return new FitsTime(scale, mjd, sod);
    }

    // ---- Calendar ---------------------------------------------------------------------------------------------------

    private static bool IsLeapYear(long y) => y % 4 == 0 && (y % 100 != 0 || y % 400 == 0);

    private static int DaysInMonth(long year, int month) => month switch
    {
        2 => IsLeapYear(year) ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    /// <summary>Days since 1970-01-01 of a proleptic Gregorian date (Howard Hinnant's algorithm).</summary>
    private static long DaysFromCivil(long y, int m, int d)
    {
        y -= m <= 2 ? 1 : 0;
        long era = (y >= 0 ? y : y - 399) / 400;
        long yoe = y - era * 400;
        long doy = (153 * (m + (m > 2 ? -3 : 9)) + 2) / 5 + d - 1;
        long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146097 + doe - 719468;
    }

    private static (long Year, int Month, int Day) CivilFromDays(long z)
    {
        z += 719468;
        long era = (z >= 0 ? z : z - 146096) / 146097;
        long doe = z - era * 146097;
        long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        long y = yoe + era * 400;
        long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        long mp = (5 * doy + 2) / 153;
        int d = (int)(doy - (153 * mp + 2) / 5 + 1);
        int m = (int)(mp < 10 ? mp + 3 : mp - 9);
        return (y + (m <= 2 ? 1 : 0), m, d);
    }

    /// <summary>The calendar date and time of day of the instant (second 60 on a leap-second day).</summary>
    public (long Year, int Month, int Day, int Hour, int Minute, double Second) ToDateTime()
    {
        var (y, m, d) = CivilFromDays(Day - 40587);
        double s = SecondOfDay;
        int hour = (int)Math.Min(Math.Floor(s / 3600.0), 23);
        s -= hour * 3600.0;
        int minute = (int)Math.Min(Math.Floor(s / 60.0), 59);
        s -= minute * 60.0;
        if (s < 0.0) s = 0.0;
        return (y, m, d, hour, minute, s);
    }

    // ---- ISO-8601 ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Parses a FITS datetime: <c>CCYY-MM-DD[Thh:mm:ss[.s...]]</c> or the signed five-digit year form <c>±CCCCC-MM-DD...</c>. Time zones
    /// are not allowed. Second 60 is accepted in UTC on a day that ends with a leap second.
    /// </summary>
    /// <exception cref="FormatException">The text is not a FITS datetime.</exception>
    /// <param name="text">The text: <c>CCYY-MM-DD[Thh:mm:ss[.s...]]</c> or the signed five-digit year form.</param>
    /// <param name="scale">The time scale the text is expressed in.</param>
    public static FitsTime Parse(string text, TimeScale scale = TimeScale.UTC)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!TryParse(text, scale, out var result, out string? error)) throw new FormatException(error);
        return result;
    }

    /// <summary>Like <see cref="Parse"/> but returns false instead of throwing.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="scale">The time scale the text is expressed in.</param>
    /// <param name="result">The instant when the text is valid.</param>
    public static bool TryParse(string? text, TimeScale scale, out FitsTime result) => TryParse(text, scale, out result, out _);

    private static bool TryParse(string? text, TimeScale scale, out FitsTime result, out string? error)
    {
        result = default;
        error = null;
        if (text is null) { error = "No text."; return false; }
        string s = text.Trim();
        int i = 0;
        int sign = 1;
        int yearDigits = 4;
        if (s.Length > 0 && s[0] is '+' or '-')
        {
            sign = s[0] == '-' ? -1 : 1;
            i = 1;
            yearDigits = 5;
        }
        bool Digits(int count, out long value)
        {
            value = 0;
            if (i + count > s.Length) return false;
            for (int k = 0; k < count; k++)
            {
                char c = s[i + k];
                if (c is < '0' or > '9') return false;
                value = value * 10 + (c - '0');
            }
            i += count;
            return true;
        }
        bool Expect(char c)
        {
            if (i < s.Length && s[i] == c) { i++; return true; }
            return false;
        }

        if (!Digits(yearDigits, out long year) || !Expect('-') || !Digits(2, out long month) || !Expect('-') || !Digits(2, out long day))
        {
            error = $"'{text}' is not a FITS datetime (CCYY-MM-DD[Thh:mm:ss[.s...]]).";
            return false;
        }
        year *= sign;
        long hour = 0, minute = 0;
        double second = 0.0;
        if (i < s.Length)
        {
            if (!Expect('T') || !Digits(2, out hour) || !Expect(':') || !Digits(2, out minute) || !Expect(':') || !Digits(2, out long whole))
            {
                error = $"'{text}' is not a FITS datetime (CCYY-MM-DD[Thh:mm:ss[.s...]]).";
                return false;
            }
            second = whole;
            if (Expect('.'))
            {
                int start = i;
                while (i < s.Length && s[i] is >= '0' and <= '9') i++;
                if (i == start) { error = $"'{text}': digits are expected after the decimal point."; return false; }
                // digits like .99999999999999999 round to a whole second: stay just below it
                second = Math.Min(second + double.Parse("0." + s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture), Math.BitDecrement(second + 1.0));
            }
            if (i != s.Length)
            {
                error = $"'{text}': trailing characters (time zones are not allowed in FITS).";
                return false;
            }
        }
        if (month is < 1 or > 12 || day < 1 || day > DaysInMonth(year, (int)month) || hour > 23 || minute > 59)
        {
            error = $"'{text}' is not a valid date or time.";
            return false;
        }
        try
        {
            result = FromDateTime(year, (int)month, (int)day, (int)hour, (int)minute, second, scale);
            return true;
        }
        catch (ArgumentOutOfRangeException e)
        {
            error = $"'{text}': {e.Message}";
            return false;
        }
    }

    /// <summary>Formats the instant as a FITS datetime with the given number of decimals of the seconds.</summary>
    /// <param name="decimals">The number of decimals of the seconds, 0 to 9.</param>
    public string ToIsoString(int decimals = 3)
    {
        if (decimals is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(decimals));
        long unit = 1;
        for (int k = 0; k < decimals; k++) unit *= 10;
        long scaled = (long)Math.Round(SecondOfDay * unit, MidpointRounding.AwayFromZero);   // exact in a long
        long length = (long)Length * unit;
        long day = Day;
        if (scaled >= length)
        {
            scaled -= length; // the rounding carried into the next day
            day++;
        }
        var (y, mo, d) = CivilFromDays(day - 40587);
        long seconds = scaled / unit, fraction = scaled % unit;
        long hour = Math.Min(seconds / 3600, 23);
        seconds -= hour * 3600;
        long minute = Math.Min(seconds / 60, 59);
        seconds -= minute * 60;
        var sb = new StringBuilder();
        if (y is < 0 or > 9999) sb.Append(y < 0 ? '-' : '+').Append(Math.Abs(y).ToString("D5", CultureInfo.InvariantCulture));
        else sb.Append(y.ToString("D4", CultureInfo.InvariantCulture));
        sb.Append('-').Append(mo.ToString("D2", CultureInfo.InvariantCulture)).Append('-').Append(d.ToString("D2", CultureInfo.InvariantCulture))
            .Append('T').Append(hour.ToString("D2", CultureInfo.InvariantCulture)).Append(':').Append(minute.ToString("D2", CultureInfo.InvariantCulture))
            .Append(':').Append(seconds.ToString("D2", CultureInfo.InvariantCulture));
        if (decimals > 0) sb.Append('.').Append(fraction.ToString("D" + decimals, CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>The instant and its time scale, for example <c>2020-01-01T00:00:00.000000 UTC</c>.</summary>
    public override string ToString() => $"{ToIsoString(6)} {Scale}";

    // ---- Arithmetic -------------------------------------------------------------------------------------------------

    /// <summary>Continuous count of SI seconds from MJD 0 on a uniform scale (UTC is counted as TAI), as a whole part and a fraction.</summary>
    private (long Whole, double Fraction) Continuous()
    {
        double whole = Math.Floor(SecondOfDay);
        long seconds = checked(Day * 86400 + (long)whole);
        if (Scale == TimeScale.UTC) seconds = checked(seconds + TimeScales.TaiMinusUtc(Day));
        return (seconds, SecondOfDay - whole);
    }

    private static FitsTime FromContinuous(TimeScale scale, long whole, double fraction)
    {
        // a fraction within a picosecond of a whole second is that second (decimal fractions such as .184 are not exact in binary)
        double nearest = Math.Round(fraction);
        if (Math.Abs(fraction - nearest) < 1e-12) fraction = nearest;
        if (fraction is < 0.0 or >= 1.0)
        {
            double carry = Math.Floor(fraction);
            whole = checked(whole + (long)carry);
            fraction -= carry;
            if (fraction >= 1.0)
            {
                whole++;
                fraction = 0.0;
            }
        }
        if (scale != TimeScale.UTC)
        {
            long day = CheckDay(FloorDiv(whole, 86400));
            return new FitsTime(scale, day, (whole - day * 86400) + fraction);
        }
        // before 1972 UTC is counted as a uniform scale offset by the 1972 value
        long firstSecond = TimeScales.FirstUtcDay * 86400 + TimeScales.TaiMinusUtc(TimeScales.FirstUtcDay);
        if (whole < firstSecond)
        {
            long early = CheckDay(FloorDiv(whole - TimeScales.TaiMinusUtc(TimeScales.FirstUtcDay), 86400));
            return new FitsTime(scale, early, (whole - TimeScales.TaiMinusUtc(TimeScales.FirstUtcDay) - early * 86400) + fraction);
        }
        // UTC: find the day such that  day * 86400 + (TAI - UTC)(day)  <=  whole  <  next day
        long guess = FloorDiv(whole - 37, 86400);
        for (long d = guess - 1; d <= guess + 2; d++)
        {
            if (d < TimeScales.FirstUtcDay) continue;
            long start = d * 86400 + TimeScales.TaiMinusUtc(d);
            long end = (d + 1) * 86400 + TimeScales.TaiMinusUtc(d + 1);
            if (whole >= start && whole < end) return new FitsTime(scale, CheckDay(d), (whole - start) + fraction);
        }
        throw new ArgumentOutOfRangeException(nameof(whole), "The date is outside the years -99999 to +99999 that FITS can express.");
    }

    private static long FloorDiv(long a, long b) => a / b - (a % b != 0 && (a < 0) != (b < 0) ? 1 : 0);

    /// <summary>The instant <paramref name="seconds"/> SI seconds later (earlier if negative), on the same scale.</summary>
    /// <param name="seconds">The duration in SI seconds; a negative value goes back in time.</param>
    public FitsTime AddSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || Math.Abs(seconds) > 4e15) throw new ArgumentOutOfRangeException(nameof(seconds), "The duration is not a representable number of seconds.");
        var (whole, fraction) = Continuous();
        double wholeSeconds = Math.Floor(seconds);
        return FromContinuous(Scale, checked(whole + (long)wholeSeconds), fraction + (seconds - wholeSeconds));
    }

    /// <summary>Orders two instants of the same time scale.</summary>
    /// <exception cref="ArgumentException">The instants are on different scales: convert one first.</exception>
    /// <param name="other">The instant to compare with, on the same time scale.</param>
    public int CompareTo(FitsTime other)
    {
        double delta = SecondsSince(other);
        return delta < 0.0 ? -1 : delta > 0.0 ? 1 : 0;
    }

    /// <summary>SI seconds from <paramref name="origin"/> to this instant; both must be on the same scale.</summary>
    /// <exception cref="ArgumentException">The instants are on different scales.</exception>
    /// <param name="origin">The instant to measure from, on the same time scale.</param>
    public double SecondsSince(FitsTime origin)
    {
        if (origin.Scale != Scale) throw new ArgumentException("The two instants are on different time scales; convert one first.", nameof(origin));
        var (w1, f1) = Continuous();
        var (w2, f2) = origin.Continuous();
        return (double)(w1 - w2) + (f1 - f2);
    }

    // ---- Time scale conversion ------------------------------------------------------------------------------------

    /// <summary>
    /// Converts to another time scale. TAI, TT, UTC (from 1972), GPS and TCG convert among each other (TCG with the constant rate
    /// L_G of the standard), TDB and TCB convert between them (L_B and TDB_0 of the standard). Anything else, such as UT1 or TT to TDB,
    /// needs external data and throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">The conversion needs data that is not carried, or involves UTC before 1972.</exception>
    /// <param name="target">The time scale to convert to.</param>
    public FitsTime To(TimeScale target)
    {
        if (target == Scale) return this;
        if (Scale == TimeScale.UTC && TimeScales.CanConvert(Scale, target)) TimeScales.RequireOfficialUtc(Day);
        var result = ConvertCore(target);
        if (result.Scale == TimeScale.UTC) TimeScales.RequireOfficialUtc(result.Day);
        return result;
    }

    private FitsTime ConvertCore(TimeScale target)
    {
        if (!TimeScales.CanConvert(Scale, target))
            throw new NotSupportedException($"Converting {Scale} to {target} needs data (Earth orientation or a time ephemeris) that this library does not carry.");
        if (Scale is TimeScale.TCB or TimeScale.TDB) return ToBarycentric(target);

        // everything goes through TT on the way
        var (whole, fraction) = Continuous();
        // offsets from TAI, in seconds: TT = TAI + 32.184 ; GPS = TAI - 19
        (long Whole, double Fraction) tt = Scale switch
        {
            TimeScale.TAI or TimeScale.UTC => (whole + 32, fraction + 0.184),
            TimeScale.GPS => (whole + 19 + 32, fraction + 0.184),
            TimeScale.TT => (whole, fraction),
            _ => TcgToTt(whole, fraction),
        };
        long w = tt.Whole;
        double f = tt.Fraction;
        return target switch
        {
            TimeScale.TT => FromContinuous(target, w, f),
            TimeScale.TAI or TimeScale.UTC => FromContinuous(target, w - 32, f - 0.184),
            TimeScale.GPS => FromContinuous(target, w - 32 - 19, f - 0.184),
            _ => TtToTcg(w, f, target),
        };
    }

    private static double SecondsFromT0(long whole, double fraction)
    {
        // seconds since JD 2443144.5003725 TT, which is MJD 43144.0003725 : 32.184 s after 1977-01-01T00:00:00 TAI
        const long t0Whole = 43144L * 86400 + 32;
        const double t0Fraction = 0.184;
        return (double)(whole - t0Whole) + (fraction - t0Fraction);
    }

    private static FitsTime TtToTcg(long whole, double fraction, TimeScale target)
    {
        // TCG - TT = L_G / (1 - L_G) (TT - T0)
        double delta = SecondsFromT0(whole, fraction) * (TimeScales.Lg / (1.0 - TimeScales.Lg));
        double whole2 = Math.Floor(delta);
        return FromContinuous(target, checked(whole + (long)whole2), fraction + (delta - whole2));
    }

    private static (long, double) TcgToTt(long whole, double fraction)
    {
        // TT = TCG - L_G (TCG - T0)
        double delta = -SecondsFromT0(whole, fraction) * TimeScales.Lg;
        double whole2 = Math.Floor(delta);
        return (checked(whole + (long)whole2), fraction + (delta - whole2));
    }

    private FitsTime ToBarycentric(TimeScale target)
    {
        var (whole, fraction) = Continuous();
        double offset = SecondsFromT0(whole, fraction);
        double delta;
        if (Scale == TimeScale.TCB)
        {
            // TDB = TCB - L_B (TCB - T0) + TDB_0
            delta = -offset * TimeScales.Lb + TimeScales.Tdb0;
        }
        else
        {
            // the inverse of the above, solved exactly: TCB - TDB = (L_B (TDB - T0) - TDB_0) / (1 - L_B)
            delta = (TimeScales.Lb * offset - TimeScales.Tdb0) / (1.0 - TimeScales.Lb);
        }
        double whole2 = Math.Floor(delta);
        return FromContinuous(target, checked(whole + (long)whole2), fraction + (delta - whole2));
    }

    // ---- Equality ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether two instants are the same representation: same time scale, same day and same second of the day. Two instants on different
    /// scales are not equal even when they are the same moment; compare <see cref="SecondsSince"/> after converting one with <see cref="To"/>.
    /// </summary>
    /// <param name="other">The instant to compare with.</param>
    public bool Equals(FitsTime other) => Scale == other.Scale && Day == other.Day && SecondOfDay.Equals(other.SecondOfDay);

    /// <summary>Whether <paramref name="obj"/> is an instant equal to this one (see <see cref="Equals(FitsTime)"/>).</summary>
    /// <param name="obj">The object to compare with.</param>
    public override bool Equals(object? obj) => obj is FitsTime t && Equals(t);

    /// <summary>A hash code consistent with <see cref="Equals(FitsTime)"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(Scale, Day, SecondOfDay);

    /// <summary>Equality of two instants.</summary>
    /// <param name="a">The first instant.</param>
    /// <param name="b">The second instant.</param>
    public static bool operator ==(FitsTime a, FitsTime b) => a.Equals(b);

    /// <summary>Inequality of two instants.</summary>
    /// <param name="a">The first instant.</param>
    /// <param name="b">The second instant.</param>
    public static bool operator !=(FitsTime a, FitsTime b) => !a.Equals(b);
}
