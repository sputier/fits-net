using System.Globalization;

namespace Eleris.FitsNet.Wcs;

/// <summary>
/// The Simple Imaging Polynomial convention (Shupe et al. 2005): polynomials <c>A_p_q</c> and <c>B_p_q</c> of the pixel offsets
/// <c>u = x - CRPIX1</c>, <c>v = y - CRPIX2</c> that are added to them before the linear transformation. The inverse polynomials
/// <c>AP_p_q</c> and <c>BP_p_q</c> are only a first approximation of the inverse here: it is solved exactly, so that
/// <see cref="WorldCoordinateSystem.WorldToPixel(ReadOnlySpan{double})"/> undoes <see cref="WorldCoordinateSystem.PixelToWorld(ReadOnlySpan{double})"/>.
/// </summary>
internal sealed class SipDistortion
{
    /// <summary>The highest order accepted: the standard convention stops at 9, and a header must not make the library loop for long.</summary>
    private const int MaxOrder = 20;

    private readonly Term[] _a, _b, _ap, _bp;

    private readonly record struct Term(int P, int Q, double Coefficient);

    private SipDistortion(Term[] a, Term[] b, Term[] ap, Term[] bp) => (_a, _b, _ap, _bp) = (a, b, ap, bp);

    /// <summary>Reads the coefficients of a header; null when it has none (no <c>A_ORDER</c> and no <c>B_ORDER</c>).</summary>
    public static SipDistortion? TryRead(FitsHeader h)
    {
        bool hasA = h.Contains("A_ORDER"), hasB = h.Contains("B_ORDER");
        if (!hasA && !hasB) return null;
        if (hasA != hasB) throw new FitsException("The SIP distortion needs both A_ORDER and B_ORDER.");
        Term[] a = ReadTerms(h, "A"), b = ReadTerms(h, "B");
        // the inverse polynomials are optional, but come as a pair
        bool hasAp = h.Contains("AP_ORDER"), hasBp = h.Contains("BP_ORDER");
        if (hasAp != hasBp) throw new FitsException("The SIP inverse polynomials need both AP_ORDER and BP_ORDER.");
        Term[] ap = hasAp ? ReadTerms(h, "AP") : [], bp = hasBp ? ReadTerms(h, "BP") : [];
        return new SipDistortion(a, b, ap, bp);
    }

    private static Term[] ReadTerms(FitsHeader h, string name)
    {
        if (!h.TryGetValue(name + "_ORDER", out int order) || order is < 0 or > MaxOrder)
            throw new FitsException($"{name}_ORDER must be an integer between 0 and {MaxOrder}.");
        var terms = new List<Term>();
        for (int p = 0; p <= order; p++)
            for (int q = 0; p + q <= order; q++)
                if (h.TryGetValue($"{name}_{p}_{q}", out double c) && c != 0.0)
                {
                    if (!double.IsFinite(c)) throw new FitsException($"{name}_{p}_{q} is not a finite number.");
                    terms.Add(new Term(p, q, c));
                }
        return [.. terms];
    }

    /// <summary>The polynomial and its two partial derivatives at (<paramref name="u"/>, <paramref name="v"/>).</summary>
    private static double Evaluate(Term[] terms, double u, double v, out double du, out double dv)
    {
        double sum = 0.0;
        du = dv = 0.0;
        foreach (var (p, q, c) in terms)
        {
            double up = Pow(u, p), vq = Pow(v, q);
            sum += c * up * vq;
            if (p > 0) du += c * p * Pow(u, p - 1) * vq;
            if (q > 0) dv += c * q * up * Pow(v, q - 1);
        }
        return sum;
    }

    private static double Pow(double x, int n)
    {
        double r = 1.0;
        for (int i = 0; i < n; i++) r *= x;
        return r;
    }

    /// <summary>The distorted offsets (u + f(u, v), v + g(u, v)).</summary>
    public void Apply(double u, double v, out double distortedU, out double distortedV)
    {
        distortedU = u + Evaluate(_a, u, v, out _, out _);
        distortedV = v + Evaluate(_b, u, v, out _, out _);
    }

    /// <summary>The offsets (u, v) whose distorted offsets are (<paramref name="distortedU"/>, <paramref name="distortedV"/>); false when the search does not converge.</summary>
    public bool Remove(double distortedU, double distortedV, out double u, out double v)
    {
        u = distortedU;
        v = distortedV;
        if (!double.IsFinite(u) || !double.IsFinite(v)) return false;
        if (_ap.Length > 0)
        {
            // the polynomial inverse of the file is a good start: it usually needs one or two steps
            u = distortedU + Evaluate(_ap, distortedU, distortedV, out _, out _);
            v = distortedV + Evaluate(_bp, distortedU, distortedV, out _, out _);
        }
        for (int step = 0; step < 50; step++)
        {
            double f = Evaluate(_a, u, v, out double fu, out double fv);
            double g = Evaluate(_b, u, v, out double gu, out double gv);
            double e1 = u + f - distortedU, e2 = v + g - distortedV;
            double a = 1.0 + fu, b = fv, c = gu, d = 1.0 + gv;
            double det = a * d - b * c;
            if (det == 0.0 || !double.IsFinite(det)) return false;
            double stepU = (d * e1 - b * e2) / det, stepV = (a * e2 - c * e1) / det;
            u -= stepU;
            v -= stepV;
            if (!double.IsFinite(u) || !double.IsFinite(v)) return false;
            // relative to the size of the offsets: far from the reference pixel a double cannot hold an absolute 1e-12 any more
            double tolerance = 1e-12 * Math.Max(1.0, Math.Max(Math.Abs(u), Math.Abs(v)));
            if (Math.Abs(stepU) <= tolerance && Math.Abs(stepV) <= tolerance) return true;
        }
        return false;
    }
}

/// <summary>
/// The TPV convention (SCAMP, SExtractor): the tangent plane coordinates (xi, eta) are polynomials of the intermediate world
/// coordinates (x, y), written as <c>PV1_0</c> to <c>PV1_39</c> for xi and <c>PV2_0</c> to <c>PV2_39</c> for eta, with the terms in the
/// order of SCAMP: 1, x, y, r, x^2, xy, y^2, x^3, ..., r^3, ... up to the seventh degree. The terms of eta have x and y swapped. A
/// coefficient that is not in the header is zero. The projection is the tangent (gnomonic) one.
/// </summary>
internal sealed class TpvDistortion
{
    private const int TermCount = 40;

    // the exponents of x, y and r of each term: the r terms are the third, the twelfth, the twenty-fourth and the fortieth
    private static readonly (int X, int Y, int R)[] Powers = BuildPowers();

    private readonly double[] _xi = new double[TermCount], _eta = new double[TermCount];

    private TpvDistortion() { }

    private static (int X, int Y, int R)[] BuildPowers()
    {
        var powers = new (int, int, int)[TermCount];
        int k = 0;
        for (int degree = 0; degree <= 7; degree++)
        {
            for (int y = 0; y <= degree; y++) powers[k++] = (degree - y, y, 0);
            if (degree % 2 == 1) powers[k++] = (0, 0, degree);   // r, r^3, r^5 and r^7 follow the terms of their degree
        }
        return powers;
    }

    /// <summary>Reads the coefficients of the two celestial axes (the ones that are not in the header are zero).</summary>
    public static TpvDistortion Read(FitsHeader h, int longitudeAxis, int latitudeAxis, string alternate)
    {
        var t = new TpvDistortion();
        for (int m = 0; m < TermCount; m++)
        {
            if (h.TryGetValue($"PV{longitudeAxis + 1}_{m}{alternate}", out double a)) t._xi[m] = Finite(a, $"PV{longitudeAxis + 1}_{m}{alternate}");
            if (h.TryGetValue($"PV{latitudeAxis + 1}_{m}{alternate}", out double b)) t._eta[m] = Finite(b, $"PV{latitudeAxis + 1}_{m}{alternate}");
        }
        for (int axis = 0; axis < 2; axis++)
            for (int m = TermCount; m < 100; m++)
                if (h.Contains($"PV{(axis == 0 ? longitudeAxis : latitudeAxis) + 1}_{m}{alternate}"))
                    throw new FitsException($"PV{(axis == 0 ? longitudeAxis : latitudeAxis) + 1}_{m}{alternate}: the TPV convention defines the terms 0 to {TermCount - 1}.");
        return t;
    }

    private static double Finite(double value, string keyword) =>
        double.IsFinite(value) ? value : throw new FitsException($"{keyword} is not a finite number.");

    /// <summary>The polynomial with coefficients <paramref name="c"/> in the arguments (<paramref name="x"/>, <paramref name="y"/>) and its partial derivatives.</summary>
    private static double Evaluate(double[] c, double x, double y, out double dx, out double dy)
    {
        double r = Math.Sqrt(x * x + y * y);
        double inverseR = r > 0.0 ? 1.0 / r : 0.0;   // the terms in r are not differentiable at the origin: their slope is taken as zero there
        double sum = 0.0;
        dx = dy = 0.0;
        for (int k = 0; k < TermCount; k++)
        {
            if (c[k] == 0.0) continue;
            var (a, b, n) = Powers[k];
            double xa = Pow(x, a), yb = Pow(y, b), rn = Pow(r, n);
            sum += c[k] * xa * yb * rn;
            if (a > 0) dx += c[k] * a * Pow(x, a - 1) * yb * rn;
            if (b > 0) dy += c[k] * b * xa * Pow(y, b - 1) * rn;
            if (n > 0)
            {
                // d r^n / d x = n r^(n-2) x, and the same for y
                double slope = n * Pow(r, n - 1) * inverseR;
                dx += c[k] * xa * yb * slope * x;
                dy += c[k] * xa * yb * slope * y;
            }
        }
        return sum;
    }

    private static double Pow(double x, int n)
    {
        double r = 1.0;
        for (int i = 0; i < n; i++) r *= x;
        return r;
    }

    /// <summary>The tangent plane coordinates (xi, eta) of the intermediate world coordinates (x, y), all in degrees.</summary>
    public void Apply(double x, double y, out double xi, out double eta)
    {
        xi = Evaluate(_xi, x, y, out _, out _);
        eta = Evaluate(_eta, y, x, out _, out _);   // x and y swapped
    }

    /// <summary>The intermediate world coordinates of a point of the tangent plane; false when the search does not converge.</summary>
    public bool Remove(double xi, double eta, out double x, out double y)
    {
        x = xi;
        y = eta;
        if (!double.IsFinite(xi) || !double.IsFinite(eta)) return false;
        for (int step = 0; step < 100; step++)
        {
            double f = Evaluate(_xi, x, y, out double fx, out double fy);
            // eta(x, y) is the polynomial of (y, x): its first argument is y
            double g = Evaluate(_eta, y, x, out double gy, out double gx);
            double e1 = f - xi, e2 = g - eta;
            double det = fx * gy - fy * gx;
            if (det == 0.0 || !double.IsFinite(det)) return false;
            double stepX = (gy * e1 - fy * e2) / det, stepY = (fx * e2 - gx * e1) / det;
            x -= stepX;
            y -= stepY;
            if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
            // the angles are in degrees: 1e-13 degree is a few tenths of a micro-arcsecond (relative once the values are large)
            double tolerance = 1e-13 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
            if (Math.Abs(stepX) <= tolerance && Math.Abs(stepY) <= tolerance) return true;
        }
        return false;
    }
}
