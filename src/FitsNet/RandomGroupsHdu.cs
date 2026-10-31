using System.Runtime.InteropServices;

namespace FitsNet;

/// <summary>
/// The random groups structure (primary HDU with <c>GROUPS = T</c> and <c>NAXIS1 = 0</c>): a sequence of groups,
/// each made of PCOUNT parameters followed by a data array.
/// </summary>
public sealed class RandomGroupsHdu : Hdu
{
    internal RandomGroupsHdu(FitsHeader header, DataSource source) : base(header, source) { }

    internal override bool CanBePrimary => true;

    internal override HduLayout GetLayout(bool primary) => HduLayout.Parse(Header, primary);

    private HduLayout Layout => GetLayout(true);

    /// <summary>The data representation (<c>BITPIX</c>).</summary>
    public BitPix BitPix => Layout.BitPix;

    /// <summary>The number of groups (<c>GCOUNT</c>).</summary>
    public long GroupCount => Layout.GroupCount;

    /// <summary>The number of parameters in each group (<c>PCOUNT</c>).</summary>
    public long ParameterCount => Layout.ParameterCount;

    /// <summary>NAXIS2..NAXISn: the shape of the data array of each group.</summary>
    public IReadOnlyList<long> DataAxes => Layout.Axes.AsSpan(1).ToArray();

    /// <summary>Number of data elements in each group (excluding parameters).</summary>
    public long ElementsPerGroup => Layout.ElementsPerGroup;

    /// <summary>The PTYPEn name of parameter <paramref name="index"/> (0-based), if declared.</summary>
    /// <param name="index">The zero-based index of the parameter.</param>
    public string? ParameterName(int index) => Header.Get<string>("PTYPE" + (index + 1));

    internal override void Normalize(bool primary, bool hasExtensions)
    {
        if (!primary) throw new FitsException("The random groups structure is only allowed in the primary HDU.");
        var l = Layout;
        var axes = l.Axes;
        RebuildMandatory(Header, true, null, l.BitPix, axes, l.ParameterCount, l.GroupCount, true, hasExtensions);
    }

    /// <summary>
    /// Creates a random groups HDU. <paramref name="values"/> holds, for each group, its parameters followed by its data.
    /// Supported types: byte, short, int, long, float, double.
    /// </summary>
    /// <typeparam name="T">The value type: <c>byte</c>, <c>short</c>, <c>int</c>, <c>long</c>, <c>float</c> or <c>double</c>.</typeparam>
    /// <param name="values">All the values: for each group its parameters, then its data array.</param>
    /// <param name="groupCount">The number of groups.</param>
    /// <param name="parameterCount">The number of parameters in each group.</param>
    /// <param name="dataAxes">The length of each axis of the data array of a group.</param>
    public static RandomGroupsHdu Create<T>(ReadOnlySpan<T> values, long groupCount, long parameterCount, params long[] dataAxes)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dataAxes);
        if (groupCount < 0 || parameterCount < 0) throw new ArgumentException("Group and parameter counts must not be negative.");
        if (parameterCount > 999) throw new ArgumentException("A random groups HDU has at most 999 parameters (PTYPEn, PSCALn and PZEROn keywords).", nameof(parameterCount));
        long elements = 1;
        foreach (long a in dataAxes)
            if (a < 0) throw new ArgumentException("Axis lengths must not be negative.", nameof(dataAxes));
        if (dataAxes.Contains(0)) elements = 0;
        else
        {
            try
            {
                foreach (long a in dataAxes) elements = checked(elements * a);
            }
            catch (OverflowException)
            {
                throw new ArgumentException("The data array of a group is too large.", nameof(dataAxes));
            }
        }
        long expectedValues;
        try
        {
            expectedValues = checked(groupCount * (parameterCount + elements));
        }
        catch (OverflowException)
        {
            throw new ArgumentException("The number of values announced by the group and parameter counts overflows.", nameof(groupCount));
        }
        if (values.Length != expectedValues)
            throw new ArgumentException("The number of values does not match groups x (parameters + data elements).", nameof(values));

        var axes = new long[dataAxes.Length + 1];
        dataAxes.CopyTo(axes, 1);
        // The unsigned conventions (BZERO for the data, PZEROn for every parameter) are supported like in images.
        byte[] raw = ImageHdu.Encode(values, out BitPix bitPix, out object? zero);
        var hdu = new RandomGroupsHdu(new FitsHeader(), new MemoryDataSource(raw));
        RebuildMandatory(hdu.Header, true, null, bitPix, axes, parameterCount, groupCount, true, false);
        if (zero is not null)
        {
            hdu.Header.Set("BZERO", zero);
            for (long i = 1; i <= parameterCount; i++) hdu.Header.Set("PZERO" + i, zero);
        }
        return hdu;
    }

    /// <summary>Reads the raw stored values of one group (parameters then data). <typeparamref name="T"/> must match BITPIX.</summary>
    /// <typeparam name="T">The type the stored values are returned as.</typeparam>
    /// <param name="group">The zero-based index of the group.</param>
    public T[] ReadRawGroup<T>(long group) where T : unmanaged
    {
        var layout = Layout;
        if (typeof(T) != layout.BitPix.NativeType())
            throw new InvalidOperationException($"BITPIX {(int)layout.BitPix} stores {layout.BitPix.NativeType().Name}, not {typeof(T).Name}.");
        return BigEndian.ToArray<T>(ReadGroupBytes(layout, group));
    }

    /// <summary>Physical parameter values of a group: PZEROn + PSCALn x stored value.</summary>
    /// <param name="group">The zero-based index of the group.</param>
    public double[] ReadParameters(long group)
    {
        var layout = Layout;
        byte[] groupBytes = ReadGroupBytes(layout, group); // checks the group index and the sizes first
        int paramBytes = (int)(layout.ParameterCount * layout.BitPix.BytesPerElement()); // at most groupBytes.Length
        double[] all = PixelConvert.ToDoubles(groupBytes.AsSpan(0, paramBytes), layout.BitPix, null);
        var result = new double[layout.ParameterCount];
        for (int i = 0; i < result.Length; i++)
        {
            double scale = Header.TryGetValue("PSCAL" + (i + 1), out double s) ? s : 1.0;
            double zero = Header.TryGetValue("PZERO" + (i + 1), out double z) ? z : 0.0;
            result[i] = zero + scale * all[i];
        }
        return result;
    }

    /// <summary>
    /// The parameters of a group by name (PTYPEn, case-insensitive). The standard says that parameters sharing a name are added
    /// together (e.g. a date split in two parts). Unnamed parameters are called <c>PARAMn</c>.
    /// </summary>
    /// <param name="group">The zero-based index of the group.</param>
    public IReadOnlyDictionary<string, double> ReadNamedParameters(long group)
    {
        double[] values = ReadParameters(group);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < values.Length; i++)
        {
            string name = ParameterName(i)?.Trim() is { Length: > 0 } n ? n : "PARAM" + (i + 1);
            result[name] = result.GetValueOrDefault(name) + values[i];
        }
        return result;
    }

    /// <summary>Physical data values of a group: BZERO + BSCALE x stored value (BLANK becomes NaN).</summary>
    /// <param name="group">The zero-based index of the group.</param>
    public double[] ReadData(long group)
    {
        var layout = Layout;
        long? blank = !layout.BitPix.IsFloatingPoint() && Header.TryGetValue("BLANK", out long b) ? b : null;
        byte[] bytes = ReadGroupBytes(layout, group);
        int skip = checked((int)layout.ParameterCount * layout.BitPix.BytesPerElement());
        double[] data = PixelConvert.ToDoubles(bytes.AsSpan(skip).ToArray(), layout.BitPix, blank);
        double scale = Header.TryGetValue("BSCALE", out double s) ? s : 1.0;
        double zero = Header.TryGetValue("BZERO", out double z) ? z : 0.0;
        if (scale != 1 || zero != 0)
            for (int i = 0; i < data.Length; i++) data[i] = zero + scale * data[i];
        return data;
    }

    private byte[] ReadGroupBytes(HduLayout layout, long group)
    {
        if (group < 0 || group >= layout.GroupCount) throw new ArgumentOutOfRangeException(nameof(group));
        long groupBytes = (layout.ParameterCount + layout.ElementsPerGroup) * layout.BitPix.BytesPerElement();
        if (groupBytes > int.MaxValue) throw new FitsException("A single group is larger than 2 GB.");
        return Source.ReadRange(group * groupBytes, (int)groupBytes);
    }
}
