using System.Numerics;

namespace Eleris.FitsNet.Tests;

/// <summary>
/// The first decoder of the Rice algorithm, kept as it was before it was made faster (one byte at a time): the decoder of the library
/// must return the same pixels, and fail on the same data.
/// </summary>
internal static class ReferenceRiceDecoder
{
    private static (int FsBits, int FsMax) Parameters(int bytePix) => bytePix switch
    {
        1 => (3, 6),
        2 => (4, 14),
        4 => (5, 25),
        _ => throw new FitsException($"Rice compression supports BYTEPIX 1, 2 or 4, not {bytePix}."),
    };

    private static uint Mask(int bytePix) => bytePix == 4 ? uint.MaxValue : (1u << (8 * bytePix)) - 1;

    /// <summary>Decodes into <paramref name="result"/>, which holds as many pixels as the tile (its length is the pixel count).</summary>
    public static void Decode(byte[] c, Span<int> result, int blockSize, int bytePix)
    {
        var (fsbits, fsmax) = Parameters(bytePix);
        if (blockSize < 1) throw new FitsException("The Rice block size must be positive.");
        int bbits = 1 << fsbits;
        uint mask = Mask(bytePix);
        int count = result.Length;
        if (count == 0) return;
        bool signed16 = bytePix == 2;

        int pos = 0;

        uint lastpix = 0;
        for (int k = 0; k < bytePix; k++) lastpix = (lastpix << 8) | Next(c, ref pos);
        uint b = Next(c, ref pos);
        int nbits = 8;

        for (int i = 0; i < count;)
        {
            nbits -= fsbits;
            while (nbits < 0)
            {
                b = (b << 8) | Next(c, ref pos);
                nbits += 8;
            }
            int fs = (int)(b >> nbits) - 1;
            // the one difference with the first decoder, on purpose: a code that no encoder writes (it only has room in 4-byte pixels) is refused,
            // where the first decoder returned pixels that depended on the overflow of its 32-bit register
            if (fs > fsmax) throw new FitsException("The Rice compressed data is corrupt.");
            b &= (1u << nbits) - 1;

            int imax = (int)Math.Min((long)i + blockSize, count);
            if (fs < 0)
            {
                // low entropy: all differences are zero
                for (; i < imax; i++) result[i] = signed16 ? (short)lastpix : (int)lastpix;
            }
            else if (fs == fsmax)
            {
                // high entropy: differences stored directly in bbits bits
                for (; i < imax; i++)
                {
                    int k = bbits - nbits;
                    uint diff = k >= 32 ? 0 : b << k;
                    for (k -= 8; k >= 0; k -= 8)
                    {
                        b = Next(c, ref pos);
                        diff |= b << k;
                    }
                    if (nbits > 0)
                    {
                        b = Next(c, ref pos);
                        diff |= b >> -k;
                        b &= (1u << nbits) - 1;
                    }
                    else
                    {
                        b = 0;
                    }
                    diff = Unmap(diff);
                    lastpix = (diff + lastpix) & mask;
                    result[i] = signed16 ? (short)lastpix : (int)lastpix;
                }
            }
            else
            {
                // normal case: unary high part, fs low bits
                for (; i < imax; i++)
                {
                    while (b == 0)
                    {
                        nbits += 8;
                        if (nbits > 1 << 28) throw new FitsException("The Rice compressed data is corrupt.");
                        b = Next(c, ref pos);
                    }
                    int nzero = nbits - (32 - BitOperations.LeadingZeroCount(b));
                    nbits -= nzero + 1;
                    b ^= 1u << nbits;
                    nbits -= fs;
                    while (nbits < 0)
                    {
                        b = (b << 8) | Next(c, ref pos);
                        nbits += 8;
                    }
                    uint diff = ((uint)nzero << fs) | (b >> nbits);
                    b &= (1u << nbits) - 1;
                    diff = Unmap(diff);
                    lastpix = (diff + lastpix) & mask;
                    result[i] = signed16 ? (short)lastpix : (int)lastpix;
                }
            }
        }
    }

    private static byte Next(byte[] c, ref int pos)
    {
        if ((uint)pos >= (uint)c.Length) ThrowTruncated();
        return c[pos++];
    }

    private static void ThrowTruncated() => throw new FitsException("The Rice compressed data is truncated.");

    /// <summary>Undoes the zigzag mapping of signed differences.</summary>
    private static uint Unmap(uint diff) => (diff & 1) == 0 ? diff >> 1 : ~(diff >> 1);
}
