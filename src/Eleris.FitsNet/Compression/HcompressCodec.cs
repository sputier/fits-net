using System.Buffers.Binary;
using System.Numerics;

namespace Eleris.FitsNet.Compression;

/// <summary>
/// H-transform compression (<c>HCOMPRESS_1</c>) of a 2-D tile: a Haar-like transform, optional quantization of the
/// coefficients by a scale factor, then bit-plane quadtree coding with a fixed Huffman code.
/// Arrays are indexed <c>a[row * width + column]</c>; the stream stores the number of rows first, then the number of columns.
/// </summary>
internal static class HcompressCodec
{
    private static readonly byte[] Magic = [0xDD, 0x99];
    private const int HeaderBytes = 25;

    // Fixed Huffman code of the 4-bit quadtree values (value -> code, number of bits).
    private static readonly int[] Code = [0x3e, 0x00, 0x01, 0x08, 0x02, 0x09, 0x1a, 0x1b, 0x03, 0x1c, 0x0a, 0x1d, 0x0b, 0x1e, 0x3f, 0x0c];
    private static readonly int[] CodeBits = [6, 3, 3, 4, 3, 4, 5, 5, 3, 5, 4, 5, 4, 5, 6, 4];

    // Direct lookup of the fixed Huffman code on 6 bits: entry = (value << 4) | code length. The code is complete, so every
    // 6-bit pattern maps to a value.
    private static readonly int[] HuffmanTable = BuildHuffmanTable();

    private static int[] BuildHuffmanTable()
    {
        var table = new int[64];
        for (int v = 0; v < 16; v++)
        {
            int len = CodeBits[v], first = Code[v] << (6 - len);
            for (int k = 0; k < 1 << (6 - len); k++) table[first + k] = (v << 4) | len;
        }
        return table;
    }

    private static int CeilLog2(int n) => n <= 1 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)(n - 1));

    // =============================================================================================================
    // Decoding
    // =============================================================================================================

    /// <summary>Decodes a tile of width x height pixels. The smoothing hint of lossy streams is not implemented and is ignored.</summary>
    public static int[] Decode(byte[] data, int width, int height, bool smooth)
    {
        var (a, nx, ny, scale) = DecodeCoefficients(data, width, height);
        if (smooth && scale > 1)
            throw new NotSupportedException("HCOMPRESS_1 smoothing of lossy data (ZNAME SMOOTH = 1) is not implemented.");
        if (scale > 1)
            for (int i = 0; i < a.Length; i++) a[i] *= scale;
        Hinv(a, nx, ny);

        // Lossy streams can overshoot the 32-bit range: saturate rather than wrap around.
        var result = new int[a.Length];
        for (int i = 0; i < result.Length; i++) result[i] = (int)Math.Clamp(a[i], int.MinValue, int.MaxValue);
        return result;
    }

    /// <summary>Entropy-decodes the stream into the (digitized) transform coefficients.</summary>
    internal static (long[] A, int Nx, int Ny, int Scale) DecodeCoefficients(byte[] data, int width, int height)
    {
        if (width < 2 || height < 2) throw new FitsException("HCOMPRESS_1 tiles need at least two pixels along each axis.");
        if (data.Length < HeaderBytes || data[0] != Magic[0] || data[1] != Magic[1])
            throw new FitsException("The HCOMPRESS_1 stream does not start with the expected signature.");
        int nx = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(2));
        int ny = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(6));
        int scale = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(10));
        long sumall = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(14));
        if (nx != height || ny != width)
            throw new FitsException($"The HCOMPRESS_1 stream describes a {ny} x {nx} tile but {width} x {height} is expected.");
        int[] nbitplanes = [data[22], data[23], data[24]];
        if (nbitplanes.Any(b => b > 62)) throw new FitsException("Invalid number of bit planes in the HCOMPRESS_1 stream.");

        long count = (long)nx * ny;
        if (count > Array.MaxLength) throw new FitsException("The HCOMPRESS_1 tile is too large.");
        var a = new long[count];
        var reader = new BitReader(data, HeaderBytes);
        int nx2 = (nx + 1) / 2, ny2 = (ny + 1) / 2;
        QtreeDecode(reader, a, 0, ny, nx2, ny2, nbitplanes[0]);
        QtreeDecode(reader, a, ny2, ny, nx2, ny / 2, nbitplanes[1]);
        QtreeDecode(reader, a, ny * nx2, ny, nx / 2, ny2, nbitplanes[1]);
        QtreeDecode(reader, a, ny * nx2 + ny2, ny, nx / 2, ny / 2, nbitplanes[2]);
        if (reader.Nybble() != 0) throw new FitsException("The HCOMPRESS_1 stream has an invalid bit plane marker.");
        reader.AlignToByte(); // the sign bits are written as a separate, byte aligned bit stream
        reader.ApplySigns(a);

        a[0] = sumall;
        return (a, nx, ny, scale);
    }

    private static void QtreeDecode(BitReader r, long[] a, int offset, int n, int nqx, int nqy, int nbitplanes)
    {
        if (nbitplanes == 0) return;
        int log2n = CeilLog2(Math.Max(nqx, nqy));
        int nqx2 = (nqx + 1) / 2, nqy2 = (nqy + 1) / 2;
        var scratch = new byte[Math.Max(1, nqx2 * nqy2)];

        for (int bit = nbitplanes - 1; bit >= 0; bit--)
        {
            int b = r.Nybble();
            if (b == 0)
            {
                // the bit plane was written directly, four pixels per nybble
                for (int i = 0; i < nqx2 * nqy2; i++) scratch[i] = (byte)r.Nybble();
                QtreeBitIns(scratch, nqx, nqy, a, offset, n, bit);
            }
            else if (b != 0xF)
            {
                throw new FitsException("The HCOMPRESS_1 stream has an invalid bit plane format code.");
            }
            else
            {
                scratch[0] = (byte)r.Huffman();
                int nx = 1, ny = 1, nfx = nqx, nfy = nqy, c = 1 << log2n;
                for (int k = 1; k < log2n; k++)
                {
                    c >>= 1;
                    nx <<= 1;
                    ny <<= 1;
                    if (nfx <= c) nx--; else nfx -= c;
                    if (nfy <= c) ny--; else nfy -= c;
                    QtreeExpand(r, scratch, nx, ny);
                }
                QtreeBitIns(scratch, nqx, nqy, a, offset, n, bit);
            }
        }
    }

    /// <summary>Expands a quadtree level in place: every set bit becomes a 4-bit code read from the stream.</summary>
    private static void QtreeExpand(BitReader r, byte[] a, int nx, int ny)
    {
        QtreeCopy(a, nx, ny);
        for (int i = nx * ny - 1; i >= 0; i--)
            if (a[i] != 0) a[i] = (byte)r.Huffman();
    }

    private static void QtreeCopy(byte[] a, int nx, int ny)
    {
        int nx2 = (nx + 1) / 2, ny2 = (ny + 1) / 2;
        int k = ny2 * (nx2 - 1) + ny2 - 1;
        for (int i = nx2 - 1; i >= 0; i--)
        {
            int s00 = 2 * (ny * i + ny2 - 1);
            for (int j = ny2 - 1; j >= 0; j--)
            {
                a[s00] = a[k];
                k--;
                s00 -= 2;
            }
        }

        int ii;
        for (ii = 0; ii < nx - 1; ii += 2)
        {
            int s00 = ny * ii, s10 = s00 + ny, j;
            for (j = 0; j < ny - 1; j += 2)
            {
                a[s10 + 1] = (byte)(a[s00] & 1);
                a[s10] = (byte)((a[s00] >> 1) & 1);
                a[s00 + 1] = (byte)((a[s00] >> 2) & 1);
                a[s00] = (byte)((a[s00] >> 3) & 1);
                s00 += 2;
                s10 += 2;
            }
            if (j < ny)
            {
                a[s10] = (byte)((a[s00] >> 1) & 1);
                a[s00] = (byte)((a[s00] >> 3) & 1);
            }
        }
        if (ii < nx)
        {
            int s00 = ny * ii, j;
            for (j = 0; j < ny - 1; j += 2)
            {
                a[s00 + 1] = (byte)((a[s00] >> 2) & 1);
                a[s00] = (byte)((a[s00] >> 3) & 1);
                s00 += 2;
            }
            if (j < ny) a[s00] = (byte)((a[s00] >> 3) & 1);
        }
    }

    /// <summary>ORs the bit plane <paramref name="bit"/> described by the 4-bit codes of <paramref name="a"/> into <paramref name="b"/>.</summary>
    private static void QtreeBitIns(byte[] a, int nx, int ny, long[] b, int offset, int n, int bit)
    {
        int k = 0, i;
        for (i = 0; i < nx - 1; i += 2)
        {
            int s00 = offset + n * i, j;
            for (j = 0; j < ny - 1; j += 2)
            {
                int code = a[k];
                if (code != 0)   // the high bit planes are mostly empty
                {
                    b[s00 + n + 1] |= (long)(code & 1) << bit;
                    b[s00 + n] |= (long)((code >> 1) & 1) << bit;
                    b[s00 + 1] |= (long)((code >> 2) & 1) << bit;
                    b[s00] |= (long)((code >> 3) & 1) << bit;
                }
                s00 += 2;
                k++;
            }
            if (j < ny)
            {
                b[s00 + n] |= (long)((a[k] >> 1) & 1) << bit;
                b[s00] |= (long)((a[k] >> 3) & 1) << bit;
                k++;
            }
        }
        if (i < nx)
        {
            int s00 = offset + n * i, j;
            for (j = 0; j < ny - 1; j += 2)
            {
                b[s00 + 1] |= (long)((a[k] >> 2) & 1) << bit;
                b[s00] |= (long)((a[k] >> 3) & 1) << bit;
                s00 += 2;
                k++;
            }
            if (j < ny) b[s00] |= (long)((a[k] >> 3) & 1) << bit;
        }
    }

    // ---- inverse H-transform ---------------------------------------------------------------------------------------

    internal static void Hinv(long[] a, int nx, int ny)
    {
        int nmax = Math.Max(nx, ny);
        int log2n = CeilLog2(nmax);
        var tmp = new long[(nmax + 1) / 2];

        int shift = 1;
        long bit0 = 1L << (log2n - 1), bit1 = bit0 << 1, bit2 = bit0 << 2;
        long mask0 = -bit0, mask1 = mask0 << 1, mask2 = mask0 << 2;
        long prnd0 = bit0 >> 1, prnd1 = bit1 >> 1, prnd2 = bit2 >> 1;
        long nrnd0 = prnd0 - 1, nrnd1 = prnd1 - 1, nrnd2 = prnd2 - 1;

        a[0] = (a[0] + (a[0] >= 0 ? prnd2 : nrnd2)) & mask2;

        int nxtop = 1, nytop = 1, nxf = nx, nyf = ny, c = 1 << log2n;
        for (int k = log2n - 1; k >= 0; k--)
        {
            c >>= 1;
            nxtop <<= 1;
            nytop <<= 1;
            if (nxf <= c) nxtop--; else nxf -= c;
            if (nyf <= c) nytop--; else nyf -= c;
            if (k == 0)
            {
                nrnd0 = 0;
                shift = 2;
            }

            for (int i = 0; i < nxtop; i++) Unshuffle(a, ny * i, nytop, 1, tmp);
            for (int j = 0; j < nytop; j++) Unshuffle(a, j, nxtop, ny, tmp);

            int oddx = nxtop % 2, oddy = nytop % 2;
            int ii;
            for (ii = 0; ii < nxtop - oddx; ii += 2)
            {
                int s00 = ny * ii, s10 = s00 + ny, jj;
                for (jj = 0; jj < nytop - oddy; jj += 2)
                {
                    long h0 = a[s00], hx = a[s10], hy = a[s00 + 1], hc = a[s10 + 1];
                    hx = (hx + (hx >= 0 ? prnd1 : nrnd1)) & mask1;
                    hy = (hy + (hy >= 0 ? prnd1 : nrnd1)) & mask1;
                    hc = (hc + (hc >= 0 ? prnd0 : nrnd0)) & mask0;
                    long lowbit0 = hc & bit0;
                    hx = hx >= 0 ? hx - lowbit0 : hx + lowbit0;
                    hy = hy >= 0 ? hy - lowbit0 : hy + lowbit0;
                    long lowbit1 = (hc ^ hx ^ hy) & bit1;
                    h0 = h0 >= 0 ? h0 + lowbit0 - lowbit1 : h0 + (lowbit0 == 0 ? lowbit1 : lowbit0 - lowbit1);
                    a[s10 + 1] = (h0 + hx + hy + hc) >> shift;
                    a[s10] = (h0 + hx - hy - hc) >> shift;
                    a[s00 + 1] = (h0 - hx + hy - hc) >> shift;
                    a[s00] = (h0 - hx - hy + hc) >> shift;
                    s00 += 2;
                    s10 += 2;
                }
                if (oddy != 0)
                {
                    long h0 = a[s00], hx = a[s10];
                    hx = (hx + (hx >= 0 ? prnd1 : nrnd1)) & mask1;
                    long lowbit1 = hx & bit1;
                    h0 = h0 >= 0 ? h0 - lowbit1 : h0 + lowbit1;
                    a[s10] = (h0 + hx) >> shift;
                    a[s00] = (h0 - hx) >> shift;
                }
            }
            if (oddx != 0)
            {
                int s00 = ny * ii, jj;
                for (jj = 0; jj < nytop - oddy; jj += 2)
                {
                    long h0 = a[s00], hy = a[s00 + 1];
                    hy = (hy + (hy >= 0 ? prnd1 : nrnd1)) & mask1;
                    long lowbit1 = hy & bit1;
                    h0 = h0 >= 0 ? h0 - lowbit1 : h0 + lowbit1;
                    a[s00 + 1] = (h0 + hy) >> shift;
                    a[s00] = (h0 - hy) >> shift;
                    s00 += 2;
                }
                if (oddy != 0) a[s00] >>= shift;
            }

            bit2 = bit1;
            bit1 = bit0;
            bit0 >>= 1;
            mask1 = mask0;
            mask0 >>= 1;
            prnd1 = prnd0;
            prnd0 >>= 1;
            nrnd1 = nrnd0;
            nrnd0 = prnd0 - 1;
        }
        _ = (bit2, prnd2, nrnd2, mask2);
    }

    private static void Unshuffle(long[] a, int off, int n, int n2, long[] tmp)
    {
        int nhalf = (n + 1) >> 1;
        int pt = 0, p1 = off + n2 * nhalf;
        for (int i = nhalf; i < n; i++)
        {
            tmp[pt++] = a[p1];
            p1 += n2;
        }
        int p2 = off + n2 * (nhalf - 1);
        p1 = off + ((n2 * (nhalf - 1)) << 1);
        for (int i = nhalf - 1; i >= 0; i--)
        {
            a[p1] = a[p2];
            p2 -= n2;
            p1 -= n2 + n2;
        }
        pt = 0;
        p1 = off + n2;
        for (int i = 1; i < n; i += 2)
        {
            a[p1] = tmp[pt++];
            p1 += n2 + n2;
        }
    }

    // =============================================================================================================
    // Encoding
    // =============================================================================================================

    /// <summary>
    /// Encodes a tile. <paramref name="scale"/> of 0 or 1 is lossless; larger values divide the transform coefficients by
    /// that number (lossy).
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<int> pixels, int width, int height, int scale)
    {
        if (width < 2 || height < 2) throw new FitsException("HCOMPRESS_1 tiles need at least two pixels along each axis.");
        if (pixels.Length != (long)width * height) throw new ArgumentException("The pixel count does not match the tile size.", nameof(pixels));
        if (scale < 0) throw new ArgumentOutOfRangeException(nameof(scale));
        int nx = height, ny = width;
        var a = new long[pixels.Length];
        for (int i = 0; i < a.Length; i++) a[i] = pixels[i];

        Htrans(a, nx, ny);
        if (scale > 1) Digitize(a, scale);

        var w = new BitWriter();
        w.WriteByte(Magic[0]);
        w.WriteByte(Magic[1]);
        WriteInt(w, nx);
        WriteInt(w, ny);
        WriteInt(w, scale);
        long sumall = a[0];
        for (int k = 7; k >= 0; k--) w.WriteByte((byte)(sumall >> (8 * k)));
        a[0] = 0;

        // signs of the non-zero coefficients (raster order, one bit each, packed), then magnitudes
        var signBits = new byte[(a.Length + 7) / 8];
        int nsign = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == 0) continue;
            if (a[i] < 0)
            {
                signBits[nsign >> 3] |= (byte)(0x80 >> (nsign & 7));
                a[i] = -a[i];
            }
            nsign++;
        }

        int nx2 = (nx + 1) / 2, ny2 = (ny + 1) / 2;
        var vmax = new long[3];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                int q = (j >= ny2 ? 1 : 0) + (i >= nx2 ? 1 : 0);
                if (vmax[q] < a[i * ny + j]) vmax[q] = a[i * ny + j];
            }
        var nbitplanes = new int[3];
        for (int q = 0; q < 3; q++) nbitplanes[q] = vmax[q] == 0 ? 0 : 64 - BitOperations.LeadingZeroCount((ulong)vmax[q]);
        foreach (int nb in nbitplanes) w.WriteByte((byte)nb);

        QtreeEncode(w, a, 0, ny, nx2, ny2, nbitplanes[0]);
        QtreeEncode(w, a, ny2, ny, nx2, ny / 2, nbitplanes[1]);
        QtreeEncode(w, a, ny * nx2, ny, nx / 2, ny2, nbitplanes[1]);
        QtreeEncode(w, a, ny * nx2 + ny2, ny, nx / 2, ny / 2, nbitplanes[2]);
        w.WriteBits(0, 4);
        w.AlignToByte();
        w.WriteBytes(signBits.AsSpan(0, (nsign + 7) / 8));
        return w.ToArray();
    }

    private static void WriteInt(BitWriter w, int value)
    {
        for (int k = 3; k >= 0; k--) w.WriteByte((byte)(value >> (8 * k)));
    }

    private static void Digitize(long[] a, int scale)
    {
        int d = (scale + 1) / 2 - 1;
        for (int i = 0; i < a.Length; i++) a[i] = (a[i] > 0 ? a[i] + d : a[i] - d) / scale;
    }

    private static void QtreeEncode(BitWriter w, long[] a, int offset, int n, int nqx, int nqy, int nbitplanes)
    {
        if (nbitplanes == 0) return;
        int log2n = CeilLog2(Math.Max(nqx, nqy));
        int nx0 = (nqx + 1) >> 1, ny0 = (nqy + 1) >> 1;

        // level 0 is the finest (4-bit codes of 2x2 blocks), the last level is a single code; the same buffers serve every bit plane
        var levels = new List<(byte[] Data, int Nx, int Ny)> { (new byte[Math.Max(1, nx0 * ny0)], nx0, ny0) };
        for (int k = 1, nx = nx0, ny = ny0; k < log2n; k++)
        {
            nx = (nx + 1) >> 1;
            ny = (ny + 1) >> 1;
            levels.Add((new byte[Math.Max(1, nx * ny)], nx, ny));
        }

        for (int bit = nbitplanes - 1; bit >= 0; bit--)
        {
            QtreeOneBit(a, offset, n, nqx, nqy, levels[0].Data, bit);
            for (int k = 1; k < levels.Count; k++)
                QtreeReduce(levels[k - 1].Data, levels[k - 1].Ny, levels[k - 1].Nx, levels[k - 1].Ny, levels[k].Data);

            // Quadtree coding is used unless its codes fill as many whole bytes as the direct bitmap (cfitsio's rule).
            int codeBits = 0;
            foreach (var (data, lx, ly) in levels)
                for (int i = 0; i < lx * ly; i++)
                    if (data[i] != 0) codeBits += CodeBits[data[i]];
            if (codeBits / 8 >= (nx0 * ny0 + 1) / 2)
            {
                w.WriteBits(0, 4);
                for (int i = 0; i < nx0 * ny0; i++) w.WriteBits(levels[0].Data[i], 4);
                continue;
            }

            w.WriteBits(0xF, 4);
            WriteHuffman(w, levels[^1].Data[0]);
            for (int l = levels.Count - 2; l >= 0; l--)
            {
                var (data, lx, ly) = levels[l];
                for (int i = lx * ly - 1; i >= 0; i--)
                    if (data[i] != 0) WriteHuffman(w, data[i]);
            }
        }
    }

    private static void WriteHuffman(BitWriter w, int value) => w.WriteBits((uint)Code[value], CodeBits[value]);

    private static int BitOf(long v, int bit) => (int)((v >> bit) & 1);

    private static void QtreeOneBit(long[] a, int offset, int n, int nx, int ny, byte[] b, int bit)
    {
        int k = 0, i;
        for (i = 0; i < nx - 1; i += 2)
        {
            int s00 = offset + n * i, s10 = s00 + n, j;
            for (j = 0; j < ny - 1; j += 2)
            {
                b[k++] = (byte)(BitOf(a[s10 + 1], bit) | (BitOf(a[s10], bit) << 1) | (BitOf(a[s00 + 1], bit) << 2) | (BitOf(a[s00], bit) << 3));
                s00 += 2;
                s10 += 2;
            }
            if (j < ny) b[k++] = (byte)((BitOf(a[s10], bit) << 1) | (BitOf(a[s00], bit) << 3));
        }
        if (i < nx)
        {
            int s00 = offset + n * i, j;
            for (j = 0; j < ny - 1; j += 2)
            {
                b[k++] = (byte)((BitOf(a[s00 + 1], bit) << 2) | (BitOf(a[s00], bit) << 3));
                s00 += 2;
            }
            if (j < ny) b[k++] = (byte)(BitOf(a[s00], bit) << 3);
        }
    }

    private static void QtreeReduce(byte[] a, int n, int nx, int ny, byte[] b)
    {
        static int Nz(byte v) => v != 0 ? 1 : 0;
        int k = 0, i;
        for (i = 0; i < nx - 1; i += 2)
        {
            int s00 = n * i, s10 = s00 + n, j;
            for (j = 0; j < ny - 1; j += 2)
            {
                b[k++] = (byte)(Nz(a[s10 + 1]) | (Nz(a[s10]) << 1) | (Nz(a[s00 + 1]) << 2) | (Nz(a[s00]) << 3));
                s00 += 2;
                s10 += 2;
            }
            if (j < ny) b[k++] = (byte)((Nz(a[s10]) << 1) | (Nz(a[s00]) << 3));
        }
        if (i < nx)
        {
            int s00 = n * i, j;
            for (j = 0; j < ny - 1; j += 2)
            {
                b[k++] = (byte)((Nz(a[s00 + 1]) << 2) | (Nz(a[s00]) << 3));
                s00 += 2;
            }
            if (j < ny) b[k++] = (byte)(Nz(a[s00]) << 3);
        }
    }

    // ---- forward H-transform ---------------------------------------------------------------------------------------

    internal static void Htrans(long[] a, int nx, int ny)
    {
        int nmax = Math.Max(nx, ny);
        int log2n = CeilLog2(nmax);
        var tmp = new long[(nmax + 1) / 2];

        int shift = 0;
        long mask = -2, mask2 = mask << 1, prnd = 1, prnd2 = prnd << 1, nrnd2 = prnd2 - 1;
        int nxtop = nx, nytop = ny;
        for (int k = 0; k < log2n; k++)
        {
            int oddx = nxtop % 2, oddy = nytop % 2;
            int i;
            for (i = 0; i < nxtop - oddx; i += 2)
            {
                int s00 = i * ny, s10 = s00 + ny, j;
                for (j = 0; j < nytop - oddy; j += 2)
                {
                    long h0 = (a[s10 + 1] + a[s10] + a[s00 + 1] + a[s00]) >> shift;
                    long hx = (a[s10 + 1] + a[s10] - a[s00 + 1] - a[s00]) >> shift;
                    long hy = (a[s10 + 1] - a[s10] + a[s00 + 1] - a[s00]) >> shift;
                    long hc = (a[s10 + 1] - a[s10] - a[s00 + 1] + a[s00]) >> shift;
                    a[s10 + 1] = hc;
                    a[s10] = (hx >= 0 ? hx + prnd : hx) & mask;
                    a[s00 + 1] = (hy >= 0 ? hy + prnd : hy) & mask;
                    a[s00] = (h0 >= 0 ? h0 + prnd2 : h0 + nrnd2) & mask2;
                    s00 += 2;
                    s10 += 2;
                }
                if (oddy != 0)
                {
                    long h0 = (a[s10] + a[s00]) << (1 - shift);
                    long hx = (a[s10] - a[s00]) << (1 - shift);
                    a[s10] = (hx >= 0 ? hx + prnd : hx) & mask;
                    a[s00] = (h0 >= 0 ? h0 + prnd2 : h0 + nrnd2) & mask2;
                }
            }
            if (oddx != 0)
            {
                int s00 = i * ny, j;
                for (j = 0; j < nytop - oddy; j += 2)
                {
                    long h0 = (a[s00 + 1] + a[s00]) << (1 - shift);
                    long hy = (a[s00 + 1] - a[s00]) << (1 - shift);
                    a[s00 + 1] = (hy >= 0 ? hy + prnd : hy) & mask;
                    a[s00] = (h0 >= 0 ? h0 + prnd2 : h0 + nrnd2) & mask2;
                    s00 += 2;
                }
                if (oddy != 0)
                {
                    long h0 = a[s00] << (2 - shift);
                    a[s00] = (h0 >= 0 ? h0 + prnd2 : h0 + nrnd2) & mask2;
                }
            }

            for (int r = 0; r < nxtop; r++) Shuffle(a, ny * r, nytop, 1, tmp);
            for (int c = 0; c < nytop; c++) Shuffle(a, c, nxtop, ny, tmp);

            nxtop = (nxtop + 1) >> 1;
            nytop = (nytop + 1) >> 1;
            shift = 1;
            mask = mask2;
            prnd = prnd2;
            mask2 <<= 1;
            prnd2 <<= 1;
            nrnd2 = prnd2 - 1;
        }
    }

    private static void Shuffle(long[] a, int off, int n, int n2, long[] tmp)
    {
        int pt = 0, p1 = off + n2;
        for (int i = 1; i < n; i += 2)
        {
            tmp[pt++] = a[p1];
            p1 += n2 + n2;
        }
        p1 = off + n2;
        int p2 = off + n2 + n2;
        for (int i = 2; i < n; i += 2)
        {
            a[p1] = a[p2];
            p1 += n2;
            p2 += n2 + n2;
        }
        pt = 0;
        for (int i = 1; i < n; i += 2)
        {
            a[p1] = tmp[pt++];
            p1 += n2;
        }
    }

    // =============================================================================================================
    // Bit reading
    // =============================================================================================================

    private sealed class BitReader(byte[] data, int start)
    {
        private int _pos = start;
        private ulong _buffer;
        private int _bits;    // valid bits at the bottom of _buffer, zero padding included
        private int _padding; // zero bits appended after the end of the data (never legitimate to consume)

        private void Fill(int n)
        {
            while (_bits < n)
            {
                if (_pos < data.Length) _buffer = (_buffer << 8) | data[_pos++];
                else
                {
                    _buffer <<= 8;
                    _padding += 8;
                }
                _bits += 8;
            }
        }

        private int Take(int n)
        {
            _bits -= n;
            if (_bits < _padding) throw new FitsException("The HCOMPRESS_1 stream is truncated.");
            return (int)((_buffer >> _bits) & ((1UL << n) - 1));
        }

        public int Bit()
        {
            Fill(1);
            return Take(1);
        }

        public int Nybble()
        {
            Fill(4);
            return Take(4);
        }

        /// <summary>Discards the bits left in the current byte.</summary>
        public void AlignToByte() => _bits -= _bits % 8;

        /// <summary>
        /// Negates the coefficients whose sign bit is set: one bit for each non-zero one, in raster order, read from the whole bytes
        /// that follow (the reader is byte aligned). The bits are taken from the data directly instead of one call each.
        /// </summary>
        public void ApplySigns(long[] a)
        {
            // the bits already buffered come first, then the bytes still in the data
            long pending = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] != 0) pending++;
            long available = _bits - _padding + 8L * (data.Length - _pos);
            if (pending > available) throw new FitsException("The HCOMPRESS_1 stream is truncated.");

            int bufferedBits = _bits;
            ulong buffered = bufferedBits == 0 ? 0 : _buffer & ((1UL << bufferedBits) - 1);
            int used = 0;                   // signs taken from the buffered bits
            long position = 8L * _pos;      // bit position in the data of the next sign after the buffered bits
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == 0) continue;
                int negative;
                if (used < bufferedBits)
                {
                    negative = (int)((buffered >> (bufferedBits - 1 - used)) & 1);
                    used++;
                }
                else
                {
                    negative = (data[(int)(position >> 3)] >> (7 - (int)(position & 7))) & 1;
                    position++;
                }
                if (negative != 0) a[i] = -a[i];
            }
        }

        /// <summary>Reads one value of the fixed Huffman code.</summary>
        public int Huffman()
        {
            Fill(6);
            int entry = HuffmanTable[(int)((_buffer >> (_bits - 6)) & 63)];
            Take(entry & 15);
            return entry >> 4;
        }
    }
}
