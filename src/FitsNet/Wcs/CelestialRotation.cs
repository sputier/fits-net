namespace FitsNet.Wcs;

/// <summary>
/// The spherical rotation between native coordinates (phi, theta) and celestial coordinates (alpha, delta), fixed by the celestial
/// position of the fiducial point and by LONPOLE / LATPOLE (standard section 8.2, Calabretta and Greisen 2002 section 2).
/// </summary>
internal sealed class CelestialRotation
{
    private const double Tolerance = 1e-10;

    private readonly double _lngPole;   // celestial longitude of the native pole
    private readonly double _phiPole;   // native longitude of the celestial pole
    private readonly double _sinLatPole, _cosLatPole;

    public CelestialRotation(double lng0, double lat0, double? lonPole, double? latPole, double phi0, double theta0)
    {
        double phip = lonPole ?? Wrap180((lat0 < theta0 ? 180.0 : 0.0) + phi0);
        double latp = latPole ?? 90.0;
        double lngp;

        if (theta0 == 90.0)
        {
            lngp = lng0;
            latp = lat0;
        }
        else
        {
            double slat0 = Trig.Sin(lat0), clat0 = Trig.Cos(lat0);
            double sthe0 = Trig.Sin(theta0), cthe0 = Trig.Cos(theta0);
            double u, v, sphip;
            bool specified = false; // LATPOLE selects nothing: it is the answer
            if (phip == phi0)
            {
                sphip = 0.0;
                u = theta0;
                v = 90.0 - lat0;
            }
            else
            {
                sphip = Trig.Sin(phip - phi0);
                double x = cthe0 * Trig.Cos(phip - phi0), y = sthe0;
                double z = Math.Sqrt(x * x + y * y);
                if (z == 0.0)
                {
                    if (slat0 != 0.0) throw new FitsException("Invalid celestial description: LAT0 must be 0 when |LONPOLE - phi0| = 90 and theta0 = 0.");
                    specified = true;
                    latp = Math.Clamp(latp, -90.0, 90.0);
                    u = v = 0.0;
                }
                else
                {
                    double slz = slat0 / z;
                    if (Math.Abs(slz) > 1.0)
                    {
                        if (Math.Abs(slz) - 1.0 >= Tolerance)
                            throw new FitsException($"Invalid celestial description: |LAT0| <= {Trig.Asin(z):F3} is required for this LONPOLE and fiducial point.");
                        slz = Math.Sign(slz);
                    }
                    u = Trig.Atan2(y, x);
                    v = Trig.Acos(slz);
                }
            }

            if (!specified)
            {
                double latp1 = Wrap180(u + v), latp2 = Wrap180(u - v);
                bool ok1 = Math.Abs(latp1) < 90.0 + Tolerance, ok2 = Math.Abs(latp2) < 90.0 + Tolerance;
                if (Math.Abs(latp - latp1) < Math.Abs(latp - latp2)) latp = ok1 ? latp1 : latp2;
                else latp = ok2 ? latp2 : latp1;
                if (Math.Abs(latp) < 90.0 + Tolerance) latp = Math.Clamp(latp, -90.0, 90.0);
            }

            double zz = Trig.Cos(latp) * clat0;
            if (Math.Abs(zz) < Tolerance)
            {
                if (Math.Abs(clat0) < Tolerance) lngp = lng0;
                else if (latp > 0.0) lngp = lng0 + phip - phi0 - 180.0;
                else lngp = lng0 - phip + phi0;
            }
            else
            {
                double x = (sthe0 - Trig.Sin(latp) * slat0) / zz;
                double y = sphip * cthe0 / clat0;
                if (x == 0.0 && y == 0.0) throw new FitsException("Invalid celestial transformation parameters.");
                lngp = lng0 - Trig.Atan2(y, x);
            }
            if (lng0 >= 0.0)
            {
                if (lngp < 0.0) lngp += 360.0;
                else if (lngp > 360.0) lngp -= 360.0;
            }
            else
            {
                if (lngp > 0.0) lngp -= 360.0;
                else if (lngp < -360.0) lngp += 360.0;
            }
        }
        if (Math.Abs(latp) > 90.0 + Tolerance)
            throw new FitsException("Ill-conditioned celestial transformation: no valid LATPOLE for this LONPOLE and fiducial point.");

        _lngPole = lngp;
        _phiPole = phip;
        _sinLatPole = Trig.Sin(latp);
        _cosLatPole = Trig.Cos(latp);
        LngPole = lngp;
        LatPole = latp;
        PhiPole = phip;
        PositiveLongitudes = lng0 >= 0.0;
    }

    /// <summary>Celestial longitude of the native pole (the first Euler angle).</summary>
    public double LngPole { get; }

    /// <summary>Celestial latitude of the native pole: LATPOLE once the ambiguity is resolved.</summary>
    public double LatPole { get; }

    /// <summary>Native longitude of the celestial pole: LONPOLE.</summary>
    public double PhiPole { get; }

    private bool PositiveLongitudes { get; }

    private static double Wrap180(double angle)
    {
        if (angle < -180.0) return angle + 360.0;
        if (angle > 180.0) return angle - 360.0;
        return angle;
    }

    /// <summary>Native (phi, theta) to celestial (alpha, delta).</summary>
    public void ToCelestial(double phi, double theta, out double lng, out double lat)
    {
        double dphi = phi - _phiPole;
        double sinT = Trig.Sin(theta), cosT = Trig.Cos(theta);
        double cosD = Trig.Cos(dphi), sinD = Trig.Sin(dphi);
        double y = -cosT * sinD;
        double x = sinT * _cosLatPole - cosT * _sinLatPole * cosD;
        double sinLat = sinT * _sinLatPole + cosT * _cosLatPole * cosD;
        lat = Trig.Atan2(sinLat, Math.Sqrt(x * x + y * y));
        double dlng = x == 0.0 && y == 0.0 ? dphi - 180.0 : Trig.Atan2(y, x);
        lng = _lngPole + dlng;
        if (PositiveLongitudes)
        {
            lng %= 360.0;
            if (lng < 0.0) lng += 360.0;
        }
        else
        {
            lng %= 360.0;
            if (lng > 0.0) lng -= 360.0;
        }
    }

    /// <summary>Celestial (alpha, delta) to native (phi, theta).</summary>
    public void ToNative(double lng, double lat, out double phi, out double theta)
    {
        double dlng = lng - _lngPole;
        double sinD = Trig.Sin(lat), cosD = Trig.Cos(lat);
        double cosA = Trig.Cos(dlng), sinA = Trig.Sin(dlng);
        double y = -cosD * sinA;
        double x = sinD * _cosLatPole - cosD * _sinLatPole * cosA;
        double sinT = sinD * _sinLatPole + cosD * _cosLatPole * cosA;
        theta = Trig.Atan2(sinT, Math.Sqrt(x * x + y * y));
        double dphi = x == 0.0 && y == 0.0 ? dlng - 180.0 : Trig.Atan2(y, x);
        phi = _phiPole + dphi;
        phi %= 360.0;
        if (phi > 180.0) phi -= 360.0;
        else if (phi < -180.0) phi += 360.0;
    }
}
