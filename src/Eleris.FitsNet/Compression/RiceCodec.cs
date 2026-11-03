using System.Numerics;

namespace Eleris.FitsNet.Compression;

/// <summary>The Rice algorithm of the tile compression convention (<c>RICE_1</c>), 1, 2 or 4 byte pixels.</summary>
internal static class RiceCodec
{
    private static (int FsBits, int FsMax) Parameters(int bytePix) => bytePix switch
    {
        1 => (3, 6),
        2 => (4, 14),
        4 => (5, 25),
        _ => throw new FitsException($"Rice compression supports BYTEPIX 1, 2 or 4, not {bytePix}."),
    };

    private static uint Mask(int bytePix) => bytePix == 4 ? uint.MaxValue : (1u << (8 * bytePix)) - 1;

    /// <summary>
    /// Decodes <paramref name="count"/> pixels. Two-byte pixels come back as signed 16-bit values, one-byte pixels as 0 to 255 and
    /// four-byte pixels as signed 32-bit values.
    /// </summary>
    public static int[] Decode(byte[] c, int count, int blockSize, int bytePix)
    {
        var result = new int[count];
        Decode(c, result, blockSize, bytePix);
        return result;
    }

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

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static byte Next(byte[] c, ref int pos)
    {
        if ((uint)pos >= (uint)c.Length) ThrowTruncated();
        return c[pos++];
    }

    private static void ThrowTruncated() => throw new FitsException("The Rice compressed data is truncated.");

    /// <summary>Undoes the zigzag mapping of signed differences.</summary>
    private static uint Unmap(uint diff) => (diff & 1) == 0 ? diff >> 1 : ~(diff >> 1);

    /// <summary>Encodes pixels (only the low <paramref name="bytePix"/> bytes of each value are used).</summary>
    public static byte[] Encode(ReadOnlySpan<int> pixels, int blockSize, int bytePix)
    {
        var (fsbits, fsmax) = Parameters(bytePix);
        if (blockSize < 1) throw new FitsException("The Rice block size must be positive.");
        int bbits = 1 << fsbits;
        uint mask = Mask(bytePix);
        int shift = 32 - 8 * bytePix;
        if (pixels.Length == 0) return [];
        var w = new BitWriter(Math.Min(pixels.Length * bytePix / 2 + 64, 1 << 26));

        uint lastpix = (uint)pixels[0] & mask;
        for (int k = bytePix - 1; k >= 0; k--) w.WriteByte((byte)(lastpix >> (8 * k)));

        var diff = new uint[Math.Min(blockSize, pixels.Length)];
        for (int i = 0; i < pixels.Length; i += blockSize)
        {
            int n = Math.Min(blockSize, pixels.Length - i);
            double pixelsum = 0;
            for (int j = 0; j < n; j++)
            {
                uint next = (uint)pixels[i + j] & mask;
                // signed difference within the pixel width, then zigzag
                int pdiff = shift == 0 ? unchecked((int)(next - lastpix)) : unchecked((int)((next - lastpix) << shift)) >> shift;
                diff[j] = unchecked((uint)(pdiff < 0 ? ~(pdiff << 1) : pdiff << 1)) & mask;
                pixelsum += diff[j];
                lastpix = next;
            }

            double dpsum = (pixelsum - (n / 2) - 1) / n;
            if (dpsum < 0) dpsum = 0;
            uint psum = ((uint)dpsum) >> 1;
            int fs = 0;
            while (psum > 0)
            {
                fs++;
                psum >>= 1;
            }

            if (fs >= fsmax)
            {
                w.WriteBits((uint)(fsmax + 1), fsbits);
                for (int j = 0; j < n; j++) w.WriteBits(diff[j], bbits);
            }
            else if (fs == 0 && pixelsum == 0)
            {
                w.WriteBits(0, fsbits);
            }
            else
            {
                w.WriteBits((uint)(fs + 1), fsbits);
                uint fsmask = (1u << fs) - 1;
                for (int j = 0; j < n; j++)
                {
                    uint v = diff[j];
                    w.WriteUnary((int)(v >> fs));
                    if (fs > 0) w.WriteBits(v & fsmask, fs);
                }
            }
        }
        return w.ToArray();
    }
}

/// <summary>MSB-first bit writer with a 64-bit accumulator and a growing byte buffer.</summary>
internal sealed class BitWriter
{
    private byte[] _buffer;
    private int _length;
    private ulong _acc;   // the pending bits, right aligned
    private int _pending; // number of pending bits (0-7 between calls)

    public BitWriter(int capacity = 1024) => _buffer = new byte[Math.Max(16, capacity)];

    private void Grow(int needed)
    {
        long size = Math.Max((long)_buffer.Length * 2, needed);
        Array.Resize(ref _buffer, (int)Math.Min(size, Array.MaxLength));
    }

    private void Append(byte b)
    {
        if (_length == _buffer.Length) Grow(_length + 1);
        _buffer[_length++] = b;
    }

    /// <summary>Writes a whole byte; the writer must be on a byte boundary.</summary>
    public void WriteByte(byte value)
    {
        if (_pending != 0) throw new InvalidOperationException("Bytes can only be written on a byte boundary.");
        Append(value);
    }

    /// <summary>Writes whole bytes; the writer must be on a byte boundary.</summary>
    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        if (_pending != 0) throw new InvalidOperationException("Bytes can only be written on a byte boundary.");
        if (_length + bytes.Length > _buffer.Length) Grow(_length + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
    }

    public void WriteBit(bool one) => WriteBits(one ? 1u : 0u, 1);

    /// <summary>Writes the low <paramref name="count"/> bits of <paramref name="value"/> (count 0-32).</summary>
    public void WriteBits(uint value, int count)
    {
        if (count == 0) return;
        _acc = (_acc << count) | (value & (uint)((1UL << count) - 1));
        _pending += count;
        while (_pending >= 8)
        {
            _pending -= 8;
            Append((byte)(_acc >> _pending));
        }
        _acc &= (1UL << _pending) - 1;
    }

    /// <summary>Writes <paramref name="zeros"/> zero bits followed by a one bit.</summary>
    public void WriteUnary(int zeros)
    {
        while (zeros >= 32)
        {
            WriteBits(0, 32);
            zeros -= 32;
        }
        WriteBits(1, zeros + 1);
    }

    /// <summary>Pads the current byte with zero bits.</summary>
    public void AlignToByte()
    {
        if (_pending > 0) WriteBits(0, 8 - _pending);
    }

    public byte[] ToArray()
    {
        var result = new byte[_length + (_pending > 0 ? 1 : 0)];
        Array.Copy(_buffer, result, _length);
        if (_pending > 0) result[^1] = (byte)(_acc << (8 - _pending));
        return result;
    }
}
