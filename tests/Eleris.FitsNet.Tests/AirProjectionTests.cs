using Eleris.FitsNet.Wcs;

namespace Eleris.FitsNet.Tests;

/// <summary>The inverse of the AIR projection is a root search: it must land where a plain bisection does.</summary>
public class AirProjectionTests
{
    private static double[] Parameters(double thetaB)
    {
        var pv = new double[30];
        Array.Fill(pv, double.NaN);
        pv[1] = thetaB;
        return pv;
    }

    [Theory]
    [InlineData(90.0)]
    [InlineData(60.0)]
    [InlineData(45.0)]
    [InlineData(0.0)]
    [InlineData(-45.0)]
    public void The_inverse_undoes_the_projection_over_the_whole_hemisphere(double thetaB)
    {
        var air = Projection.Create("AIR", Parameters(thetaB), null, null);
        int checkedPoints = 0;
        for (double theta = -89.0; theta <= 90.0; theta += 0.37)
        {
            for (double phi = -180.0; phi <= 180.0; phi += 37.0)
            {
                if (!air.Forward(phi, theta, out double x, out double y)) continue;
                Assert.True(air.Inverse(x, y, out double phiBack, out double thetaBack), $"theta {theta}");
                // the angle on the sphere, which does not depend on the azimuth where phi is not defined (the pole)
                Assert.Equal(theta, thetaBack, 1e-9);
                if (theta < 90.0) Assert.Equal(0.0, (phi - phiBack + 540.0) % 360.0 - 180.0, 1e-9);   // equal modulo 360 degrees
                checkedPoints++;
            }
        }
        Assert.True(checkedPoints > 1000);
    }

    [Fact]
    public void Points_beyond_the_edge_have_no_inverse_and_the_centre_is_the_pole()
    {
        var air = Projection.Create("AIR", Parameters(90.0), null, null);
        Assert.True(air.Inverse(0, 0, out _, out double theta));
        Assert.Equal(90.0, theta, 1e-12);
        Assert.False(air.Inverse(1e15, 1e15, out _, out _));   // the projection reaches very far, but not beyond the opposite pole
    }

    [Theory]
    [InlineData(90.0)]
    [InlineData(-88.0)]
    public void Points_a_hair_from_the_pole_come_back_where_a_bisection_puts_them(double thetaB)
    {
        var air = Projection.Create("AIR", Parameters(thetaB), null, null);
        foreach (double d in new[] { 1e-9, 1e-8, 1e-7, 1e-6, 1e-5, 1e-4, 1e-3, 1e-2, 1e-1, 1.0 })
        {
            double theta = 90.0 - 2.0 * d;
            Assert.True(air.Forward(30.0, theta, out double x, out double y));
            Assert.True(air.Inverse(x, y, out _, out double back));

            // the plain bisection on the radius that the forward projection gives
            double target = Math.Sqrt(x * x + y * y);
            double lo = 0.0, hi = 90.0 - 1e-9;
            for (int k = 0; k < 200; k++)
            {
                double mid = 0.5 * (lo + hi);
                Assert.True(air.Forward(0.0, 90.0 - 2.0 * mid, out double mx, out double my));
                if (Math.Sqrt(mx * mx + my * my) < target) lo = mid; else hi = mid;
                if (hi - lo < 1e-14) break;
            }
            Assert.Equal(90.0 - 2.0 * 0.5 * (lo + hi), back, 1e-9);   // the two searches stop at different places within the noise of the radius (below 1e-10 degree)
        }
    }
}
