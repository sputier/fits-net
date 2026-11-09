using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Eleris.FitsNet;

/// <summary>Outcome of checking one of the CHECKSUM / DATASUM keywords.</summary>
public enum ChecksumStatus
{
    /// <summary>The keyword is not present.</summary>
    Missing,

    /// <summary>The stored value matches the data.</summary>
    Valid,

    /// <summary>The stored value does not match (or cannot be parsed).</summary>
    Invalid,
}

/// <summary>Result of <see cref="Hdu.VerifyChecksum"/>.</summary>
/// <param name="Checksum">The CHECKSUM keyword, which covers header and data.</param>
/// <param name="DataSum">The DATASUM keyword, which covers the data unit only.</param>
public readonly record struct ChecksumResult(ChecksumStatus Checksum, ChecksumStatus DataSum);

/// <summary>What <see cref="FitsFile.Save(Stream, ChecksumPolicy)"/> does with CHECKSUM and DATASUM.</summary>
public enum ChecksumPolicy
{
    /// <summary>Recompute them in HDUs that already carry one of the two keywords; leave other HDUs alone.</summary>
    Auto,

    /// <summary>Write fresh CHECKSUM and DATASUM in every HDU.</summary>
    Always,

    /// <summary>Remove both keywords from every HDU.</summary>
    Never,
}

/// <summary>The 32-bit ones' complement checksum of the FITS standard (appendix J).</summary>
internal static class Checksum
{
    /// <summary>Folds the carries of a wide accumulator back in (end-around carry).</summary>
    public static uint Fold(ulong sum)
    {
        while ((sum >> 32) != 0) sum = (sum & 0xFFFFFFFF) + (sum >> 32);
        return (uint)sum;
    }

    public static uint Add(uint a, uint b) => Fold((ulong)a + b);

    /// <summary>Sums big-endian 32-bit words; a trailing partial word is zero-padded.</summary>
    public static uint Sum(ReadOnlySpan<byte> data)
    {
        int i = data.Length / 4 * 4;
        ulong sum = SumWords(data[..i]);
        if (i < data.Length)
        {
            Span<byte> tail = stackalloc byte[4];
            data[i..].CopyTo(tail);
            sum += BinaryPrimitives.ReadUInt32BigEndian(tail);
        }
        return Fold(sum);
    }

    // Words swapped and added per round: small enough to stay in the L1 cache between the two passes.
    private const int Round = 2048;

    /// <summary>
    /// Adds big-endian 32-bit words (the length is a multiple of 4) in a 64-bit accumulator that the caller folds. The words are
    /// swapped to the machine order a block at a time, then added several at a time; the accumulator cannot overflow because a
    /// span holds fewer than 2^31 bytes, so fewer than 2^29 words of less than 2^32.
    /// </summary>
    internal static ulong SumWords(ReadOnlySpan<byte> words)
    {
        if (!BitConverter.IsLittleEndian || !Vector.IsHardwareAccelerated || words.Length < 4 * Vector<uint>.Count) return SumWordsScalar(words);
        ulong sum = 0;
        ReadOnlySpan<uint> native = MemoryMarshal.Cast<byte, uint>(words);
        Span<uint> swapped = stackalloc uint[Math.Min(Round, native.Length)];
        var lanes = Vector<ulong>.Zero;
        for (int start = 0; start < native.Length; start += Round)
        {
            int n = Math.Min(Round, native.Length - start);
            BinaryPrimitives.ReverseEndianness(native.Slice(start, n), swapped[..n]);
            int j = 0;
            for (; j + Vector<uint>.Count <= n; j += Vector<uint>.Count)
            {
                Vector.Widen(new Vector<uint>(swapped[j..]), out Vector<ulong> low, out Vector<ulong> high);
                lanes += low + high;
            }
            for (; j < n; j++) sum += swapped[j];
        }
        return sum + Vector.Sum(lanes);
    }

    /// <summary>The same sum one word at a time: for short spans and for machines without vector instructions.</summary>
    internal static ulong SumWordsScalar(ReadOnlySpan<byte> words)
    {
        ulong sum = 0;
        for (int i = 0; i < words.Length; i += 4) sum += BinaryPrimitives.ReadUInt32BigEndian(words[i..]);
        return sum;
    }

    /// <summary>The 16 character ASCII encoding of a checksum (the algorithm of cfitsio's <c>ffesum</c>).</summary>
    public static string Encode(uint sum, bool complement)
    {
        ReadOnlySpan<byte> exclude = [0x3a, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x5b, 0x5c, 0x5d, 0x5e, 0x5f, 0x60];
        uint value = complement ? ~sum : sum;
        Span<byte> asc = stackalloc byte[16];
        Span<byte> ch = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            int b = (int)((value >> (24 - 8 * i)) & 0xFF);
            int quotient = b / 4 + 0x30, remainder = b % 4;
            for (int j = 0; j < 4; j++) ch[j] = (byte)quotient;
            ch[0] += (byte)remainder;

            // Avoid punctuation characters by moving value between the two members of each pair.
            for (bool check = true; check;)
            {
                check = false;
                foreach (byte x in exclude)
                    for (int j = 0; j < 4; j += 2)
                        if (ch[j] == x || ch[j + 1] == x)
                        {
                            ch[j]++;
                            ch[j + 1]--;
                            check = true;
                        }
            }
            for (int j = 0; j < 4; j++) asc[4 * j + i] = ch[j];
        }

        var result = new char[16];
        for (int i = 0; i < 16; i++) result[i] = (char)asc[(i + 15) % 16];
        return new string(result);
    }

    public static string ToDecimal(uint sum) => sum.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A write-only stream that accumulates the checksum of everything written to it.</summary>
internal sealed class ChecksumStream : Stream
{
    private ulong _sum;
    private readonly byte[] _word = new byte[4];
    private int _filled;

    /// <summary>The checksum so far; a trailing partial word counts as if zero padded.</summary>
    public uint Result
    {
        get
        {
            if (_filled == 0) return Checksum.Fold(_sum);
            Span<byte> tail = stackalloc byte[4];
            _word.AsSpan(0, _filled).CopyTo(tail);
            return Checksum.Fold(_sum + BinaryPrimitives.ReadUInt32BigEndian(tail));
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        int i = 0;
        // Complete a word left over from the previous write.
        while (_filled > 0 && i < buffer.Length)
        {
            _word[_filled++] = buffer[i++];
            if (_filled == 4)
            {
                _sum += BinaryPrimitives.ReadUInt32BigEndian(_word);
                _filled = 0;
            }
        }
        int whole = (buffer.Length - i) / 4 * 4;
        _sum += Checksum.SumWords(buffer.Slice(i, whole));
        i += whole;
        for (; i < buffer.Length; i++) _word[_filled++] = buffer[i];
        _sum = Checksum.Fold(_sum);
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled(cancellationToken);
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
