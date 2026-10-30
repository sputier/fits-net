using System.Text.Json;
using FitsNet.Wcs;

namespace FitsNet.Tests;

/// <summary>World coordinate systems (section 8) against the reference implementation (wcslib through astropy).</summary>
public class WcsTests
{
    private sealed record Point(double[] Pix, double?[] World, double?[]? Back);

    private sealed record Probe(double[] World, double?[] Pix);

    private sealed record Case(string Name, char Alternate, FitsHeader Header, Point[] Points, Probe[] Probes);

    private static readonly Lazy<Dictionary<string, Case>> Cases = new(Load);

    private static Dictionary<string, Case> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "wcs_vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, Case>();
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            var header = new FitsHeader();
            foreach (var card in c.GetProperty("cards").EnumerateArray())
            {
                string key = card[0].GetString()!;
                var v = card[1];
                object value = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString()!,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => v.TryGetInt64(out long l) ? l : v.GetDouble(),
                };
                header.Set(key, value);
            }
            var points = c.GetProperty("points").EnumerateArray().Select(p => new Point(
                p.GetProperty("pix").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                p.GetProperty("world").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? (double?)null : x.GetDouble()).ToArray(),
                p.TryGetProperty("back", out var b) && b.ValueKind == JsonValueKind.Array
                    ? b.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? (double?)null : x.GetDouble()).ToArray()
                    : null)).ToArray();
            string name = c.GetProperty("name").GetString()!;
            char alt = c.TryGetProperty("alternate", out var a) ? a.GetString()![0] : ' ';
            var probes = c.TryGetProperty("worlds", out var wl)
                ? wl.EnumerateArray().Select(p => new Probe(p.GetProperty("world").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                    p.GetProperty("pix").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? (double?)null : x.GetDouble()).ToArray())).ToArray()
                : [];
            result[name] = new Case(name, alt, header, points, probes);
        }
        return result;
    }

    public static IEnumerable<object[]> CaseNames() => Cases.Value.Keys.Select(k => new object[] { k });

    /// <summary>Angle between two sky positions in degrees.</summary>
    private static double Separation(double lon1, double lat1, double lon2, double lat2)
    {
        double d2r = Math.PI / 180;
        double dlon = (lon1 - lon2) * d2r;
        double s1 = Math.Sin(lat1 * d2r), c1 = Math.Cos(lat1 * d2r), s2 = Math.Sin(lat2 * d2r), c2 = Math.Cos(lat2 * d2r);
        double y = Math.Sqrt(Math.Pow(c2 * Math.Sin(dlon), 2) + Math.Pow(c1 * s2 - s1 * c2 * Math.Cos(dlon), 2));
        double x = s1 * s2 + c1 * c2 * Math.Cos(dlon);
        return Math.Atan2(y, x) / d2r;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Pixel_to_world_matches_the_reference(string name)
    {
        var c = Cases.Value[name];
        var wcs = WorldCoordinateSystem.FromHeader(c.Header, c.Alternate);
        int bad = 0;
        string first = "";
        // CSC is a single precision polynomial: wcslib evaluates it in float, and the last digits depend on the compiler
        double tolerance = name.StartsWith("CSC", StringComparison.Ordinal) ? 3e-5 : 2e-7;
        foreach (var p in c.Points)
        {
            double[] ours = wcs.PixelToWorld(p.Pix);
            bool expectedNaN = p.World.Any(v => v is null);
            bool ok;
            if (expectedNaN)
            {
                ok = ours.Any(double.IsNaN);
            }
            else if (wcs.IsCelestial)
            {
                ok = Separation(ours[wcs.LongitudeAxis], ours[wcs.LatitudeAxis], p.World[wcs.LongitudeAxis]!.Value, p.World[wcs.LatitudeAxis]!.Value) < tolerance;
                for (int i = 0; i < ours.Length && ok; i++)
                    if (i != wcs.LongitudeAxis && i != wcs.LatitudeAxis)
                        ok = Math.Abs(ours[i] - p.World[i]!.Value) <= 1e-9 * Math.Max(1, Math.Abs(p.World[i]!.Value));
            }
            else
            {
                ok = true;
                for (int i = 0; i < ours.Length; i++) ok &= Math.Abs(ours[i] - p.World[i]!.Value) <= 1e-9 * Math.Max(1, Math.Abs(p.World[i]!.Value));
            }
            if (!ok && bad++ == 0)
                first = $"pix [{string.Join(", ", p.Pix)}]: expected [{string.Join(", ", p.World.Select(v => v?.ToString() ?? "NaN"))}] got [{string.Join(", ", ours)}]";
        }
        Assert.True(bad == 0, $"{bad} of {c.Points.Length} points differ; first: {first}");
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void World_to_pixel_matches_the_reference(string name)
    {
        var c = Cases.Value[name];
        var wcs = WorldCoordinateSystem.FromHeader(c.Header, c.Alternate);
        int bad = 0, compared = 0;
        string first = "";
        foreach (var p in c.Points)
        {
            if (p.Back is null || p.World.Any(v => v is null)) continue;
            double[] world = p.World.Select(v => v!.Value).ToArray();
            double[] ours = wcs.WorldToPixel(world);
            compared++;
            bool expectedNaN = p.Back.Any(v => v is null);
            bool ok = expectedNaN
                ? ours.Any(double.IsNaN)
                : ours.Select((v, i) => Math.Abs(v - p.Back[i]!.Value) <= 2e-6 * Math.Max(1, Math.Abs(p.Back[i]!.Value))).All(t => t);
            if (!ok && bad++ == 0)
                first = $"world [{string.Join(", ", world)}] expected pix [{string.Join(", ", p.Back.Select(v => v?.ToString() ?? "NaN"))}] got [{string.Join(", ", ours)}]";
        }
        Assert.True(bad == 0, $"{bad} of {compared} positions differ; first: {first}");
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void World_to_pixel_of_independent_positions_matches_the_reference(string name)
    {
        var c = Cases.Value[name];
        if (c.Probes.Length == 0) return;
        var wcs = WorldCoordinateSystem.FromHeader(c.Header, c.Alternate);
        int bad = 0;
        string first = "";
        foreach (var probe in c.Probes)
        {
            double[] ours = wcs.WorldToPixel(probe.World);
            // a point at the horizon of a perspective projection goes to infinity: huge or NaN are the same answer
            bool expectedNaN = probe.Pix.Any(v => v is null || Math.Abs(v.Value) > 1e12);
            bool ok = expectedNaN
                ? ours.Any(v => double.IsNaN(v) || Math.Abs(v) > 1e9)
                : ours.Select((v, i) => Math.Abs(v - probe.Pix[i]!.Value) <= 5e-6 * Math.Max(1, Math.Abs(probe.Pix[i]!.Value))).All(t => t);
            if (!ok && bad++ == 0)
                first = $"world [{string.Join(", ", probe.World)}] expected [{string.Join(", ", probe.Pix.Select(v => v?.ToString() ?? "NaN"))}] got [{string.Join(", ", ours)}]";
        }
        Assert.True(bad == 0, $"{bad} of {c.Probes.Length} positions differ; first: {first}");
    }
}
