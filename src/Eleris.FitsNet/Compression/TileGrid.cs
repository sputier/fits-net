namespace Eleris.FitsNet.Compression;

/// <summary>The decomposition of an N-dimensional image into tiles (first axis varying fastest, as in FITS).</summary>
internal sealed class TileGrid
{
    public long[] Axes { get; }

    public long[] TileShape { get; }

    public long[] TilesPerAxis { get; }

    public long TileCount { get; }

    private readonly long[] _strides;

    public TileGrid(long[] axes, long[] tileShape)
    {
        if (axes.Length != tileShape.Length) throw new FitsException("ZNAXIS and the number of ZTILEn keywords differ.");
        Axes = axes;
        TileShape = tileShape;
        TilesPerAxis = new long[axes.Length];
        _strides = new long[axes.Length];
        long count = 1, stride = 1;
        try
        {
            for (int i = 0; i < axes.Length; i++)
            {
                if (axes[i] < 0 || tileShape[i] < 1) throw new FitsException("Invalid image or tile dimensions.");
                TilesPerAxis[i] = axes[i] / tileShape[i] + (axes[i] % tileShape[i] != 0 ? 1 : 0);
                count = checked(count * TilesPerAxis[i]);
                _strides[i] = stride;
                stride = checked(stride * axes[i]);
            }
        }
        catch (OverflowException)
        {
            throw new FitsException("The image size overflows.");
        }
        TileCount = axes.Length == 0 ? 0 : count;
    }

    /// <summary>Number of pixels of the largest tile.</summary>
    public double MaxTilePixels
    {
        get
        {
            double n = 1;
            for (int i = 0; i < Axes.Length; i++) n *= Math.Min(TileShape[i], Math.Max(1, Axes[i]));
            return n;
        }
    }

    public long PixelCount => Axes.Length == 0 ? 0 : _strides[^1] * Axes[^1];

    /// <summary>Origin (per axis) and extent of tile <paramref name="index"/>; edge tiles are clipped to the image.</summary>
    public void GetTile(long index, out long[] origin, out long[] extent)
    {
        origin = new long[Axes.Length];
        extent = new long[Axes.Length];
        for (int i = 0; i < Axes.Length; i++)
        {
            long t = index % TilesPerAxis[i];
            index /= TilesPerAxis[i];
            origin[i] = t * TileShape[i];
            extent[i] = Math.Min(TileShape[i], Axes[i] - origin[i]);
        }
    }

    /// <summary>The number of pixels of a tile as an array length.</summary>
    public static int CountInt(long[] extent)
    {
        long n = Count(extent);
        return n <= Array.MaxLength ? (int)n : throw new FitsException("The number of pixels of a tile is larger than an array can hold.");
    }

    public static long Count(long[] extent)
    {
        long n = 1;
        foreach (long e in extent)
        {
            if (e < 0) throw new FitsException("A tile cannot have a negative extent.");
            try
            {
                n = checked(n * e);
            }
            catch (OverflowException)
            {
                throw new FitsException("The number of pixels of a tile overflows.");
            }
        }
        return n;
    }

    /// <summary>
    /// Copies a tile of native values into the big-endian bytes of the whole image, row by row. Tiles never overlap, so several tiles
    /// can be written at the same time.
    /// </summary>
    public void WriteTile<T>(Span<byte> image, ReadOnlySpan<T> tile, long[] origin, long[] extent) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        foreach (var (imageOffset, tileOffset, run) in Runs(origin, extent))
            BigEndian.Write(tile.Slice((int)tileOffset, (int)run), image.Slice(checked((int)(imageOffset * size)), (int)run * size));
    }

    /// <summary>Copies the big-endian bytes of a tile as they are into the big-endian bytes of the whole image.</summary>
    public void WriteTileBytes(Span<byte> image, ReadOnlySpan<byte> tile, int elementSize, long[] origin, long[] extent)
    {
        foreach (var (imageOffset, tileOffset, run) in Runs(origin, extent))
            tile.Slice((int)tileOffset * elementSize, (int)run * elementSize).CopyTo(image.Slice(checked((int)(imageOffset * elementSize)), (int)run * elementSize));
    }

    /// <summary>Copies a tile out of the big-endian bytes of the whole image, as native values.</summary>
    public void ReadTile<T>(ReadOnlySpan<byte> image, Span<T> tile, long[] origin, long[] extent) where T : unmanaged
    {
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        foreach (var (imageOffset, tileOffset, run) in Runs(origin, extent))
            BigEndian.Read(image.Slice(checked((int)(imageOffset * size)), (int)run * size), tile.Slice((int)tileOffset, (int)run));
    }

    /// <summary>Copies the big-endian bytes of a tile as they are, for the algorithms that compress bytes.</summary>
    public void ReadTileBytes(ReadOnlySpan<byte> image, Span<byte> tile, int elementSize, long[] origin, long[] extent)
    {
        foreach (var (imageOffset, tileOffset, run) in Runs(origin, extent))
            image.Slice(checked((int)(imageOffset * elementSize)), (int)run * elementSize).CopyTo(tile.Slice((int)tileOffset * elementSize, (int)run * elementSize));
    }

    /// <summary>The contiguous runs of a tile: where each starts in the image and in the tile (in pixels), and its length.</summary>
    private IEnumerable<(long ImageOffset, long TileOffset, long Run)> Runs(long[] origin, long[] extent)
    {
        if (Axes.Length == 0) yield break;
        long run = extent[0];
        if (run == 0) yield break;
        var idx = new long[Axes.Length]; // position within the tile for axes 1..n-1
        long tileOffset = 0;
        while (true)
        {
            long imageOffset = origin[0];
            for (int d = 1; d < Axes.Length; d++) imageOffset += (origin[d] + idx[d]) * _strides[d];
            yield return (imageOffset, tileOffset, run);
            tileOffset += run;

            int axis = 1;
            for (; axis < Axes.Length; axis++)
            {
                if (++idx[axis] < extent[axis]) break;
                idx[axis] = 0;
            }
            if (axis >= Axes.Length) yield break;
        }
    }
}
