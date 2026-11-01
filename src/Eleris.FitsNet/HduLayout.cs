namespace Eleris.FitsNet;

/// <summary>The data layout described by the mandatory keywords of an HDU header.</summary>
internal sealed record HduLayout(
    BitPix BitPix, long[] Axes, long ParameterCount, long GroupCount, bool IsRandomGroups, string? Xtension, long DataLength)
{
    public const int MaxAxes = 999;

    /// <summary>
    /// Product of the axes used for the data array. For random groups axis 1 (which is 0) is excluded, so a group
    /// described by NAXIS = 1 alone holds PCOUNT parameters and one data element (cfitsio treats NAXIS1 as 1 there).
    /// </summary>
    public long ElementsPerGroup { get; init; }

    public static HduLayout Parse(FitsHeader header, bool primary, bool strict = false)
    {
        string? xtension = null;
        if (primary)
        {
            if (!header.Contains("SIMPLE")) throw new FitsException("Primary header must start with SIMPLE.");
        }
        else
        {
            xtension = header.Get<string>("XTENSION") ?? throw new FitsException("Extension header must start with XTENSION.");
        }

        long bitpix = Require(header, "BITPIX", strict);
        if (!BitPixExtensions.IsValid(bitpix)) throw new FitsException($"Invalid BITPIX {bitpix}.");
        long naxis = Require(header, "NAXIS", strict);
        if (naxis is < 0 or > MaxAxes) throw new FitsException($"Invalid NAXIS {naxis}.");

        var axes = new long[naxis];
        for (int i = 0; i < naxis; i++)
        {
            axes[i] = Require(header, "NAXIS" + (i + 1), strict);
            if (axes[i] < 0) throw new FitsException($"NAXIS{i + 1} is negative.");
        }

        if (strict) CheckOrder(header, primary, (int)naxis);

        bool groups = primary && naxis >= 1 && axes[0] == 0 && header.Get<bool>("GROUPS");
        long pcount = 0, gcount = 1;
        if (!primary || groups)
        {
            pcount = Require(header, "PCOUNT", strict);
            gcount = Require(header, "GCOUNT", strict);
            if (pcount < 0 || gcount < 0) throw new FitsException("PCOUNT and GCOUNT must not be negative.");
        }

        if (xtension is "IMAGE" or "IUEIMAGE" && (pcount != 0 || gcount != 1))
            throw new FitsException($"An {xtension} extension requires PCOUNT = 0 and GCOUNT = 1.");

        if (xtension is "TABLE" or "BINTABLE" or "A3DTABLE" && (bitpix != 8 || naxis != 2 || gcount != 1))
            throw new FitsException($"A {xtension} extension requires BITPIX = 8, NAXIS = 2 and GCOUNT = 1.");
        if (xtension == "TABLE" && pcount != 0) throw new FitsException("An ASCII TABLE extension requires PCOUNT = 0.");

        int first = groups ? 1 : 0;
        long elements = 0;
        if (naxis > 0)
        {
            // A zero-length axis makes the array empty whatever the other axes are; check it before multiplying.
            bool empty = false;
            for (int i = first; i < naxis; i++) empty |= axes[i] == 0;
            elements = empty ? 0 : 1;
            try
            {
                if (!empty)
                    for (int i = first; i < naxis; i++) elements = checked(elements * axes[i]);
            }
            catch (OverflowException)
            {
                throw new FitsException("The data size described by the header overflows.");
            }
        }

        try
        {
            long length = checked(Math.Abs(bitpix) / 8 * gcount * checked(pcount + elements));
            return new HduLayout((BitPix)bitpix, axes, pcount, gcount, groups, xtension, length) { ElementsPerGroup = elements };
        }
        catch (OverflowException)
        {
            throw new FitsException("The data size described by the header overflows.");
        }
    }

    /// <summary>Rounds up to a whole number of 2880-byte blocks.</summary>
    public static long Padded(long length)
    {
        if (length < 0) throw new FitsException("A data size cannot be negative.");
        long rest = length % FitsHeader.BlockSize;
        if (rest == 0) return length;
        long fill = FitsHeader.BlockSize - rest;
        if (length > long.MaxValue - fill) throw new FitsException("The data size described by the header overflows.");
        return length + fill;
    }

    private static long Require(FitsHeader header, string keyword, bool strict)
    {
        var card = header.Find(keyword);
        if (strict && card is not null && card.Value is not long)
            throw new FitsException($"{keyword} must be an integer.");
        return header.TryGetValue(keyword, out long v) ? v : throw new FitsException($"Mandatory keyword {keyword} is missing or not an integer.");
    }

    /// <summary>Mandatory keywords must come first and in order (standard 4.4.1).</summary>
    private static void CheckOrder(FitsHeader header, bool primary, int naxis)
    {
        string[] expected = [primary ? "SIMPLE" : "XTENSION", "BITPIX", "NAXIS"];
        for (int i = 0; i < expected.Length; i++)
            if (header.Count <= i || header[i].Keyword != expected[i])
                throw new FitsException($"Mandatory keyword {expected[i]} must be at card position {i + 1}.");
        for (int n = 1; n <= naxis; n++)
            if (header.Count <= 2 + n || header[2 + n].Keyword != "NAXIS" + n)
                throw new FitsException($"NAXIS{n} must be at card position {3 + n}.");
    }
}
