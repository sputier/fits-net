namespace Eleris.FitsNet.Examples;

public static class TimeExamples
{
    public static void Instants()
    {
        #region Example:Time.Instants
        // Parse reads a FITS datetime; the time scale is part of the instant.
        FitsTime utc = FitsTime.Parse("2016-12-31T23:59:60.5", TimeScale.UTC);   // a leap second
        Console.WriteLine(utc.ToIsoString(3));                                    // 2016-12-31T23:59:60.500
        Console.WriteLine($"MJD {utc.Mjd:F6}, JD {utc.Jd:F6}");

        // Conversions between the scales that can be computed without outside data.
        FitsTime tai = utc.To(TimeScale.TAI);
        FitsTime tt = utc.To(TimeScale.TT);
        Console.WriteLine($"TAI {tai.ToIsoString(1)}  TT {tt.ToIsoString(1)}");

        // Arithmetic is in SI seconds, leap seconds included.
        FitsTime later = utc.AddSeconds(1.0);
        Console.WriteLine($"{later.ToIsoString(1)}, {later.SecondsSince(utc)} s later");

        // Build an instant from a date, a Modified Julian Date or a Julian Date.
        FitsTime j2000 = FitsTime.FromDateTime(2000, 1, 1, 12, 0, 0, TimeScale.TT);
        Console.WriteLine(j2000.Jd == FitsTime.FromJd(2451545.0, TimeScale.TT).Jd);           // True
        #endregion
    }

    public static void Invalid()
    {
        #region Example:Time.Invalid
        // TryParse does not throw. Time zones are not part of FITS datetimes, so a trailing Z is refused.
        Console.WriteLine(FitsTime.TryParse("2020-01-01T00:00:00", TimeScale.UTC, out _));    // True
        Console.WriteLine(FitsTime.TryParse("2020-01-01T00:00:00Z", TimeScale.UTC, out _));   // False

        // Conversions that need data the library does not carry (Earth orientation, a time ephemeris) are refused.
        try
        {
            FitsTime.Parse("2020-01-01", TimeScale.UTC).To(TimeScale.UT1);
        }
        catch (NotSupportedException e)
        {
            Console.WriteLine(e.Message);
        }
        #endregion
    }

    public static void Frame()
    {
        #region Example:Time.Frame
        // The keywords of section 9 of the standard describe how the time values of a file are to be read.
        var header = new FitsHeader()
            .Set("TIMESYS", "TT")
            .Set("MJDREF", 55197.0)          // the reference time, as a Modified Julian Date
            .Set("TIMEUNIT", "d")
            .Set("TSTART", 100.5)
            .Set("TSTOP", 101.5)
            .Set("TREFPOS", "GEOCENTER");

        TimeFrame frame = TimeFrame.FromHeader(header);
        Console.WriteLine($"{frame.Scale}, {frame.Unit}, {frame.ReferencePosition}");
        Console.WriteLine($"{frame.Start!.Value.ToIsoString(0)} to {frame.Stop!.Value.ToIsoString(0)}");

        // Turn the time values of the data into instants, and back.
        FitsTime instant = frame.ToTime(100.75);
        double value = frame.ToValue(instant);                    // 100.75
        Console.WriteLine($"{instant.ToIsoString(0)} {value}");
        #endregion
    }

    public static void Columns()
    {
        #region Example:Time.Columns
        // A time axis of an image, or a time column of a table, can override the global keywords.
        var header = new FitsHeader()
            .Set("TIMESYS", "UTC").Set("MJDREF", 58000.0)
            .Set("TTYPE2", "TIME").Set("TCUNI2", "s");             // column 2 holds seconds since the reference time

        TimeFrame column = TimeFrame.ForColumn(header, 2);
        Console.WriteLine(column.ToTime(86400.0).ToIsoString(0));   // 2017-09-05T00:00:00

        // A time axis of an image: CTYPEi names the scale (or says TIME), CUNITi the unit.
        var image = new FitsHeader().Set("CTYPE3", "TIME").Set("CUNIT3", "min").Set("MJDREF", 58000.0);
        TimeFrame axis = TimeFrame.ForAxis(image, 3);
        Console.WriteLine(axis.SecondsPerUnit);                     // 60
        #endregion
    }
}
