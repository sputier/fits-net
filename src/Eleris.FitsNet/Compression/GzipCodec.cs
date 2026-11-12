using System.IO.Compression;

namespace Eleris.FitsNet.Compression;

/// <summary><c>GZIP_1</c> and <c>GZIP_2</c> (gzip, with byte shuffling for the latter) and the helpers they share.</summary>
internal static class GzipCodec
{

    public static byte[] Decompress(byte[] data, long expectedLength)
    {
        if (expectedLength < 0 || expectedLength > Array.MaxLength) throw new FitsException("Invalid decompressed tile size.");
        // Deflate cannot expand by more than about 1032:1; a header asking for more is forged and would only waste memory.
        if (expectedLength > (long)data.Length * 1100 + 4096) throw new FitsException("The gzip tile cannot decompress to the announced size.");
        using var input = new MemoryStream(data, writable: false);
        // gzip is the specified wrapper; tolerate bare zlib/deflate streams written by some tools.
        bool gzip = data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B;
        bool zlib = !gzip && data.Length >= 2 && (data[0] & 0x0F) == 8 && ((data[0] << 8) | data[1]) % 31 == 0;
        Stream stream = gzip ? new GZipStream(input, CompressionMode.Decompress)
            : zlib ? new ZLibStream(input, CompressionMode.Decompress)
            : new DeflateStream(input, CompressionMode.Decompress);
        using (stream)
        {
            var result = new byte[expectedLength];
            try
            {
                stream.ReadExactly(result);
                Span<byte> extra = stackalloc byte[1];
                if (stream.Read(extra) > 0) throw new FitsException("The gzip tile decompresses to more bytes than expected.");
            }
            catch (EndOfStreamException)
            {
                throw new FitsException("The gzip tile decompresses to fewer bytes than expected.");
            }
            catch (InvalidDataException e)
            {
                throw new FitsException("Invalid gzip data in a compressed tile.", e);
            }
            return result;
        }
    }

    /// <summary>The level used when none is asked for (<see cref="CompressionOptions.GzipLevel"/> -1).</summary>
    public const int DefaultLevel = -1;

    /// <param name="data">The bytes to compress.</param>
    /// <param name="level">The zlib level 0 to 9, or <see cref="DefaultLevel"/> for the default of the runtime.</param>
    public static byte[] Compress(ReadOnlySpan<byte> data, int level = DefaultLevel)
    {
        using var output = new MemoryStream();
#if NET9_0_OR_GREATER
        using (var gz = level < 0
            ? new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)
            : new GZipStream(output, new ZLibCompressionOptions { CompressionLevel = level }, leaveOpen: true))
#else
        // before .NET 9 the levels are not selectable one by one: 0 stores, 1 is the fastest, 9 the smallest, the others are the default
        using (var gz = new GZipStream(output, level switch { 0 => CompressionLevel.NoCompression, 1 => CompressionLevel.Fastest, 9 => CompressionLevel.SmallestSize, _ => CompressionLevel.Optimal }, leaveOpen: true))
#endif
            gz.Write(data);
        return output.ToArray();
    }

    /// <summary>Groups the most significant bytes of every element first (the GZIP_2 transform).</summary>
    public static byte[] Shuffle(ReadOnlySpan<byte> data, int elementSize)
    {
        int n = data.Length / elementSize;
        var result = new byte[data.Length];
        for (int i = 0; i < n; i++)
            for (int b = 0; b < elementSize; b++)
                result[b * n + i] = data[i * elementSize + b];
        return result;
    }

    public static byte[] Unshuffle(ReadOnlySpan<byte> data, int elementSize)
    {
        int n = data.Length / elementSize;
        var result = new byte[data.Length];
        for (int i = 0; i < n; i++)
            for (int b = 0; b < elementSize; b++)
                result[i * elementSize + b] = data[b * n + i];
        return result;
    }
}
