using System.Globalization;

namespace FitsNet.Wcs;

/// <summary>
/// The world coordinate system of an HDU (standard section 8): the link between pixel coordinates and world coordinates, built from
/// the <c>CRPIXj</c>, <c>CRVALi</c>, <c>CDELTi</c>, <c>CTYPEi</c>, <c>CUNITi</c>, <c>PCi_j</c> / <c>CDi_j</c> / <c>CROTAi</c>,
/// <c>PVi_m</c>, <c>LONPOLE</c>, <c>LATPOLE</c> keywords of one coordinate description (primary or alternate A to Z).
/// Pixel coordinates follow the FITS convention: the centre of the first pixel is 1.0. Celestial coordinates are in degrees,
/// the other axes in the unit of their <c>CUNITi</c>.
/// </summary>
public sealed class WorldCoordinateSystem
{
    private readonly double[] _crpix;
    private readonly double[] _crval;
    private readonly double[,] _matrix;   // intermediate world coordinates = matrix * (pixel - crpix)
    private readonly double[,] _inverse;
    private readonly string[] _ctype;
    private readonly string[] _cunit;
    private readonly int _lng = -1, _lat = -1;
    private readonly double _celestialScale = 1.0; // axis unit to degrees for the celestial axes
    private readonly Projection? _projection;
    private readonly CelestialRotation? _rotation;
    private readonly SpectralAxis?[] _spectral;
    private readonly WorldAxisKind[] _kinds;
    private IReadOnlyList<WorldAxisKind>? _kindsView;
    private IReadOnlyList<string>? _ctypeView, _cunitView;
    private IReadOnlyList<double>? _crpixView, _crvalView;
    private readonly double?[] _phaseZero;
    private readonly double?[] _phasePeriod;

    private WorldCoordinateSystem(FitsHeader h, char alt)
    {
        string a = alt == ' ' ? "" : alt.ToString();
        Alternate = alt;
        Name = h.Get<string>("WCSNAME" + a);
        int n = AxisCountOf(h, a);
        AxisCount = n;
        _crpix = new double[n];
        _crval = new double[n];
        _ctype = new string[n];
        _cunit = new string[n];
        var cdelt = new double[n];
        for (int i = 0; i < n; i++)
        {
            _crpix[i] = h.TryGetValue("CRPIX" + (i + 1) + a, out double p) ? p : 0.0;
            _crval[i] = h.TryGetValue("CRVAL" + (i + 1) + a, out double v) ? v : 0.0;
            cdelt[i] = h.TryGetValue("CDELT" + (i + 1) + a, out double d) ? d : 1.0;
            _ctype[i] = (h.Get<string>("CTYPE" + (i + 1) + a) ?? "").Trim();
            _cunit[i] = (h.Get<string>("CUNIT" + (i + 1) + a) ?? "").Trim();
        }

        // celestial axes: coordinate type in characters 1-4, projection code in characters 6-8 of CTYPEi
        for (int i = 0; i < n; i++)
        {
            string type = CoordinateType(_ctype[i]);
            if (type.Length == 0) continue;
            if (IsLongitude(type)) { if (_lng >= 0) throw new FitsException("Two longitude axes in the WCS."); _lng = i; }
            else if (IsLatitude(type)) { if (_lat >= 0) throw new FitsException("Two latitude axes in the WCS."); _lat = i; }
        }
        if ((_lng >= 0) != (_lat >= 0))
            throw new FitsException("A celestial WCS needs both a longitude and a latitude axis (CTYPEi).");
        if (_lng >= 0 && !Matching(CoordinateType(_ctype[_lng]), CoordinateType(_ctype[_lat])))
            throw new FitsException($"CTYPE{_lng + 1} '{_ctype[_lng]}' and CTYPE{_lat + 1} '{_ctype[_lat]}' are not a longitude and latitude pair.");

        double[,] pc = Identity(n);
        bool hasCd = false;
        var cd = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (h.TryGetValue($"PC{i + 1}_{j + 1}{a}", out double m)) pc[i, j] = m;
                if (h.TryGetValue($"CD{i + 1}_{j + 1}{a}", out double c)) { cd[i, j] = c; hasCd = true; }
            }
        }

        _matrix = new double[n, n];
        if (hasCd)
        {
            _matrix = cd; // CDELT does not take part
        }
        else
        {
            // the legacy rotation CROTAi (AIPS) of the latitude axis stands for a PC matrix when there is none
            double? crota = null;
            if (a.Length == 0 && _lat >= 0 && h.TryGetValue("CROTA" + (_lat + 1), out double r)) crota = r;
            bool anyPc = false;
            for (int i = 0; i < n && !anyPc; i++)
                for (int j = 0; j < n; j++)
                    if (h.Contains($"PC{i + 1}_{j + 1}{a}")) { anyPc = true; break; }
            if (crota is { } rho && !anyPc && _lng >= 0)
            {
                int x = _lng, y = _lat;
                double s = Trig.Sin(rho), c = Trig.Cos(rho);
                pc[x, x] = c;
                pc[y, y] = c;
                pc[x, y] = -s * cdelt[y] / cdelt[x];
                pc[y, x] = s * cdelt[x] / cdelt[y];
            }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    _matrix[i, j] = cdelt[i] * pc[i, j];
        }

        if (_lng >= 0)
        {
            _celestialScale = UnitToDegrees(_cunit[_lng]);
            if (UnitToDegrees(_cunit[_lat]) != _celestialScale) throw new FitsException("The two celestial axes must use the same CUNITi.");
            ProjectionCode = ProjectionOf(_ctype[_lng]);
            if (ProjectionCode != ProjectionOf(_ctype[_lat]))
                throw new FitsException($"The celestial axes have different projections ('{_ctype[_lng]}' and '{_ctype[_lat]}').");
            if (ProjectionCode.Length != 3 || !ProjectionCode.All(char.IsAsciiLetterUpper))
                throw new FitsException($"CTYPE{_lng + 1} '{_ctype[_lng]}' has no projection code.");
            if (!Projection.IsKnown(ProjectionCode))
                throw new NotSupportedException($"The projection '{ProjectionCode}' is not one of the 27 celestial projections of the FITS standard.");

            var pv = new double[30];
            Array.Fill(pv, double.NaN);
            for (int m = 0; m < pv.Length; m++)
                if (h.TryGetValue($"PV{_lat + 1}_{m}{a}", out double v)) pv[m] = v;
            // PVi_1 and PVi_2 of the longitude axis give the native fiducial point; the projection only moves its own fiducial
            // point when PVi_0 is not zero, and then only if both are given.
            for (int m = 5; m < 30; m++)
                if (h.Contains($"PV{_lng + 1}_{m}{a}"))
                    throw new FitsException($"PV{_lng + 1}_{m}{a}: only PVi_0 to PVi_4 are defined for the longitude axis.");
            double? phi0 = h.TryGetValue($"PV{_lng + 1}_1{a}", out double p0) ? p0 : null;
            double? theta0 = h.TryGetValue($"PV{_lng + 1}_2{a}", out double t0) ? t0 : null;
            if (theta0 is { } th && Math.Abs(th) > 90.0)
            {
                if (Math.Abs(th) > 90.0 + 1e-10) throw new FitsException($"PV{_lng + 1}_2{a} (theta_0) must be between -90 and 90.");
                theta0 = Math.CopySign(90.0, th);
            }
            bool moved = h.TryGetValue($"PV{_lng + 1}_0{a}", out double flag) && flag != 0.0 && phi0 is not null && theta0 is not null;
            _projection = Projection.Create(ProjectionCode, pv, moved ? phi0 : null, moved ? theta0 : null);

            // PVi_3 and PVi_4 of the longitude axis override LONPOLE and LATPOLE
            LongitudePole = h.TryGetValue($"PV{_lng + 1}_3{a}", out double pv3) ? pv3 : h.TryGetValue("LONPOLE" + a, out double lp) ? lp : null;
            LatitudePole = h.TryGetValue($"PV{_lng + 1}_4{a}", out double pv4) ? pv4 : h.TryGetValue("LATPOLE" + a, out double lt) ? lt : null;
            if (LongitudePole == 999.0) LongitudePole = null;
            _rotation = new CelestialRotation(_crval[_lng] * _celestialScale, _crval[_lat] * _celestialScale, LongitudePole, LatitudePole,
                phi0 ?? _projection.Phi0, theta0 ?? _projection.Theta0);
        }

        Equinox = h.TryGetValue("EQUINOX" + a, out double eq) ? eq : a.Length == 0 && h.TryGetValue("EPOCH", out double ep) ? ep : null;
        RadeSys = h.Get<string>("RADESYS" + a)?.Trim() ?? (a.Length == 0 ? h.Get<string>("RADECSYS")?.Trim() : null);
        // the standard's default for equatorial coordinates: FK4 before 1984, FK5 from 1984, ICRS when no equinox is given
        if (RadeSys is null && _lng >= 0 && CoordinateType(_ctype[_lng]) == "RA--") RadeSys = Equinox is { } year ? (year < 1984.0 ? "FK4" : "FK5") : "ICRS";
        UnappliedDistortion = _lng >= 0 && _ctype[_lng].Length > 8 ? _ctype[_lng][8..] : "";

        _spectral = new SpectralAxis?[n];
        _kinds = new WorldAxisKind[n];
        _phaseZero = new double?[n];
        _phasePeriod = new double?[n];
        for (int i = 0; i < n; i++)
        {
            string type = _ctype[i];
            if (i == _lng || i == _lat) { _kinds[i] = WorldAxisKind.Celestial; continue; }
            if (type.Equals("PHASE", StringComparison.OrdinalIgnoreCase)) _kinds[i] = WorldAxisKind.Phase;
            else if (type.Equals("TIMELAG", StringComparison.OrdinalIgnoreCase)) _kinds[i] = WorldAxisKind.TimeLag;
            else if (type.Equals("FREQUENCY", StringComparison.OrdinalIgnoreCase)) _kinds[i] = WorldAxisKind.TemporalFrequency;
            else if (type.Equals("STOKES", StringComparison.OrdinalIgnoreCase)) _kinds[i] = WorldAxisKind.Stokes;
            else if (type.Equals("COMPLEX", StringComparison.OrdinalIgnoreCase)) _kinds[i] = WorldAxisKind.Complex;
            else if (TimeFrame.IsTimeCoordinateType(type)) _kinds[i] = WorldAxisKind.Time;
            else if ((_spectral[i] = SpectralAxis.TryCreate(type, _crval[i], h, a, i + 1)) is not null) _kinds[i] = WorldAxisKind.Spectral;
            if (_kinds[i] == WorldAxisKind.Phase)
            {
                _phaseZero[i] = h.TryGetValue($"CZPHS{i + 1}{a}", out double z) ? z : null;
                _phasePeriod[i] = h.TryGetValue($"CPERI{i + 1}{a}", out double per) ? per : null;
            }
        }

        _inverse = Invert(_matrix, n);
    }

    /// <summary>The alternate description letter (a space for the primary one).</summary>
    public char Alternate { get; }

    /// <summary>WCSNAMEa, the name of the description.</summary>
    public string? Name { get; }

    /// <summary>Number of world coordinate axes (WCSAXESa).</summary>
    public int AxisCount { get; }

    /// <summary>The three-letter projection code of the celestial axes (for example <c>TAN</c>); empty when there are none.</summary>
    public string ProjectionCode { get; } = "";

    /// <summary>Whether the description has a longitude and a latitude axis.</summary>
    public bool IsCelestial => _lng >= 0;

    /// <summary>Zero-based index of the longitude axis, or -1.</summary>
    public int LongitudeAxis => _lng;

    /// <summary>Zero-based index of the latitude axis, or -1.</summary>
    public int LatitudeAxis => _lat;

    /// <summary>Reference system of the celestial coordinates (RADESYSa), such as <c>ICRS</c> or <c>FK5</c>.</summary>
    public string? RadeSys { get; }

    /// <summary>Equinox of the celestial coordinates in years (EQUINOXa), if given.</summary>
    public double? Equinox { get; }

    /// <summary>LONPOLEa as given in the header (null: default).</summary>
    public double? LongitudePole { get; }

    /// <summary>LATPOLEa as given in the header (null: default).</summary>
    public double? LatitudePole { get; }

    /// <summary>
    /// A distortion convention appended to the projection code of the celestial CTYPEs (such as <c>-SIP</c> or <c>-TPV</c>). It is not part
    /// of the standard and is not applied: the transformation is the plain projection. Empty when there is none.
    /// </summary>
    public string UnappliedDistortion { get; }

    /// <summary>What each axis is: linear, celestial, spectral, time, or one of the other time-related axes of section 9.6.</summary>
    public IReadOnlyList<WorldAxisKind> AxisKinds => _kindsView ??= Array.AsReadOnly(_kinds);

    /// <summary>Zero-based index of the time axis, or -1. An image has at most one.</summary>
    public int TimeAxis => Array.IndexOf(_kinds, WorldAxisKind.Time);

    /// <summary>CZPHSia: the time at the zero point of a phase axis (zero-based axis index), if given.</summary>
    public double? PhaseZeroPoint(int axis) => _phaseZero[axis];

    /// <summary>CPERIia: the constant period of a phase axis (zero-based axis index), if given.</summary>
    public double? PhasePeriod(int axis) => _phasePeriod[axis];

    /// <summary>The CTYPEi values.</summary>
    public IReadOnlyList<string> CoordinateTypes => _ctypeView ??= Array.AsReadOnly(_ctype);

    /// <summary>The CUNITi values as written in the header (celestial axes are nevertheless converted to degrees).</summary>
    public IReadOnlyList<string> Units => _cunitView ??= Array.AsReadOnly(_cunit);

    /// <summary>The reference pixel, CRPIXj.</summary>
    public IReadOnlyList<double> ReferencePixel => _crpixView ??= Array.AsReadOnly(_crpix);

    /// <summary>The reference values, CRVALi, in the unit of CUNITi as written in the header.</summary>
    public IReadOnlyList<double> ReferenceValue => _crvalView ??= Array.AsReadOnly(_crval);

    // ---- Construction --------------------------------------------------------------------------------------------

    /// <summary>Reads the coordinate description <paramref name="alternate"/> (a space, or a letter from A to Z) of a header.</summary>
    /// <exception cref="FitsException">The keywords are inconsistent or the description is unusable.</exception>
    /// <exception cref="NotSupportedException">The description uses a projection or algorithm that is not part of the standard.</exception>
    public static WorldCoordinateSystem FromHeader(FitsHeader header, char alternate = ' ')
    {
        ArgumentNullException.ThrowIfNull(header);
        if (alternate != ' ' && alternate is < 'A' or > 'Z') throw new ArgumentException("The alternate description is a space or a letter from A to Z.", nameof(alternate));
        return new WorldCoordinateSystem(header, alternate);
    }

    /// <summary>The alternate descriptions present in a header (the primary one is not listed).</summary>
    public static IReadOnlyList<char> AlternatesIn(FitsHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var letters = new SortedSet<char>();
        foreach (var card in header)
        {
            string k = card.Keyword;
            if (card.Kind != FitsCardKind.Value || k.Length < 6 || k[^1] is < 'A' or > 'Z') continue;
            string body = k[..^1];
            foreach (string prefix in Prefixes)
            {
                if (!body.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var rest = body.AsSpan(prefix.Length);
                bool single = prefix is "WCSNAME" or "WCSAXES";
                if ((single ? rest.Length == 0 : rest.Length > 0) && rest.IndexOfAnyExceptInRange('0', '9') < 0) letters.Add(k[^1]);
            }
        }
        return [.. letters];
    }

    private static readonly string[] Prefixes = ["CRPIX", "CRVAL", "CTYPE", "CDELT", "CUNIT", "WCSNAME", "WCSAXES"];

    private static int AxisCountOf(FitsHeader h, string a)
    {
        if (h.TryGetValue("WCSAXES" + a, out int declared) && declared is >= 0 and <= 99) return declared;
        int n = h.TryGetValue("NAXIS", out int naxis) && naxis is >= 0 and <= 99 ? naxis : 0;
        foreach (var card in h)
        {
            if (card.Kind != FitsCardKind.Value) continue;
            string k = card.Keyword;
            if (!k.EndsWith(a, StringComparison.Ordinal)) continue;
            string body = a.Length == 0 ? k : k[..^1];
            foreach (string prefix in new[] { "CRPIX", "CRVAL", "CTYPE", "CDELT", "CUNIT" })
                if (body.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(body.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i is >= 1 and <= 99)
                    n = Math.Max(n, i);
        }
        return n;
    }

    private static double[,] Identity(int n)
    {
        var m = new double[n, n];
        for (int i = 0; i < n; i++) m[i, i] = 1.0;
        return m;
    }

    private static double[,] Invert(double[,] m, int n)
    {
        var a = (double[,])m.Clone();
        var inv = Identity(n);
        for (int c = 0; c < n; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < n; r++)
                if (Math.Abs(a[r, c]) > Math.Abs(a[pivot, c])) pivot = r;
            if (a[pivot, c] == 0.0 || !double.IsFinite(a[pivot, c])) throw new FitsException("The linear transformation (PCi_j or CDi_j with CDELTi) is singular.");
            if (pivot != c)
                for (int k = 0; k < n; k++)
                {
                    (a[c, k], a[pivot, k]) = (a[pivot, k], a[c, k]);
                    (inv[c, k], inv[pivot, k]) = (inv[pivot, k], inv[c, k]);
                }
            double d = a[c, c];
            for (int k = 0; k < n; k++)
            {
                a[c, k] /= d;
                inv[c, k] /= d;
            }
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                double f = a[r, c];
                if (f == 0.0) continue;
                for (int k = 0; k < n; k++)
                {
                    a[r, k] -= f * a[c, k];
                    inv[r, k] -= f * inv[c, k];
                }
            }
        }
        return inv;
    }

    // ---- CTYPE parsing --------------------------------------------------------------------------------------------

    /// <summary>The four-character coordinate type of a celestial CTYPE (<c>RA--</c>, <c>GLON</c>, ...) or an empty string.</summary>
    private static string CoordinateType(string ctype)
    {
        if (ctype.Length < 4) return "";
        string type = ctype[..4];
        return IsLongitude(type) || IsLatitude(type) ? type : "";
    }

    private static bool Matching(string lon, string lat) =>
        lon == "RA--" ? lat == "DEC-"
        : lon.EndsWith("LON", StringComparison.Ordinal) ? lat.EndsWith("LAT", StringComparison.Ordinal) && lon[0] == lat[0]
        : lon.EndsWith("LN", StringComparison.Ordinal) && lat.EndsWith("LT", StringComparison.Ordinal) && lon[..2] == lat[..2];

    private static string ProjectionOf(string ctype) => ctype.Length >= 8 && ctype[4] == '-' ? ctype.Substring(5, 3) : "";

    private static bool IsLongitude(string type) => type == "RA--" || type.EndsWith("LON", StringComparison.Ordinal) || type.EndsWith("LN", StringComparison.Ordinal);

    private static bool IsLatitude(string type) => type == "DEC-" || type.EndsWith("LAT", StringComparison.Ordinal) || type.EndsWith("LT", StringComparison.Ordinal);

    private static double UnitToDegrees(string unit) => unit.Trim().ToLowerInvariant() switch
    {
        "" or "deg" or "degree" or "degrees" => 1.0,
        "arcmin" => 1.0 / 60.0,
        "arcsec" => 1.0 / 3600.0,
        "mas" => 1.0 / 3.6e6,
        "rad" => Trig.R2D,
        var other => throw new FitsException($"Celestial axes cannot use the unit '{other}'; use deg, arcmin, arcsec, mas or rad."),
    };

    // ---- Transformations ------------------------------------------------------------------------------------------

    /// <summary>
    /// Converts FITS pixel coordinates (one per axis, the first pixel is centred on 1.0) to world coordinates. A point that has no image
    /// in a celestial projection, such as a point outside the sphere, gives NaN for the two celestial coordinates only.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="pixel"/> does not have one value per axis.</exception>
    public double[] PixelToWorld(ReadOnlySpan<double> pixel)
    {
        if (pixel.Length != AxisCount) throw new ArgumentException($"{AxisCount} pixel coordinates are needed.", nameof(pixel));
        var world = new double[AxisCount];
        Span<double> scratch = AxisCount <= 128 ? stackalloc double[AxisCount] : new double[AxisCount];
        PixelToWorldCore(pixel, world, scratch);
        return world;
    }

    /// <summary>
    /// Converts many points at once without allocating: <paramref name="pixels"/> holds the points one after the other
    /// (<see cref="AxisCount"/> values each) and <paramref name="world"/> receives the world coordinates in the same layout.
    /// </summary>
    /// <exception cref="ArgumentException">The lengths are not a whole number of points, or <paramref name="world"/> is too short.</exception>
    public void PixelToWorld(ReadOnlySpan<double> pixels, Span<double> world)
    {
        int n = AxisCount;
        if (n == 0 || pixels.Length % n != 0) throw new ArgumentException($"The pixel values must be a whole number of points of {n} coordinates.", nameof(pixels));
        if (world.Length < pixels.Length) throw new ArgumentException("The output is too short.", nameof(world));
        Span<double> scratch = n <= 128 ? stackalloc double[n] : new double[n];
        for (int at = 0; at < pixels.Length; at += n) PixelToWorldCore(pixels.Slice(at, n), world.Slice(at, n), scratch);
    }

    private void PixelToWorldCore(ReadOnlySpan<double> pixel, Span<double> world, Span<double> x)
    {
        int n = AxisCount;
        for (int i = 0; i < n; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < n; j++)
                if (_matrix[i, j] != 0.0) sum += _matrix[i, j] * (pixel[j] - _crpix[j]);   // a zero coefficient ignores a NaN pixel
            x[i] = sum;
        }
        for (int i = 0; i < n; i++)
            world[i] = _spectral[i] is { } s ? s.IntermediateToWorld(x[i]) : x[i] + _crval[i];
        if (_lng < 0) return;
        double px = x[_lng] * _celestialScale, py = x[_lat] * _celestialScale;
        if (_projection!.Inverse(px, py, out double phi, out double theta))
        {
            _rotation!.ToCelestial(phi, theta, out double lng, out double lat);
            world[_lng] = lng;
            world[_lat] = lat;
        }
        else
        {
            world[_lng] = world[_lat] = double.NaN;
        }
    }

    /// <summary>The inverse of <see cref="PixelToWorld(ReadOnlySpan{double})"/>; NaN for the pixel coordinates that depend on a world position that cannot be reached.</summary>
    /// <exception cref="ArgumentException"><paramref name="world"/> does not have one value per axis.</exception>
    public double[] WorldToPixel(ReadOnlySpan<double> world)
    {
        if (world.Length != AxisCount) throw new ArgumentException($"{AxisCount} world coordinates are needed.", nameof(world));
        var pixel = new double[AxisCount];
        Span<double> scratch = AxisCount <= 128 ? stackalloc double[AxisCount] : new double[AxisCount];
        WorldToPixelCore(world, pixel, scratch);
        return pixel;
    }

    /// <summary>Converts many positions at once without allocating (see <see cref="PixelToWorld(ReadOnlySpan{double}, Span{double})"/>).</summary>
    /// <exception cref="ArgumentException">The lengths are not a whole number of points, or <paramref name="pixels"/> is too short.</exception>
    public void WorldToPixel(ReadOnlySpan<double> world, Span<double> pixels)
    {
        int n = AxisCount;
        if (n == 0 || world.Length % n != 0) throw new ArgumentException($"The world values must be a whole number of points of {n} coordinates.", nameof(world));
        if (pixels.Length < world.Length) throw new ArgumentException("The output is too short.", nameof(pixels));
        Span<double> scratch = n <= 128 ? stackalloc double[n] : new double[n];
        for (int at = 0; at < world.Length; at += n) WorldToPixelCore(world.Slice(at, n), pixels.Slice(at, n), scratch);
    }

    private void WorldToPixelCore(ReadOnlySpan<double> world, Span<double> pixel, Span<double> x)
    {
        int n = AxisCount;
        for (int i = 0; i < n; i++)
            x[i] = _spectral[i] is { } s ? s.WorldToIntermediate(world[i]) : world[i] - _crval[i];
        if (_lng >= 0)
        {
            _rotation!.ToNative(world[_lng], world[_lat], out double phi, out double theta);
            if (_projection!.Forward(phi, theta, out double px, out double py))
            {
                x[_lng] = px / _celestialScale;
                x[_lat] = py / _celestialScale;
            }
            else
            {
                x[_lng] = x[_lat] = double.NaN;
            }
        }
        for (int i = 0; i < n; i++)
        {
            double sum = 0.0;
            for (int j = 0; j < n; j++)
                if (_inverse[i, j] != 0.0) sum += _inverse[i, j] * x[j];   // a zero coefficient ignores a NaN coordinate
            pixel[i] = sum + _crpix[i];
        }
    }
}

/// <summary>The kind of a world coordinate axis.</summary>
public enum WorldAxisKind
{
    /// <summary>An ordinary linear axis.</summary>
    Linear,

    /// <summary>Longitude or latitude of a celestial projection.</summary>
    Celestial,

    /// <summary>A spectral coordinate (frequency, wavelength, velocity, ...).</summary>
    Spectral,

    /// <summary>Time (<c>TIME</c> or one of the time scales of Table 30).</summary>
    Time,

    /// <summary>Temporal phase (<c>PHASE</c>).</summary>
    Phase,

    /// <summary>Time lag of correlation spectra (<c>TIMELAG</c>).</summary>
    TimeLag,

    /// <summary>Temporal frequency (<c>FREQUENCY</c>).</summary>
    TemporalFrequency,

    /// <summary>Polarization (<c>STOKES</c>); see <see cref="StokesParameters"/> for the conventional values (section 8.5).</summary>
    Stokes,

    /// <summary>Real, imaginary (and weight) components of complex data (<c>COMPLEX</c>, section 8.5): coordinates 1, 2 and 3.</summary>
    Complex,
}

/// <summary>Adds the WCS to the HDU classes whose header can carry one.</summary>
public static class WorldCoordinateSystemExtensions
{
    /// <summary>The world coordinate system described by the header of the HDU (see <see cref="WorldCoordinateSystem.FromHeader"/>).</summary>
    public static WorldCoordinateSystem GetWorldCoordinateSystem(this Hdu hdu, char alternate = ' ')
    {
        ArgumentNullException.ThrowIfNull(hdu);
        return WorldCoordinateSystem.FromHeader(hdu.Header, alternate);
    }
}
