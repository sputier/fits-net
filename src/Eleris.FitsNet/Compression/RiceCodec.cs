using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

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
        int sign = bytePix == 2 ? 16 : 0;   // two-byte pixels come back signed: the shift pair extends the sign

        var reader = new BitReader(c);
        uint lastpix = reader.Read(8 * bytePix);

        for (int i = 0; i < count;)
        {
            int fs = (int)reader.Read(fsbits) - 1;
            if (fs > fsmax) throw new FitsException("The Rice compressed data is corrupt.");   // no encoder writes it (4-byte pixels: the code has room for more)
            int imax = (int)Math.Min((long)i + blockSize, count);
            if (fs < 0)
            {
                // low entropy: all differences are zero
                result[i..imax].Fill(unchecked((int)(lastpix << sign)) >> sign);
                i = imax;
            }
            else if (fs == fsmax)
            {
                // high entropy: differences stored directly in bbits bits
                for (; i < imax; i++)
                {
                    lastpix = (Unmap(reader.Read(bbits)) + lastpix) & mask;
                    result[i] = unchecked((int)(lastpix << sign)) >> sign;
                }
            }
            else
            {
                // normal case: unary high part, fs low bits
                for (; i < imax; i++)
                {
                    lastpix = (Unmap(reader.ReadRice(fs)) + lastpix) & mask;
                    result[i] = unchecked((int)(lastpix << sign)) >> sign;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTruncated() => throw new FitsException("The Rice compressed data is truncated.");

    /// <summary>Undoes the zigzag mapping of signed differences (without a branch: the sign of a difference is not predictable).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Unmap(uint diff) => (diff >> 1) ^ (uint)-(int)(diff & 1);

    /// <summary>
    /// MSB-first bits read through a 64-bit window that is refilled eight bytes at a time. The bits of the window below the valid ones
    /// are either zero or the next bits of the data (a refill adds the same bits again, which changes nothing).
    /// </summary>
    private struct BitReader
    {
        private readonly byte[] _data;
        private int _pos;
        private ulong _acc;
        private int _bits;   // the valid bits at the top of the window

        public BitReader(byte[] data)
        {
            _data = data;
            _pos = 0;
            _acc = 0;
            _bits = 0;
        }

        /// <summary>Tops the window up to 56 to 63 bits, or to the end of the data: never 64, so that no shift is by the whole window.</summary>
        private void Refill()
        {
            if (_pos <= _data.Length - 8)
            {
                _acc |= BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(_pos)) >> _bits;
                int bytes = (63 - _bits) >> 3;
                _pos += bytes;
                _bits += bytes << 3;
            }
            else
            {
                for (; _bits <= 55 && _pos < _data.Length; _bits += 8) _acc |= (ulong)_data[_pos++] << (56 - _bits);
            }
        }

        /// <summary>Reads <paramref name="count"/> bits (0 to 32).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Read(int count)
        {
            if (_bits < count)
            {
                Refill();
                if (_bits < count) ThrowTruncated();
            }
            uint value = (uint)((_acc >> 1) >> (63 - count));   // two shifts: a count of 0 must give 0
            _acc <<= count;
            _bits -= count;
            return value;
        }

        /// <summary>
        /// Reads a Rice code: zeros up to a one (their number is the high part of the value) then <paramref name="fs"/> bits (the low part).
        /// When the whole code is in the window, it is taken with one shift.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadRice(int fs)
        {
            if (_bits < 48) Refill();
            ulong window = _acc;
            int zeros = BitOperations.LeadingZeroCount(window);
            int total = zeros + 1 + fs;
            if (total <= _bits)
            {
                _acc = window << total;
                _bits -= total;
                return ((uint)zeros << fs) | (uint)(((window << (zeros + 1)) >> 1) >> (63 - fs));
            }
            return ((uint)ReadUnary() << fs) | Read(fs);
        }

        /// <summary>Reads zeros up to a one, and returns the number of zeros.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadUnary()
        {
            if (_bits < 32) Refill();
            int zeros = BitOperations.LeadingZeroCount(_acc);
            if (zeros < _bits)
            {
                _acc <<= zeros + 1;
                _bits -= zeros + 1;
                return zeros;
            }
            return ReadLongUnary();
        }

        /// <summary>A run of zeros that goes beyond the bits of the window (or the end of the data).</summary>
        private int ReadLongUnary()
        {
            int zeros = 0;
            while (true)
            {
                zeros += _bits;
                _acc <<= _bits;   // a shift by 0 to 63
                _bits = 0;
                if (zeros > 1 << 28) throw new FitsException("The Rice compressed data is corrupt.");
                Refill();
                if (_bits == 0) ThrowTruncated();
                int run = BitOperations.LeadingZeroCount(_acc);
                if (run < _bits)
                {
                    _acc <<= run + 1;
                    _bits -= run + 1;
                    return zeros + run;
                }
            }
        }
    }

    /// <summary>Encodes pixels (only the low <paramref name="bytePix"/> bytes of each value are used).</summary>
    public static byte[] Encode(ReadOnlySpan<int> pixels, int blockSize, int bytePix)
    {
        var (fsbits, fsmax) = Parameters(bytePix);
        if (blockSize < 1) throw new FitsException("The Rice block size must be positive.");
        int bbits = 1 << fsbits;
        uint mask = Mask(bytePix);
        int shift = 32 - 8 * bytePix;
        if (pixels.Length == 0) return [];

        int blockLength = Math.Min(blockSize, pixels.Length);
        uint[]? rented = null;
        var sink = default(BitSink);
        try
        {
            sink = new BitSink((int)Math.Min((long)pixels.Length * bytePix / 2 + 64, 1 << 26));
            if (blockLength > 256) rented = ArrayPool<uint>.Shared.Rent(blockLength);
            Span<uint> diff = rented is null ? stackalloc uint[blockLength] : rented.AsSpan(0, blockLength);
            uint lastpix = (uint)pixels[0] & mask;
            sink.Write(lastpix, 8 * bytePix);

            for (int i = 0; i < pixels.Length; i += blockSize)
            {
                int n = Math.Min(blockSize, pixels.Length - i);
                ReadOnlySpan<int> block = pixels.Slice(i, n);
                ulong sum = 0;
                if (shift == 0)
                {
                    for (int j = 0; j < block.Length; j++)
                    {
                        uint next = (uint)block[j];
                        int d = unchecked((int)(next - lastpix));
                        uint zigzag = unchecked((uint)((d << 1) ^ (d >> 31)));   // the signed difference mapped to 0, 1, 2, ... for 0, -1, 1, ...
                        diff[j] = zigzag;
                        sum += zigzag;
                        lastpix = next;
                    }
                }
                else
                {
                    for (int j = 0; j < block.Length; j++)
                    {
                        uint next = (uint)block[j] & mask;
                        int d = unchecked((int)((next - lastpix) << shift)) >> shift;   // the difference within the width of a pixel
                        uint zigzag = unchecked((uint)((d << 1) ^ (d >> 31))) & mask;
                        diff[j] = zigzag;
                        sum += zigzag;
                        lastpix = next;
                    }
                }

                // cfitsio sums the differences in a double: the same value as long as the sum is exact in a double (below 2^53)
                double pixelsum = n <= 1 << 20 ? sum : SumInDouble(diff[..n]);
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
                    sink.Reserve(((long)fsbits + (long)n * bbits + 7) / 8 + 8);
                    sink.Write((uint)(fsmax + 1), fsbits);
                    for (int j = 0; j < n; j++) sink.Write(diff[j], bbits);
                }
                else if (fs == 0 && pixelsum == 0)
                {
                    sink.Reserve(fsbits / 8 + 9);
                    sink.Write(0, fsbits);
                }
                else
                {
                    // fs + 1 bits for each value, plus the quotients of the unary parts, whose total is at most the sum shifted by fs
                    sink.Reserve(((long)fsbits + (long)n * (fs + 1) + (long)(sum >> fs) + 7) / 8 + 8);
                    sink.Write((uint)(fs + 1), fsbits);
                    uint high = 1u << fs, lowMask = high - 1;
                    for (int j = 0; j < n; j++)
                    {
                        uint v = diff[j];
                        uint quotient = v >> fs;
                        // the unary code of the quotient (that many zeros and a one) then the fs low bits are one number of that many bits
                        if (quotient <= (uint)(31 - fs)) sink.Write(high | (v & lowMask), (int)quotient + 1 + fs);
                        else sink.WriteLong(quotient, fs, v & lowMask);
                    }
                }
            }
            return sink.ToArray();
        }
        finally
        {
            sink.Release();
            if (rented is not null) ArrayPool<uint>.Shared.Return(rented);
        }
    }

    private static double SumInDouble(ReadOnlySpan<uint> values)
    {
        double sum = 0;
        foreach (uint v in values) sum += v;
        return sum;
    }

    /// <summary>
    /// MSB-first bits written through a 64-bit accumulator, four bytes at a time, into a pooled buffer that the caller has made big enough
    /// (<see cref="Reserve"/>) for what it writes before the next reservation.
    /// </summary>
    private struct BitSink
    {
        private byte[] _buffer;
        private int _length;
        private ulong _acc;   // the pending bits, at the top
        private int _bits;    // how many (0 to 31 between calls)

        public BitSink(int capacity) => _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(64, capacity));

        /// <summary>Makes room for <paramref name="bytes"/> more bytes.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reserve(long bytes)
        {
            if (_length + bytes > _buffer.Length) Grow(_length + bytes);
        }

        private void Grow(long needed)
        {
            if (needed > Array.MaxLength) throw new OutOfMemoryException("The Rice compressed tile is too large.");
            byte[] bigger = ArrayPool<byte>.Shared.Rent((int)Math.Max(needed, Math.Min((long)_buffer.Length * 2, Array.MaxLength)));
            Buffer.BlockCopy(_buffer, 0, bigger, 0, _length);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = bigger;
        }

        /// <summary>Writes the low <paramref name="count"/> bits (1 to 32) of <paramref name="code"/>, which has no other bit set.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(uint code, int count)
        {
            _acc |= (ulong)code << (64 - _bits - count);
            _bits += count;
            if (_bits >= 32)
            {
                BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(_length), (uint)(_acc >> 32));
                _length += 4;
                _acc <<= 32;
                _bits -= 32;
            }
        }

        /// <summary>A quotient of 8 or more with its low bits can need more than 32 bits: the zeros by 32, then the one and the low bits.</summary>
        public void WriteLong(uint zeros, int lowBits, uint low)
        {
            for (; zeros >= 32; zeros -= 32) Write(0, 32);
            if (zeros > 0) Write(0, (int)zeros);
            Write((1u << lowBits) | low, lowBits + 1);
        }

        public byte[] ToArray()
        {
            int tail = (_bits + 7) / 8;
            var result = new byte[_length + tail];
            Buffer.BlockCopy(_buffer, 0, result, 0, _length);
            for (int i = 0; i < tail; i++) result[_length + i] = (byte)(_acc >> (56 - 8 * i));
            return result;
        }

        public void Release()
        {
            if (_buffer is not null) ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null!;
        }
    }
}

/// <summary>MSB-first bit writer: a 64-bit accumulator that is written four bytes at a time into a growing byte buffer.</summary>
internal sealed class BitWriter
{
    private byte[] _buffer;
    private int _length;
    private ulong _acc;   // the pending bits, at the top
    private int _bits;    // how many (0 to 31 between calls)

    public BitWriter(int capacity = 1024) => _buffer = new byte[Math.Max(16, capacity)];

    private void Grow(int needed)
    {
        long size = Math.Max((long)_buffer.Length * 2, needed);
        Array.Resize(ref _buffer, (int)Math.Min(size, Array.MaxLength));
    }

    /// <summary>Writes the whole bytes of the accumulator, so that the buffer holds everything but the last bits (fewer than 8).</summary>
    private void FlushBytes()
    {
        int bytes = _bits >> 3;
        if (_length + bytes > _buffer.Length) Grow(_length + bytes);
        for (int i = 0; i < bytes; i++) _buffer[_length++] = (byte)(_acc >> (56 - 8 * i));
        _acc <<= bytes << 3;
        _bits &= 7;
    }

    /// <summary>Writes a whole byte; the writer must be on a byte boundary.</summary>
    public void WriteByte(byte value)
    {
        if ((_bits & 7) != 0) throw new InvalidOperationException("Bytes can only be written on a byte boundary.");
        WriteBits(value, 8);
    }

    /// <summary>Writes whole bytes; the writer must be on a byte boundary.</summary>
    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        if ((_bits & 7) != 0) throw new InvalidOperationException("Bytes can only be written on a byte boundary.");
        FlushBytes();
        if (_length + bytes.Length > _buffer.Length) Grow(_length + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
    }

    public void WriteBit(bool one) => WriteBits(one ? 1u : 0u, 1);

    /// <summary>Writes the low <paramref name="count"/> bits of <paramref name="value"/> (count 0-32).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void WriteBits(uint value, int count)
    {
        if (count == 0) return;
        _acc |= (ulong)(value & (uint)((1UL << count) - 1)) << (64 - _bits - count);
        _bits += count;
        if (_bits >= 32)
        {
            if (_length + 4 > _buffer.Length) Grow(_length + 4);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(_length), (uint)(_acc >> 32));
            _length += 4;
            _acc <<= 32;
            _bits -= 32;
        }
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
        if ((_bits & 7) != 0) WriteBits(0, 8 - (_bits & 7));
    }

    public byte[] ToArray()
    {
        int tail = (_bits + 7) >> 3;
        var result = new byte[_length + tail];
        Array.Copy(_buffer, result, _length);
        for (int i = 0; i < tail; i++) result[_length + i] = (byte)(_acc >> (56 - 8 * i));
        return result;
    }
}
