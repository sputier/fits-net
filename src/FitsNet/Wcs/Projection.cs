namespace FitsNet.Wcs;

/// <summary>Trigonometry in degrees that is exact at multiples of 90 degrees.</summary>
internal static class Trig
{
    public const double D2R = Math.PI / 180.0;
    public const double R2D = 180.0 / Math.PI;

    public static double Sin(double degrees)
    {
        if (degrees % 90.0 == 0.0)
        {
            int i = (int)Math.Abs(Math.Floor(degrees / 90.0 - 0.5) % 4.0);
            return i switch { 0 => 1.0, 2 => -1.0, _ => 0.0 };
        }
        return Math.Sin(degrees * D2R);
    }

    public static double Cos(double degrees)
    {
        if (degrees % 90.0 == 0.0)
        {
            int i = (int)Math.Abs(Math.Floor(degrees / 90.0 + 0.5) % 4.0);
            return i switch { 0 => 1.0, 2 => -1.0, _ => 0.0 };
        }
        return Math.Cos(degrees * D2R);
    }

    public static double Tan(double degrees)
    {
        double r = degrees % 360.0;
        if (r == 0.0 || r == 180.0 || r == -180.0) return 0.0;
        if (r == 45.0 || r == -135.0 || r == 225.0) return 1.0;
        if (r == -45.0 || r == 135.0 || r == -225.0) return -1.0;
        return Math.Tan(degrees * D2R);
    }

    /// <summary>ln(1 + x) accurate for small x (the framework version is not).</summary>
    public static double Log1P(double x)
    {
        double u = 1.0 + x;
        if (u == 1.0) return x;
        return Math.Log(u) * x / (u - 1.0);   // the error of 1 + x is divided out (Kahan)
    }

    public static double Asin(double x) => Math.Asin(x) * R2D;

    public static double Acos(double x) => Math.Acos(x) * R2D;

    public static double Atan(double x) => Math.Atan(x) * R2D;

    /// <summary>
    /// atan2 in degrees with the exact answers of the reference implementation on the axes: a zero ordinate gives 0 or +180 whatever its sign,
    /// a zero abscissa gives +90 or -90. The sign of a zero ordinate is rounding noise and must not choose between -180 and +180.
    /// </summary>
    public static double Atan2(double y, double x)
    {
        if (y == 0.0)
        {
            if (x >= 0.0) return 0.0;
            if (x < 0.0) return 180.0;
        }
        else if (x == 0.0)
        {
            if (y > 0.0) return 90.0;
            if (y < 0.0) return -90.0;
        }
        return Math.Atan2(y, x) * R2D;
    }

    /// <summary>asin that accepts values a hair outside [-1, 1] (rounding) and rejects the others with NaN.</summary>
    public static double AsinTolerant(double x, double tolerance = 1e-13) =>
        Math.Abs(x) <= 1.0 ? Asin(x) : Math.Abs(x) <= 1.0 + tolerance ? Math.CopySign(90.0, x) : double.NaN;

    public static double AcosTolerant(double x, double tolerance = 1e-13) =>
        Math.Abs(x) <= 1.0 ? Acos(x) : Math.Abs(x) <= 1.0 + tolerance ? (x > 0 ? 0.0 : 180.0) : double.NaN;
}

/// <summary>
/// A celestial map projection of the FITS standard (section 8.2, Calabretta and Greisen 2002): the link between the native spherical
/// coordinates (phi, theta) and the projection plane (x, y). Every angle and every plane coordinate is in degrees; the plane unit
/// is the length of one degree on the generating sphere. Functions answer NaN when the point has no image.
/// </summary>
internal abstract class Projection
{
    protected const double R0 = Trig.R2D;

    /// <summary>The PVi_m parameters of the latitude axis, indexed by m (NaN when absent).</summary>
    protected readonly double[] Pv = new double[30];

    protected Projection(string code) => Code = code;

    public string Code { get; }

    /// <summary>Native coordinates of the fiducial point, in the projection's default unless the header changed them.</summary>
    public double Phi0 { get; private set; }

    public double Theta0 { get; private set; }

    /// <summary>Plane coordinates of the fiducial point (zero unless the header moved it).</summary>
    public double X0 { get; private set; }

    public double Y0 { get; private set; }

    /// <summary>Default fiducial theta: 90 for zenithal projections, 0 for most others, theta_a for the conics.</summary>
    protected virtual double DefaultTheta0 => 0.0;

    protected double P(int m, double fallback) => double.IsNaN(Pv[m]) ? fallback : Pv[m];

    /// <summary>Plane to native sphere (the offsets of the fiducial point are not applied).</summary>
    protected abstract bool InverseCore(double x, double y, out double phi, out double theta);

    /// <summary>Native sphere to plane (the offsets of the fiducial point are not applied).</summary>
    protected abstract bool ForwardCore(double phi, double theta, out double x, out double y);

    /// <summary>Validates the parameters; throws <see cref="FitsException"/> for unusable ones.</summary>
    protected virtual void Setup() { }

    /// <summary>Plane to native sphere; false when the point has no image or falls outside the native sphere.</summary>
    public bool Inverse(double x, double y, out double phi, out double theta)
    {
        if (!InverseCore(x + X0, y + Y0, out phi, out theta) || double.IsNaN(phi) || double.IsNaN(theta)) return false;
        // the native longitude is within [-180, 180] and the latitude within [-90, 90], up to rounding
        const double tolerance = 1e-12;
        if (phi < -180.0) { if (phi < -180.0 - tolerance) return false; phi = -180.0; }
        else if (phi > 180.0) { if (phi > 180.0 + tolerance) return false; phi = 180.0; }
        if (theta < -90.0) { if (theta < -90.0 - tolerance) return false; theta = -90.0; }
        else if (theta > 90.0) { if (theta > 90.0 + tolerance) return false; theta = 90.0; }
        return true;
    }

    public bool Forward(double phi, double theta, out double x, out double y)
    {
        bool ok = ForwardCore(phi, theta, out x, out y);
        x -= X0;
        y -= Y0;
        return ok;
    }

    private void Initialise(double[] pv, double? phi0, double? theta0)
    {
        Array.Copy(pv, Pv, Math.Min(pv.Length, Pv.Length));
        Setup();
        X0 = Y0 = 0.0;
        Phi0 = 0.0;
        Theta0 = DefaultTheta0;
        if (phi0 is not null || theta0 is not null)
        {
            Phi0 = phi0 ?? 0.0;
            Theta0 = theta0 ?? DefaultTheta0;
            if (!ForwardCore(Phi0, Theta0, out double x0, out double y0))
                throw new FitsException($"The fiducial point of the {Code} projection cannot be projected.");
            (X0, Y0) = (x0, y0);
        }
    }

    /// <summary>Creates the projection with the given three-letter code.</summary>
    /// <param name="code">The three-letter code of the projection.</param>
    /// <param name="pv">PVi_m of the latitude axis (index m; NaN means absent).</param>
    /// <param name="phi0">Native longitude of the fiducial point if the header moved it.</param>
    /// <param name="theta0">Native latitude of the fiducial point if the header moved it.</param>
    public static Projection Create(string code, double[] pv, double? phi0, double? theta0)
    {
        Projection p = code switch
        {
            "AZP" => new Azp(),
            "SZP" => new Szp(),
            "TAN" => new Tan(),
            "STG" => new Stg(),
            "SIN" => new Sin(),
            "ARC" => new Arc(),
            "ZPN" => new Zpn(),
            "ZEA" => new Zea(),
            "AIR" => new Air(),
            "CYP" => new Cyp(),
            "CEA" => new Cea(),
            "CAR" => new Car(),
            "MER" => new Mer(),
            "SFL" => new Sfl(),
            "PAR" => new Par(),
            "MOL" => new Mol(),
            "AIT" => new Ait(),
            "COP" => new Cop(),
            "COE" => new Coe(),
            "COD" => new Cod(),
            "COO" => new Coo(),
            "BON" => new Bon(),
            "PCO" => new Pco(),
            "TSC" => new Tsc(),
            "CSC" => new Csc(),
            "QSC" => new Qsc(),
            "HPX" => new Hpx(),
            _ => throw new NotSupportedException($"The projection code '{code}' is not one of the 27 projections of the FITS standard."),
        };
        p.Initialise(pv, phi0, theta0);
        return p;
    }

    public static bool IsKnown(string code) => Codes.Contains(code);

    private static readonly HashSet<string> Codes =
    [
        "AZP", "SZP", "TAN", "STG", "SIN", "ARC", "ZPN", "ZEA", "AIR", "CYP", "CEA", "CAR", "MER", "SFL", "PAR", "MOL", "AIT",
        "COP", "COE", "COD", "COO", "BON", "PCO", "TSC", "CSC", "QSC", "HPX",
    ];

    protected static bool Finite(double x, double y) => double.IsFinite(x) && double.IsFinite(y);
}

// ================================ zenithal projections (fiducial point at the native pole) ================================

internal abstract class Zenithal(string code) : Projection(code)
{
    protected override double DefaultTheta0 => 90.0;

    /// <summary>Radius in the plane for a native latitude, NaN when undefined.</summary>
    protected abstract double Radius(double theta);

    /// <summary>Native latitude for a radius, NaN when undefined.</summary>
    protected abstract double Latitude(double r);

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double r = Radius(theta);
        (x, y) = (double.NaN, double.NaN);
        if (double.IsNaN(r)) return false;
        x = r * Trig.Sin(phi);
        y = -r * Trig.Cos(phi);
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double r = Math.Sqrt(x * x + y * y);
        phi = r == 0.0 ? 0.0 : Trig.Atan2(x, -y);
        theta = Latitude(r);
        return !double.IsNaN(theta);
    }
}

internal sealed class Tan() : Zenithal("TAN")
{
    protected override double Radius(double theta) => theta <= 0.0 ? double.NaN : R0 * Trig.Cos(theta) / Trig.Sin(theta);

    protected override double Latitude(double r) => Trig.Atan2(R0, r);
}

internal sealed class Stg() : Zenithal("STG")
{
    protected override double Radius(double theta) => theta == -90.0 ? double.NaN : 2.0 * R0 * Trig.Cos(theta) / (1.0 + Trig.Sin(theta));

    protected override double Latitude(double r) => 90.0 - 2.0 * Trig.Atan(r / (2.0 * R0));
}

internal sealed class Arc() : Zenithal("ARC")
{
    protected override double Radius(double theta) => 90.0 - theta;

    protected override double Latitude(double r) => 90.0 - r;
}

internal sealed class Zea() : Zenithal("ZEA")
{
    protected override double Radius(double theta) => 2.0 * R0 * Trig.Sin((90.0 - theta) / 2.0);

    protected override double Latitude(double r)
    {
        double s = r / (2.0 * R0);
        return Math.Abs(s) > 1.0 ? double.NaN : 90.0 - 2.0 * Trig.Asin(s);
    }
}

internal sealed class Sin() : Projection("SIN")
{
    private double _xi, _eta;

    protected override double DefaultTheta0 => 90.0;

    protected override void Setup() => (_xi, _eta) = (P(1, 0.0), P(2, 0.0));

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double sinPhi = Trig.Sin(phi), cosPhi = Trig.Cos(phi);
        // 1 - sin(theta) without cancellation near the pole
        double colatitude = (90.0 - Math.Abs(theta)) * Trig.D2R;
        double t = colatitude < 1e-5 ? (theta > 0.0 ? colatitude * colatitude / 2.0 : 2.0 - colatitude * colatitude / 2.0) : 1.0 - Trig.Sin(theta);
        double cosTheta = colatitude < 1e-5 ? colatitude : Trig.Cos(theta);
        x = R0 * (cosTheta * sinPhi + _xi * t);
        y = -R0 * (cosTheta * cosPhi - _eta * t);
        if (_xi == 0.0 && _eta == 0.0) return theta >= 0.0; // the southern hemisphere overlaps the northern one
        // slant orthographic: the far side of the generating sphere is hidden
        return theta >= -Trig.Atan(_xi * sinPhi - _eta * cosPhi);
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double xp = x / R0, yp = y / R0;
        phi = theta = double.NaN;
        double t;
        if (_xi == 0.0 && _eta == 0.0)
        {
            double r2 = xp * xp + yp * yp;
            if (r2 > 1.0)
            {
                if (r2 > 1.0 + 2e-13) return false;
                r2 = 1.0;
            }
            theta = Trig.Acos(Math.Sqrt(r2));
            phi = r2 == 0.0 ? 0.0 : Trig.Atan2(xp, -yp);
            return true;
        }
        // (x' - xi t)^2 + (eta t - y')^2 = 2t - t^2, with t = 1 - sin(theta)
        double a = _xi * _xi + _eta * _eta + 1.0;
        double b = _xi * xp + _eta * yp + 1.0;
        double c = xp * xp + yp * yp;
        double d = b * b - a * c;
        if (d < 0.0)
        {
            if (d < -1e-12) return false;
            d = 0.0;
        }
        t = (b - Math.Sqrt(d)) / a;
        double s = 1.0 - t;
        if (Math.Abs(s) > 1.0) return false;
        theta = Trig.Asin(s);
        double u = xp - _xi * t, v = -yp + _eta * t;
        phi = u == 0.0 && v == 0.0 ? 0.0 : Trig.Atan2(u, v);
        return true;
    }
}

internal sealed class Azp() : Projection("AZP")
{
    private double _mu, _gamma;

    protected override double DefaultTheta0 => 90.0;

    protected override void Setup()
    {
        _mu = P(1, 0.0);
        _gamma = P(2, 0.0);
        if (_mu == -1.0 || Trig.Cos(_gamma) == 0.0) throw new FitsException("Invalid parameters for the AZP projection.");
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double cosg = Trig.Cos(_gamma), tang = Trig.Tan(_gamma);
        double denominator = _mu + Trig.Sin(theta) + Trig.Cos(theta) * Trig.Cos(phi) * tang;
        (x, y) = (double.NaN, double.NaN);
        if (denominator == 0.0) return false;
        double r = R0 * (_mu + 1.0) * Trig.Cos(theta) / denominator;
        x = r * Trig.Sin(phi);
        y = -r * Trig.Cos(phi) / cosg;
        // the point must not lie on the hidden part of the sphere: past the horizon (overlap) or where the projection diverges
        double horizon = Math.Abs(_mu) > 1.0 ? Trig.Asin(-1.0 / _mu) : -90.0;
        if (theta < horizon) return false;
        if (Math.Abs(_mu * cosg) < 1.0)
        {
            double s = tang * Trig.Cos(phi);
            double t = _mu / Math.Sqrt(1.0 + s * s);
            if (Math.Abs(t) <= 1.0)
            {
                s = Trig.Atan(-s);
                t = Trig.Asin(t);
                double a = s - t, b = s + t + 180.0;
                if (a > 90.0) a -= 360.0;
                if (b > 90.0) b -= 360.0;
                if (theta < Math.Max(a, b)) return false;
            }
        }
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double cosg = Trig.Cos(_gamma), sing = Trig.Sin(_gamma);
        double yc = y * cosg;
        double r = Math.Sqrt(x * x + yc * yc);
        if (r == 0.0)
        {
            (phi, theta) = (0.0, 90.0);
            return true;
        }
        phi = Trig.Atan2(x, -yc);
        double q = R0 * (_mu + 1.0) + y * sing;
        double s = r / q;
        double t = s * _mu / Math.Sqrt(s * s + 1.0);
        s = Trig.Atan2(1.0, s);
        if (Math.Abs(t) > 1.0)
        {
            if (Math.Abs(t) > 1.0 + 1e-13)
            {
                theta = double.NaN;
                return false;
            }
            t = Math.CopySign(90.0, t);
        }
        else
        {
            t = Trig.Asin(t);
        }
        double a = s - t, b = s + t + 180.0;
        if (a > 90.0) a -= 360.0;
        if (b > 90.0) b -= 360.0;
        theta = Math.Max(a, b);
        return true;
    }
}

internal sealed class Szp() : Projection("SZP")
{
    private double _xp, _yp, _zp, _mu;

    protected override double DefaultTheta0 => 90.0;

    protected override void Setup()
    {
        double mu = P(1, 0.0), phic = P(2, 0.0), thetac = P(3, 90.0);
        _mu = mu;
        _xp = -mu * Trig.Cos(thetac) * Trig.Sin(phic);
        _yp = mu * Trig.Cos(thetac) * Trig.Cos(phic);
        _zp = mu * Trig.Sin(thetac) + 1.0;
        if (_zp == 0.0) throw new FitsException("Invalid parameters for the SZP projection.");
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double s = 1.0 - Trig.Sin(theta);
        double t = _zp - s;
        (x, y) = (double.NaN, double.NaN);
        if (t == 0.0) return false;
        x = R0 * (_zp * Trig.Cos(theta) * Trig.Sin(phi) - _xp * s) / t;
        y = -R0 * (_zp * Trig.Cos(theta) * Trig.Cos(phi) + _yp * s) / t;

        double horizon = Math.Abs(1.0 - _zp) < 1.0 ? Trig.Asin(1.0 - _zp) : -90.0;
        if (theta < horizon) return false;
        if (Math.Abs(_mu) > 1.0)
        {
            double w = _xp * Trig.Sin(phi) - _yp * Trig.Cos(phi);
            double q = 1.0 / Math.Sqrt((_zp - 1.0) * _zp - 1.0 + w * w);
            if (Math.Abs(q) <= 1.0)
            {
                w = Trig.Atan2(w, _zp - 1.0);
                q = Trig.Asin(q);
                double a = w - q, b = w + q + 180.0;
                if (a > 90.0) a -= 360.0;
                if (b > 90.0) b -= 360.0;
                if (theta < Math.Max(a, b)) return false;
            }
        }
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double xp = x / R0, yp = y / R0;
        phi = theta = double.NaN;
        double u = _xp - xp, v = _yp - yp;
        double a = u * u + v * v + _zp * _zp;
        double b = 2.0 * _zp * (xp * u + yp * v - _zp);
        double c = _zp * _zp * (xp * xp + yp * yp);
        double s;
        if (a == 0.0) return false;
        double d = b * b - 4.0 * a * c;
        if (d < 0.0)
        {
            if (d < -1e-12 * Math.Max(1.0, b * b)) return false;
            d = 0.0;
        }
        double sq = Math.Sqrt(d);
        double s1 = (-b - sq) / (2.0 * a), s2 = (-b + sq) / (2.0 * a);
        // the smaller s is the larger latitude: the nearer side
        s = Math.Min(s1, s2);
        if (s < 0.0 && Math.Max(s1, s2) >= 0.0 && s < -1e-14) s = Math.Max(s1, s2);
        double sinTheta = 1.0 - s;
        if (Math.Abs(sinTheta) > 1.0)
        {
            if (Math.Abs(sinTheta) > 1.0 + 1e-13) return false;
            sinTheta = Math.CopySign(1.0, sinTheta);
        }
        theta = Trig.Asin(sinTheta);
        // zp cos(theta) sin(phi) = x'(zp - s) + xp s ;  zp cos(theta) cos(phi) = -y'(zp - s) - yp s
        double east = xp * (_zp - s) + _xp * s;
        double north = -yp * (_zp - s) - _yp * s;
        phi = east == 0.0 && north == 0.0 ? 0.0 : Trig.Atan2(east, north);
        return true;
    }
}

internal sealed class Zpn() : Zenithal("ZPN")
{
    private double[] _p = [];
    private double _max = Math.PI;

    protected override void Setup()
    {
        int degree = -1;
        for (int m = 0; m < Pv.Length; m++)
            if (!double.IsNaN(Pv[m]) && Pv[m] != 0.0) degree = m;
        if (degree < 1) throw new FitsException("The ZPN projection needs PVi_m parameters with a non-zero term of degree 1 or more.");
        _p = new double[degree + 1];
        for (int m = 0; m <= degree; m++) _p[m] = double.IsNaN(Pv[m]) ? 0.0 : Pv[m];

        // the polynomial may only be inverted up to its first extremum
        _max = Math.PI;
        const int steps = 4096;
        double previous = Slope(0.0);
        for (int i = 1; i <= steps; i++)
        {
            double z = Math.PI * i / steps;
            double slope = Slope(z);
            if (slope * previous <= 0.0 && previous != 0.0)
            {
                double lo = Math.PI * (i - 1) / steps, hi = z;
                for (int k = 0; k < 80; k++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (Slope(mid) * previous > 0.0) lo = mid; else hi = mid;
                }
                _max = lo;
                break;
            }
            if (slope != 0.0) previous = slope;
        }
    }

    private double Value(double z)
    {
        double sum = 0.0;
        for (int m = _p.Length - 1; m >= 0; m--) sum = sum * z + _p[m];
        return sum;
    }

    private double Slope(double z)
    {
        double sum = 0.0;
        for (int m = _p.Length - 1; m >= 1; m--) sum = sum * z + m * _p[m];
        return sum;
    }

    protected override double Radius(double theta)
    {
        double z = (90.0 - theta) * Trig.D2R;
        return z > _max + 1e-12 ? double.NaN : R0 * Value(z);
    }

    protected override double Latitude(double r)
    {
        double target = r / R0;
        double rising = Slope(0.0) >= 0.0 ? 1.0 : -1.0;
        double f0 = (Value(0.0) - target) * rising;
        double f1 = (Value(_max) - target) * rising;
        if (f0 > 0.0) return double.NaN;
        if (f1 < 0.0) return f1 > -1e-13 ? 90.0 - _max * Trig.R2D : double.NaN;
        double lo = 0.0, hi = _max;
        for (int k = 0; k < 100; k++)
        {
            double mid = 0.5 * (lo + hi);
            if ((Value(mid) - target) * rising < 0.0) lo = mid; else hi = mid;
        }
        return 90.0 - 0.5 * (lo + hi) * Trig.R2D;
    }
}

internal sealed class Air() : Zenithal("AIR")
{
    private double _thetaB, _w;

    protected override void Setup()
    {
        _thetaB = P(1, 90.0);
        if (_thetaB == 90.0)
        {
            _w = -0.5;
        }
        else if (_thetaB > -90.0)
        {
            double xib = (90.0 - _thetaB) / 2.0;
            double tan = Trig.Tan(xib), sin = Trig.Sin(xib);
            _w = 0.5 * Trig.Log1P(-sin * sin) / (tan * tan);   // ln(cos x) = (1/2) ln(1 - sin^2 x), exact for small x
        }
        else
        {
            throw new FitsException("Invalid parameters for the AIR projection.");
        }
    }

    private double RadiusOf(double xi)
    {
        if (xi == 0.0) return 0.0;
        double tan = Trig.Tan(xi);
        return -2.0 * R0 * (Math.Log(Trig.Cos(xi)) / tan + _w * tan);
    }

    protected override double Radius(double theta) => theta == -90.0 ? double.NaN : RadiusOf((90.0 - theta) / 2.0);

    protected override double Latitude(double r)
    {
        if (r == 0.0) return 90.0;
        double lo = 0.0, hi = 90.0 - 1e-9;
        if (RadiusOf(hi) < r) return double.NaN;
        for (int k = 0; k < 200; k++)
        {
            double mid = 0.5 * (lo + hi);
            if (RadiusOf(mid) < r) lo = mid; else hi = mid;
            if (hi - lo < 1e-14) break;
        }
        return 90.0 - 2.0 * 0.5 * (lo + hi);
    }
}

// ================================ cylindrical projections ================================

internal sealed class Cyp() : Projection("CYP")
{
    private double _mu, _lambda;

    protected override void Setup()
    {
        _mu = P(1, 1.0);
        _lambda = P(2, 1.0);
        if (_lambda == 0.0 || _mu + _lambda == 0.0) throw new FitsException("Invalid parameters for the CYP projection.");
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        x = _lambda * phi;
        double s = _mu + Trig.Cos(theta);
        y = s == 0.0 ? double.NaN : R0 * (_mu + _lambda) * Trig.Sin(theta) / s;
        return s != 0.0;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        phi = x / _lambda;
        double eta = y / (R0 * (_mu + _lambda));
        theta = Trig.Atan2(eta, 1.0) + Trig.Asin(eta * _mu / Math.Sqrt(eta * eta + 1.0));
        return true;
    }
}

internal sealed class Cea() : Projection("CEA")
{
    private double _lambda;

    protected override void Setup()
    {
        _lambda = P(1, 1.0);
        if (_lambda <= 0.0 || _lambda > 1.0) throw new FitsException("Invalid parameters for the CEA projection.");
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        x = phi;
        y = R0 * Trig.Sin(theta) / _lambda;
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        phi = x;
        theta = Trig.AsinTolerant(y * _lambda / R0);
        return !double.IsNaN(theta);
    }
}

internal sealed class Car() : Projection("CAR")
{
    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        (x, y) = (phi, theta);
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        (phi, theta) = (x, y);
        return true;
    }
}

internal sealed class Mer() : Projection("MER")
{
    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        x = phi;
        bool ok = Math.Abs(theta) < 90.0;
        y = ok ? R0 * Math.Log(Trig.Tan((90.0 + theta) / 2.0)) : double.NaN;
        return ok;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        phi = x;
        theta = 90.0 - 2.0 * Trig.Atan(Math.Exp(-y / R0));
        return true;
    }
}

// ================================ pseudocylindrical projections ================================

internal sealed class Sfl() : Projection("SFL")
{
    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        (x, y) = (phi * Trig.Cos(theta), theta);
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        theta = y;
        double c = Trig.Cos(y);
        phi = c == 0.0 ? 0.0 : x / c;
        return Math.Abs(y) <= 90.0 + 1e-12 && (c != 0.0 || x == 0.0);
    }
}

internal sealed class Par() : Projection("PAR")
{
    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        x = phi * (2.0 * Trig.Cos(2.0 * theta / 3.0) - 1.0);
        y = 180.0 * Trig.Sin(theta / 3.0);
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double s = y / 180.0;
        phi = theta = double.NaN;
        if (Math.Abs(s) > 1.0) return false;
        theta = 3.0 * Trig.Asin(s);
        double t = 1.0 - 4.0 * s * s;
        if (Math.Abs(t) < 1e-13)
        {
            // the pole of the parabola: the whole line of the pole maps to one point
            phi = 0.0;
            return Math.Abs(x) < 1e-9;
        }
        phi = x / t;
        return true;
    }
}

internal sealed class Mol() : Projection("MOL")
{
    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double gamma;
        if (Math.Abs(theta) == 90.0)
        {
            gamma = Math.CopySign(Math.PI / 2.0, theta);
        }
        else
        {
            // Newton iteration on 2g + sin(2g) = pi sin(theta)
            double target = Math.PI * Trig.Sin(theta);
            gamma = theta * Trig.D2R;
            for (int k = 0; k < 100; k++)
            {
                double slope = 2.0 + 2.0 * Math.Cos(2.0 * gamma);
                if (slope == 0.0) break;
                double step = (2.0 * gamma + Math.Sin(2.0 * gamma) - target) / slope;
                gamma -= step;
                if (Math.Abs(step) < 1e-16) break;
            }
        }
        x = 2.0 * Math.Sqrt(2.0) / Math.PI * phi * Math.Cos(gamma);
        y = Math.Sqrt(2.0) * R0 * Math.Sin(gamma);
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        phi = theta = double.NaN;
        double s = y / (Math.Sqrt(2.0) * R0);
        if (Math.Abs(s) > 1.0)
        {
            if (Math.Abs(s) > 1.0 + 1e-12) return false;
            s = Math.CopySign(1.0, s);
        }
        double gamma = Math.Asin(s);
        double c = Math.Cos(gamma);
        if (c == 0.0)
        {
            if (Math.Abs(x) > 1e-12) return false;
            phi = 0.0;
        }
        else
        {
            phi = x * Math.PI / (2.0 * Math.Sqrt(2.0) * c);
        }
        theta = Trig.AsinTolerant((2.0 * gamma + Math.Sin(2.0 * gamma)) / Math.PI);
        return !double.IsNaN(theta);
    }
}

internal sealed class Ait() : Projection("AIT")
{
    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double c = Trig.Cos(theta) * Trig.Cos(phi / 2.0);
        double w = Math.Sqrt(2.0 / (1.0 + c));
        x = 2.0 * R0 * w * Trig.Cos(theta) * Trig.Sin(phi / 2.0);
        y = R0 * w * Trig.Sin(theta);
        return 1.0 + c != 0.0;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        phi = theta = double.NaN;
        double u = x / (4.0 * R0), v = y / (2.0 * R0);
        double z2 = 1.0 - u * u - v * v;
        if (z2 < 0.0)
        {
            if (z2 < -1e-13) return false;
            z2 = 0.0;
        }
        double z = Math.Sqrt(z2);
        phi = 2.0 * Trig.Atan2(0.5 * z * x / R0, 2.0 * z2 - 1.0);
        theta = Trig.AsinTolerant(y * z / R0);
        return !double.IsNaN(theta);
    }
}

// ================================ conic projections (fiducial latitude theta_a) ================================

internal abstract class Conic(string code) : Projection(code)
{
    protected double ThetaA, Eta, C, Y0Cone;

    protected override double DefaultTheta0 => ThetaA;

    /// <summary>Radius from the apex of the cone for a latitude.</summary>
    protected abstract double ConeRadius(double theta);

    /// <summary>Latitude for a radius from the apex.</summary>
    protected abstract double ConeLatitude(double r);

    protected void Require()
    {
        if (double.IsNaN(Pv[1])) throw new FitsException($"The {Code} projection needs PVi_1 (theta_a).");
        ThetaA = Pv[1];
        Eta = P(2, 0.0);
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double r = ConeRadius(theta);
        (x, y) = (double.NaN, double.NaN);
        if (double.IsNaN(r)) return false;
        double a = C * phi;
        x = r * Trig.Sin(a);
        y = Y0Cone - r * Trig.Cos(a);
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double dy = Y0Cone - y;
        double r = Math.Sqrt(x * x + dy * dy);
        if (ThetaA < 0.0) r = -r;
        phi = r == 0.0 ? 0.0 : Trig.Atan2(x / r, dy / r) / C;
        theta = ConeLatitude(r);
        return !double.IsNaN(theta);
    }
}

internal sealed class Cop() : Conic("COP")
{
    protected override void Setup()
    {
        Require();
        C = Trig.Sin(ThetaA);
        if (C == 0.0 || Trig.Cos(Eta) == 0.0) throw new FitsException("Invalid parameters for the COP projection.");
        Y0Cone = R0 * Trig.Cos(Eta) / Trig.Tan(ThetaA);
    }

    protected override double ConeRadius(double theta)
    {
        double t = theta - ThetaA;
        return Math.Abs(t) >= 90.0 ? double.NaN : R0 * Trig.Cos(Eta) * (1.0 / Trig.Tan(ThetaA) - Trig.Tan(t));
    }

    protected override double ConeLatitude(double r) => ThetaA + Trig.Atan(1.0 / Trig.Tan(ThetaA) - r / (R0 * Trig.Cos(Eta)));
}

internal sealed class Coe() : Conic("COE")
{
    private double _gamma, _s1s2;

    protected override void Setup()
    {
        Require();
        double t1 = ThetaA - Eta, t2 = ThetaA + Eta;
        _gamma = Trig.Sin(t1) + Trig.Sin(t2);
        if (_gamma == 0.0) throw new FitsException("Invalid parameters for the COE projection.");
        _s1s2 = 1.0 + Trig.Sin(t1) * Trig.Sin(t2);
        C = _gamma / 2.0;
        Y0Cone = 2.0 * R0 / _gamma * Math.Sqrt(_s1s2 - _gamma * Trig.Sin(ThetaA));
    }

    protected override double ConeRadius(double theta)
    {
        double q = _s1s2 - _gamma * Trig.Sin(theta);
        return q < 0.0 ? double.NaN : 2.0 * R0 / _gamma * Math.Sqrt(q);
    }

    protected override double ConeLatitude(double r)
    {
        double w = r * _gamma / (2.0 * R0);
        return Trig.AsinTolerant((_s1s2 - w * w) / _gamma);
    }
}

internal sealed class Cod() : Conic("COD")
{
    protected override void Setup()
    {
        Require();
        double t = Eta * Trig.D2R;
        C = Eta == 0.0 ? Trig.Sin(ThetaA) : Trig.Sin(ThetaA) * Trig.Sin(Eta) / t;
        if (C == 0.0) throw new FitsException("Invalid parameters for the COD projection.");
        double eta = Eta == 0.0 ? 1.0 : t / Math.Tan(t);
        Y0Cone = R0 * eta / Trig.Tan(ThetaA);
    }

    protected override double ConeRadius(double theta) => ThetaA - theta + Y0Cone;

    protected override double ConeLatitude(double r) => ThetaA - r + Y0Cone;
}

internal sealed class Coo() : Conic("COO")
{
    private double _psi;

    protected override void Setup()
    {
        Require();
        double t1 = ThetaA - Eta, t2 = ThetaA + Eta;
        if (Math.Abs(t1) >= 90.0 || Math.Abs(t2) >= 90.0 || t1 == -90.0) throw new FitsException("Invalid parameters for the COO projection.");
        double tan1 = Trig.Tan(45.0 - t1 / 2.0), tan2 = Trig.Tan(45.0 - t2 / 2.0);
        C = t1 == t2 ? Trig.Sin(t1) : Math.Log(Trig.Cos(t2) / Trig.Cos(t1)) / Math.Log(tan2 / tan1);
        if (C == 0.0 || double.IsNaN(C)) throw new FitsException("Invalid parameters for the COO projection.");
        _psi = Trig.Cos(t1) / (C * Math.Pow(tan1, C));
        Y0Cone = R0 * _psi * Math.Pow(Trig.Tan(45.0 - ThetaA / 2.0), C);
    }

    protected override double ConeRadius(double theta)
    {
        if (theta == -90.0) return ThetaA < 0 ? 0.0 : double.NaN;
        return R0 * _psi * Math.Pow(Trig.Tan(45.0 - theta / 2.0), C);
    }

    protected override double ConeLatitude(double r)
    {
        if (r == 0.0) return C < 0.0 ? -90.0 : 90.0;
        double q = Math.Pow(r / (R0 * _psi), 1.0 / C);
        return 90.0 - 2.0 * Trig.Atan(q);
    }
}

// ================================ polyconic projections ================================

internal sealed class Bon() : Projection("BON")
{
    private double _theta1, _y0;

    protected override void Setup()
    {
        if (double.IsNaN(Pv[1])) throw new FitsException("The BON projection needs PVi_1 (theta_1).");
        _theta1 = Pv[1];
        if (_theta1 != 0.0) _y0 = R0 * Trig.Cos(_theta1) / Trig.Sin(_theta1) + _theta1;
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        if (_theta1 == 0.0)
        {
            (x, y) = (phi * Trig.Cos(theta), theta);
            return true;
        }
        double r = _y0 - theta;
        double alpha = r == 0.0 ? 0.0 : phi * Trig.Cos(theta) / r * Trig.R2D;
        x = r * Trig.Sin(alpha);
        y = _y0 - r * Trig.Cos(alpha);
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        if (_theta1 == 0.0)
        {
            theta = y;
            double c = Trig.Cos(y);
            phi = c == 0.0 ? 0.0 : x / c;
            return Math.Abs(y) <= 90.0 + 1e-12 && (c != 0.0 || x == 0.0);
        }
        double dy = _y0 - y;
        double r = Math.Sqrt(x * x + dy * dy);
        if (_theta1 < 0.0) r = -r;
        double alpha = r == 0.0 ? 0.0 : Trig.Atan2(x / r, dy / r);
        theta = _y0 - r;
        double cos = Trig.Cos(theta);
        phi = cos == 0.0 ? 0.0 : alpha * (r / R0) / cos;
        return Math.Abs(theta) <= 90.0 + 1e-12;
    }
}

internal sealed class Pco() : Projection("PCO")
{
    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        if (theta == 0.0)
        {
            (x, y) = (phi, 0.0);
            return true;
        }
        double cot = 1.0 / Trig.Tan(theta);
        if (Trig.Tan(theta) == 0.0)
        {
            (x, y) = (double.NaN, double.NaN);
            return false;
        }
        double e = phi * Trig.Sin(theta);
        x = R0 * cot * Trig.Sin(e);
        y = R0 * cot * (1.0 - Trig.Cos(e)) + theta;
        return true;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        const double tolerance = 1e-12;
        double w = Math.Abs(y);
        if (w < tolerance)
        {
            (phi, theta) = (x, 0.0);
            return true;
        }
        if (Math.Abs(w - 90.0) < tolerance)
        {
            (phi, theta) = (0.0, Math.CopySign(90.0, y));
            return true;
        }
        double the, ymthe, tanthe;
        if (w < 1.0e-4)
        {
            // series for a point very close to the equator
            the = y / (1.0 + Trig.D2R / (2.0 * R0) * x * x);
            ymthe = y - the;
            tanthe = Trig.Tan(the);
        }
        else
        {
            double pos = y, neg = 0.0;
            double xx = x * x;
            double fpos = xx, fneg = -xx;
            the = pos;
            ymthe = tanthe = 0.0;
            for (int k = 0; k < 64; k++)
            {
                double lambda = fpos / (fpos - fneg);
                lambda = Math.Clamp(lambda, 0.1, 0.9);
                the = pos - lambda * (pos - neg);
                ymthe = y - the;
                tanthe = Trig.Tan(the);
                double f = xx + ymthe * (ymthe - 2.0 * R0 / tanthe);
                if (Math.Abs(f) < tolerance || Math.Abs(pos - neg) < tolerance) break;
                if (f > 0.0) (pos, fpos) = (the, f); else (neg, fneg) = (the, f);
            }
        }
        double x1 = R0 - ymthe * tanthe, y1 = x * tanthe;
        phi = x1 == 0.0 && y1 == 0.0 ? 0.0 : Trig.Atan2(y1, x1) / Trig.Sin(the);
        theta = the;
        return true;
    }
}

// ================================ quadrilateralised spherical cubes ================================

internal abstract class QuadCube(string code) : Projection(code)
{
    protected const double Scale = 45.0;

    /// <summary>Face of the cube a direction belongs to, and the face centre in the layout.</summary>
    protected static int Face(double l, double m, double n, out double zeta)
    {
        int face = 0;
        zeta = n;
        if (l > zeta) (face, zeta) = (1, l);
        if (m > zeta) (face, zeta) = (2, m);
        if (-l > zeta) (face, zeta) = (3, -l);
        if (-m > zeta) (face, zeta) = (4, -m);
        if (-n > zeta) (face, zeta) = (5, -n);
        return face;
    }

    protected static void FaceOrigin(int face, out double x0, out double y0)
    {
        (x0, y0) = face switch { 1 => (0.0, 0.0), 2 => (2.0, 0.0), 3 => (4.0, 0.0), 4 => (6.0, 0.0), 5 => (0.0, -2.0), _ => (0.0, 2.0) };
    }

    /// <summary>The face a plane position (in face units) belongs to; null when it is outside the layout.</summary>
    protected static int? FaceOf(ref double xf, ref double yf)
    {
        if (Math.Abs(xf) <= 1.0)
        {
            if (Math.Abs(yf) > 3.0) return null;
        }
        else if (Math.Abs(xf) > 7.0 || Math.Abs(yf) > 1.0)
        {
            return null;
        }
        if (xf < -1.0) xf += 8.0;
        if (xf > 5.0) { xf -= 6.0; return 4; }
        if (xf > 3.0) { xf -= 4.0; return 3; }
        if (xf > 1.0) { xf -= 2.0; return 2; }
        if (yf > 1.0) { yf -= 2.0; return 0; }
        if (yf < -1.0) { yf += 2.0; return 5; }
        return 1;
    }

    protected static void Direction(int face, double chi, double psi, double t, out double l, out double m, out double n)
    {
        switch (face)
        {
            case 1: l = t; m = chi * l; n = psi * l; break;
            case 2: m = t; l = -chi * m; n = psi * m; break;
            case 3: l = -t; m = chi * l; n = -psi * l; break;
            case 4: m = -t; l = -chi * m; n = -psi * m; break;
            case 5: n = -t; l = -psi * n; m = -chi * n; break;
            default: n = t; l = -psi * n; m = chi * n; break;
        }
    }
}

internal sealed class Tsc() : QuadCube("TSC")
{
    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double xf = x / Scale, yf = y / Scale;
        (phi, theta) = (double.NaN, double.NaN);
        if (FaceOf(ref xf, ref yf) is not { } face) return false;
        Direction(face, xf, yf, 1.0 / Math.Sqrt(1.0 + xf * xf + yf * yf), out double l, out double m, out double n);
        phi = l == 0.0 && m == 0.0 ? 0.0 : Trig.Atan2(m, l);
        theta = Trig.Asin(Math.Clamp(n, -1.0, 1.0));
        return true;
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double l = Trig.Cos(theta) * Trig.Cos(phi), m = Trig.Cos(theta) * Trig.Sin(phi), n = Trig.Sin(theta);
        int face = Face(l, m, n, out double zeta);
        (double xf, double yf) = face switch
        {
            1 => (m / zeta, n / zeta),
            2 => (-l / zeta, n / zeta),
            3 => (-m / zeta, n / zeta),
            4 => (l / zeta, n / zeta),
            5 => (m / zeta, l / zeta),
            _ => (m / zeta, -l / zeta),
        };
        FaceOrigin(face, out double x0, out double y0);
        xf = Math.Clamp(xf, -1.0, 1.0);
        yf = Math.Clamp(yf, -1.0, 1.0);
        x = Scale * (xf + x0);
        y = Scale * (yf + y0);
        return true;
    }
}

internal sealed class Csc() : QuadCube("CSC")
{
    // Polynomial approximation of the inverse of the cube mapping (Calabretta and Greisen 2002), in single precision as published.
    private const float P00 = -0.27292696f, P10 = -0.07629969f, P20 = -0.22797056f, P30 = 0.54852384f, P40 = -0.62930065f,
        P50 = 0.25795794f, P60 = 0.02584375f, P01 = -0.02819452f, P11 = -0.01471565f, P21 = 0.48051509f, P31 = -1.74114454f,
        P41 = 1.71547508f, P51 = -0.53022337f, P02 = 0.27058160f, P12 = -0.56800938f, P22 = 0.30803317f, P32 = 0.98938102f,
        P42 = -0.83180469f, P03 = -0.60441560f, P13 = 1.50880086f, P23 = -0.93678576f, P33 = 0.08693841f, P04 = 0.93412077f,
        P14 = -1.41601920f, P24 = 0.33887446f, P05 = -0.63915306f, P15 = 0.52032238f, P06 = 0.14381585f;

    private const float Gstar = 1.37484847732f, Mm = 0.004869491981f, Gamma = -0.13161671474f, Omega1 = -0.159596235474f,
        D0 = 0.0759196200467f, D1 = -0.0217762490699f, C00 = 0.141189631152f, C10 = 0.0809701286525f, C01 = -0.281528535557f,
        C11 = 0.15384112876f, C20 = -0.178251207466f, C02 = 0.106959469314f;

    private static float Poly(float u, float v)
    {
        float z0 = P00 + u * (P10 + u * (P20 + u * (P30 + u * (P40 + u * (P50 + u * P60)))));
        float z1 = P01 + u * (P11 + u * (P21 + u * (P31 + u * (P41 + u * P51))));
        float z2 = P02 + u * (P12 + u * (P22 + u * (P32 + u * P42)));
        float z3 = P03 + u * (P13 + u * (P23 + u * P33));
        float z4 = P04 + u * (P14 + u * P24);
        float z5 = P05 + u * P15;
        float z6 = P06;
        return z0 + v * (z1 + v * (z2 + v * (z3 + v * (z4 + v * (z5 + v * z6)))));
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double xd = x / Scale, yd = y / Scale;
        (phi, theta) = (double.NaN, double.NaN);
        if (FaceOf(ref xd, ref yd) is not { } face) return false;
        float xf = (float)xd, yf = (float)yd;
        float xx = xf * xf, yy = yf * yf;
        float chi = xf + xf * (1.0f - xx) * Poly(xx, yy);
        float psi = yf + yf * (1.0f - yy) * Poly(yy, xx);
        double t = 1.0 / Math.Sqrt((double)(chi * chi + psi * psi) + 1.0);
        Direction(face, chi, psi, t, out double l, out double m, out double n);
        phi = l == 0.0 && m == 0.0 ? 0.0 : Trig.Atan2(m, l);
        theta = Trig.Asin(Math.Clamp(n, -1.0, 1.0));
        return true;
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double l = Trig.Cos(theta) * Trig.Cos(phi), m = Trig.Cos(theta) * Trig.Sin(phi), n = Trig.Sin(theta);
        int face = Face(l, m, n, out double zeta);
        (double xi, double eta) = face switch
        {
            1 => (m, n),
            2 => (-l, n),
            3 => (-m, n),
            4 => (l, n),
            5 => (m, l),
            _ => (m, -l),
        };
        FaceOrigin(face, out double x0, out double y0);
        float chi = (float)(xi / zeta), psi = (float)(eta / zeta);
        float chi2 = chi * chi, psi2 = psi * psi;
        float chi2co = 1.0f - chi2, psi2co = 1.0f - psi2;
        float chipsi = (float)Math.Abs((double)(chi * psi));
        float chi4 = chi2 > 1.0e-16f ? chi2 * chi2 : 0.0f;
        float psi4 = psi2 > 1.0e-16f ? psi2 * psi2 : 0.0f;
        float chi2psi2 = chipsi > 1.0e-16f ? chi2 * psi2 : 0.0f;
        float xf = chi * (chi2 + chi2co * (Gstar + psi2 * (Gamma * chi2co + Mm * chi2 +
                  psi2co * (C00 + C10 * chi2 + C01 * psi2 + C11 * chi2psi2 + C20 * chi4 + C02 * psi4)) +
                  chi2 * (Omega1 - chi2co * (D0 + D1 * chi2))));
        float yf = psi * (psi2 + psi2co * (Gstar + chi2 * (Gamma * psi2co + Mm * psi2 +
                  chi2co * (C00 + C10 * psi2 + C01 * chi2 + C11 * chi2psi2 + C20 * psi4 + C02 * chi4)) +
                  psi2 * (Omega1 - psi2co * (D0 + D1 * psi2))));
        double xs = Math.Clamp((double)xf, -1.0, 1.0), ys = Math.Clamp((double)yf, -1.0, 1.0);
        x = Scale * (xs + x0);
        y = Scale * (ys + y0);
        return true;
    }
}

internal sealed class Qsc() : QuadCube("QSC")
{
    private static readonly double Sqrt2Inv = 1.0 / Math.Sqrt(2.0);

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double xf = x / Scale, yf = y / Scale;
        (phi, theta) = (double.NaN, double.NaN);
        if (FaceOf(ref xf, ref yf) is not { } face) return false;

        double omega, tau, zeta, zeco, w;
        bool direct = Math.Abs(xf) > Math.Abs(yf);
        double major = direct ? xf : yf, minor = direct ? yf : xf;
        if (major == 0.0)
        {
            omega = 0.0;
            tau = 1.0;
            zeta = 1.0;
            zeco = 0.0;
        }
        else
        {
            w = 15.0 * minor / major;
            omega = Trig.Sin(w) / (Trig.Cos(w) - Sqrt2Inv);
            tau = 1.0 + omega * omega;
            zeco = major * major * (1.0 - 1.0 / Math.Sqrt(1.0 + tau));
            zeta = 1.0 - zeco;
        }
        if (zeta < -1.0)
        {
            if (zeta < -1.0 - 1e-12) return false;
            zeta = -1.0;
            zeco = 2.0;
            w = 0.0;
        }
        else
        {
            w = Math.Sqrt(zeco * (2.0 - zeco) / tau);
        }

        double l, m, n;
        switch (face)
        {
            case 1:
                l = zeta;
                if (direct) { m = xf < 0.0 ? -w : w; n = m * omega; }
                else { n = yf < 0.0 ? -w : w; m = n * omega; }
                break;
            case 2:
                m = zeta;
                if (direct) { l = xf > 0.0 ? -w : w; n = -l * omega; }
                else { n = yf < 0.0 ? -w : w; l = -n * omega; }
                break;
            case 3:
                l = -zeta;
                if (direct) { m = xf > 0.0 ? -w : w; n = -m * omega; }
                else { n = yf < 0.0 ? -w : w; m = -n * omega; }
                break;
            case 4:
                m = -zeta;
                if (direct) { l = xf < 0.0 ? -w : w; n = l * omega; }
                else { n = yf < 0.0 ? -w : w; l = n * omega; }
                break;
            case 5:
                n = -zeta;
                if (direct) { m = xf < 0.0 ? -w : w; l = m * omega; }
                else { l = yf < 0.0 ? -w : w; m = l * omega; }
                break;
            default:
                n = zeta;
                if (direct) { m = xf < 0.0 ? -w : w; l = -m * omega; }
                else { l = yf > 0.0 ? -w : w; m = -l * omega; }
                break;
        }
        phi = l == 0.0 && m == 0.0 ? 0.0 : Trig.Atan2(m, l);
        theta = Trig.Asin(Math.Clamp(n, -1.0, 1.0));
        return true;
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        if (Math.Abs(theta) == 90.0)
        {
            x = 0.0;
            y = Math.CopySign(2.0 * Scale, theta);
            return true;
        }
        double sinPhi = Trig.Sin(phi), cosPhi = Trig.Cos(phi);
        double l = Trig.Cos(theta) * cosPhi, m = Trig.Cos(theta) * sinPhi, n = Trig.Sin(theta);
        int face = Face(l, m, n, out double zeta);
        double zeco = 1.0 - zeta;
        double xi, eta;
        double t = theta * Trig.D2R;
        double p = Math.Atan2(sinPhi, cosPhi);
        switch (face)
        {
            case 1:
                (xi, eta) = (m, n);
                if (zeco < 1e-8) zeco = (p * p + t * t) / 2.0;
                break;
            case 2:
                (xi, eta) = (-l, n);
                if (zeco < 1e-8) { p -= Math.PI / 2.0; zeco = (p * p + t * t) / 2.0; }
                break;
            case 3:
                (xi, eta) = (-m, n);
                if (zeco < 1e-8) { p -= Math.CopySign(Math.PI, p); zeco = (p * p + t * t) / 2.0; }
                break;
            case 4:
                (xi, eta) = (l, n);
                if (zeco < 1e-8) { p += Math.PI / 2.0; zeco = (p * p + t * t) / 2.0; }
                break;
            case 5:
                (xi, eta) = (m, l);
                if (zeco < 1e-8) { t = (theta + 90.0) * Trig.D2R; zeco = t * t / 2.0; }
                break;
            default:
                (xi, eta) = (m, -l);
                if (zeco < 1e-8) { t = (90.0 - theta) * Trig.D2R; zeco = t * t / 2.0; }
                break;
        }
        FaceOrigin(face, out double x0, out double y0);

        double xf = 0.0, yf = 0.0;
        if (xi != 0.0 || eta != 0.0)
        {
            double omega, tau;
            if (-xi > Math.Abs(eta))
            {
                omega = eta / xi;
                tau = 1.0 + omega * omega;
                xf = -Math.Sqrt(zeco / (1.0 - 1.0 / Math.Sqrt(1.0 + tau)));
                yf = xf / 15.0 * (Trig.Atan(omega) - Trig.Asin(omega / Math.Sqrt(tau + tau)));
            }
            else if (xi > Math.Abs(eta))
            {
                omega = eta / xi;
                tau = 1.0 + omega * omega;
                xf = Math.Sqrt(zeco / (1.0 - 1.0 / Math.Sqrt(1.0 + tau)));
                yf = xf / 15.0 * (Trig.Atan(omega) - Trig.Asin(omega / Math.Sqrt(tau + tau)));
            }
            else if (-eta >= Math.Abs(xi))
            {
                omega = xi / eta;
                tau = 1.0 + omega * omega;
                yf = -Math.Sqrt(zeco / (1.0 - 1.0 / Math.Sqrt(1.0 + tau)));
                xf = yf / 15.0 * (Trig.Atan(omega) - Trig.Asin(omega / Math.Sqrt(tau + tau)));
            }
            else if (eta >= Math.Abs(xi))
            {
                omega = xi / eta;
                tau = 1.0 + omega * omega;
                yf = Math.Sqrt(zeco / (1.0 - 1.0 / Math.Sqrt(1.0 + tau)));
                xf = yf / 15.0 * (Trig.Atan(omega) - Trig.Asin(omega / Math.Sqrt(tau + tau)));
            }
        }
        xf = Math.Clamp(xf, -1.0, 1.0);
        yf = Math.Clamp(yf, -1.0, 1.0);
        x = Scale * (xf + x0);
        y = Scale * (yf + y0);
        return true;
    }
}

// ================================ HEALPix ================================

internal sealed class Hpx() : Projection("HPX")
{
    private double _h, _k;
    private int _m, _n;
    private double _equatorial, _w3, _w4, _w5, _w6, _w7;

    protected override void Setup()
    {
        _h = P(1, 4.0);
        _k = P(2, 3.0);
        if (_h <= 0.0 || _k <= 0.0) throw new FitsException("Invalid parameters for the HPX projection.");
        _m = (int)(_h + 0.5) % 2;
        _n = (int)(_k + 0.5) % 2;
        _equatorial = (_k - 1.0) / _k;
        _w3 = 90.0 * _k / _h;
        _w4 = (_k + 1.0) / 2.0;
        _w5 = 90.0 * (_k - 1.0) / _h;
        _w6 = 180.0 / _h;
        _w7 = _h / 360.0;
    }

    protected override bool InverseCore(double x, double y, out double phi, out double theta)
    {
        double s = x;
        double t = -180.0 + (2.0 * Math.Floor((x + 180.0) * _w7) + 1.0) * _w6;
        t = x - t;
        double absy = Math.Abs(y);
        (phi, theta) = (double.NaN, double.NaN);
        if (absy <= _w5)
        {
            theta = Trig.Asin(y / _w3);
            phi = s;
            return true;
        }
        if (absy > _w6 * _w4) return false;
        bool offset = !(_n == 1 || y > 0.0);
        double sigma = _w4 - absy / _w6;
        double u, v;
        if (sigma == 0.0)
        {
            u = 1e9;
            v = 90.0;
        }
        else
        {
            v = 1.0 - sigma * sigma / _k;
            if (v < -1.0) return false;
            u = 1.0 / sigma;
            v = Trig.Asin(v);
        }
        if (y < 0.0) v = -v;
        if (offset)
        {
            int h = (int)Math.Floor(s / _w6) + _m;
            t += h % 2 != 0 ? -_w6 : _w6;
        }
        double r = u * t;
        if (Math.Abs(r) >= _w6 + 1e-12) return false;
        if (r != 0.0) r -= t;
        phi = s + r;
        theta = v;
        return true;
    }

    protected override bool ForwardCore(double phi, double theta, out double x, out double y)
    {
        double xi = phi;
        double t = -180.0 + (2.0 * Math.Floor((phi + 180.0) * _w7) + 1.0) * _w6;
        t = phi - t;
        double sin = Trig.Sin(theta), abs = Math.Abs(sin);
        if (abs <= _equatorial)
        {
            x = xi;
            y = _w3 * sin;
            return true;
        }
        bool offset = !(_n == 1 || theta > 0.0);
        double sigma = Math.Sqrt(_k * (1.0 - abs));
        double scale = sigma - 1.0;
        double eta = _w6 * (_w4 - sigma);
        if (theta < 0.0) eta = -eta;
        if (offset)
        {
            int h = (int)Math.Floor(xi / _w6) + _m;
            t += h % 2 != 0 ? -_w6 : _w6;
        }
        x = xi + t * scale;
        y = eta;
        if (180.0 < x) x = 360.0 - x;
        return true;
    }
}
