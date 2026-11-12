using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Eleris.FitsNet;

/// <summary>
/// Writes a FITS file that holds one image, band by band, so that the image never has to be in memory: for images larger than the
/// memory, or computed a few rows at a time (a mosaic, a stack). The header is given first; the rows then follow in file order, a band of
/// consecutive rows of one plane at a time, and <see cref="Complete"/> publishes the file.
/// </summary>
/// <remarks>
/// <para>
/// A row is <c>NAXIS1</c> values, a plane is <c>NAXIS2</c> rows (one row for a one-dimensional image), and the planes are the
/// combinations of the other axes, in file order (<c>NAXIS3</c> planes for a cube). The rows are given plane 0 rows 0 to
/// <c>NAXIS2 - 1</c>, then plane 1, and so on; a band can have any number of rows, from one to what is left of its plane.
/// </para>
/// <para>
/// The file is byte for byte the one <see cref="FitsFile.Save(string, ChecksumPolicy, FitsSaveMode, CancellationToken)"/> writes for a
/// <see cref="FitsFile"/> holding only an image of the same pixels and header (made with <see cref="ImageHdu.CreateFromPlanes{T}"/>
/// or <see cref="ImageHdu.Create{T}"/>), with the same guarantees: the file is written to a temporary file in the directory of the
/// target and moved in place by <see cref="Complete"/>; <see cref="FitsSaveMode.CreateNew"/> never replaces a file, even one that appears
/// while the image is written (on Unix with the limits described for that mode in
/// <see cref="FitsFile.Save(string, ChecksumPolicy, FitsSaveMode, CancellationToken)"/>); a write that fails, is cancelled, or is disposed of before <see cref="Complete"/> deletes the temporary
/// file and leaves an existing target untouched. <c>CHECKSUM</c> and <c>DATASUM</c> are summed while the rows are written, and their
/// cards, which have a fixed width, are filled in before the file is published.
/// </para>
/// <para>
/// The writer holds no band: each band is converted to the bytes of the file through a buffer of 256 KB, and the memory it uses (that
/// buffer, the buffer of the file and the header) does not depend on the number of rows. The writes are synchronous. Any exception thrown by <see cref="WriteRows"/> or <see cref="Complete"/>, an invalid argument included, abandons the write:
/// the temporary file is deleted, nothing is published, and every later call throws <see cref="InvalidOperationException"/>. An instance
/// is not safe for concurrent use.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var header = new FitsHeader()
///     .Set("BITPIX", -32).Set("NAXIS", 2).Set("NAXIS1", width).Set("NAXIS2", height)
///     .Set("OBJECT", "M42");
/// using var writer = FitsImageWriter&lt;float&gt;.Create("mosaic.fits", header, ChecksumPolicy.Always, FitsSaveMode.CreateNew);
/// var band = new float[width * 64];
/// for (long row = 0; row &lt; height; row += 64)
/// {
///     int rows = (int)Math.Min(64, height - row);
///     ComputeRows(row, band.AsSpan(0, rows * width));
///     writer.WriteRows(0, row, band.AsSpan(0, rows * width));
/// }
/// writer.Complete();
/// </code>
/// </example>
/// <typeparam name="T">
/// The type of the values written, as in <see cref="ImageHdu.Create{T}"/>: <c>byte</c>, <c>short</c>, <c>int</c>, <c>long</c>,
/// <c>float</c> or <c>double</c> for <c>BITPIX</c> 8, 16, 32, 64, -32 or -64, and <c>sbyte</c>, <c>ushort</c>, <c>uint</c> or <c>ulong</c>
/// for the same integers with the <c>BZERO</c> convention. The values are the stored values: <c>BSCALE</c>, <c>BZERO</c> and
/// <c>BLANK</c> are cards of the header, written as given, not applied to the values.
/// </typeparam>
public sealed class FitsImageWriter<T> : IDisposable where T : unmanaged
{
    // Values converted at a time: a block that stays in the cache between the swap and the write, as for an image made of planes.
    private const int BlockBytes = 256 * 1024;

    private enum State
    {
        Writing,
        Completed,
        Failed,
        Disposed,
    }

    private readonly ImageHdu _image;
    private readonly string _target;
    private readonly string _temp;
    private readonly bool _createNew;
    private readonly bool _flipTopBit;
    private readonly long _dataLength;
    private readonly int _headerLength;
    private FileStream? _stream;
    private ChecksumStream? _sum;
    private byte[]? _buffer;
    private long _plane;
    private long _row;
    private State _state;

    private FitsImageWriter(ImageHdu image, string target, string temp, bool createNew, bool flipTopBit, bool checksums)
    {
        _image = image;
        _target = target;
        _temp = temp;
        _createNew = createNew;
        _flipTopBit = flipTopBit;
        var axes = image.Axes;
        RowLength = (int)axes[0];
        RowsPerPlane = axes.Count > 1 ? axes[1] : 1;
        long planes = 1;
        for (int i = 2; i < axes.Count; i++) planes *= axes[i];   // no overflow: the size of the data was checked
        PlaneCount = planes;
        _dataLength = image.GetLayout(primary: true).DataLength;
        _sum = checksums ? new ChecksumStream() : null;

        byte[] header = image.Header.ToBytes();
        _headerLength = header.Length;
        _stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
        try
        {
            _stream.Write(header);
        }
        catch
        {
            Abort();
            throw;
        }
    }

    /// <summary>The number of values in a row: <c>NAXIS1</c>.</summary>
    public int RowLength { get; }

    /// <summary>The number of rows in a plane: <c>NAXIS2</c>, or 1 for a one-dimensional image.</summary>
    public long RowsPerPlane { get; }

    /// <summary>The number of planes: the product of <c>NAXIS3</c> and the axes after it, 1 for an image of one or two axes.</summary>
    public long PlaneCount { get; }

    /// <summary>
    /// Starts writing an image to <paramref name="path"/>: checks the header, creates the temporary file next to the target and writes the
    /// header to it. The target is not touched until <see cref="Complete"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The header holds <c>BITPIX</c>, <c>NAXIS</c> and <c>NAXIS1</c>..<c>NAXISn</c>, and any other card, which is written as given
    /// (<c>BSCALE</c>, <c>BZERO</c>, <c>BLANK</c>, a WCS, ...). It is copied: changing it afterwards changes nothing. Its structural cards
    /// are made those of a primary image, as <see cref="FitsFile.Save(string, ChecksumPolicy)"/> does for the first HDU of a file: the
    /// header may start with <c>SIMPLE</c>, or with <c>XTENSION = 'IMAGE'</c> (the header of an extension), or with neither. So the header
    /// of an <see cref="ImageHdu"/> gives the file that saving that image alone would give.
    /// </para>
    /// <para>
    /// <c>BITPIX</c> must be the one of <typeparamref name="T"/>, and for <c>sbyte</c>, <c>ushort</c>, <c>uint</c> and <c>ulong</c> the header
    /// must have the <c>BZERO</c> of the convention (-128, 32768, 2147483648 or 9223372036854775808, which <see cref="ImageHdu.Create{T}"/>
    /// writes) and no <c>BSCALE</c> other than 1. The image must have at least one pixel.
    /// </para>
    /// <para>
    /// <paramref name="checksums"/> is applied as by <see cref="FitsFile.Save(string, ChecksumPolicy)"/>: <see cref="ChecksumPolicy.Auto"/>
    /// writes <c>CHECKSUM</c> and <c>DATASUM</c> when the header has one of them, <see cref="ChecksumPolicy.Always"/> always,
    /// <see cref="ChecksumPolicy.Never"/> removes them. Unlike a save of an HDU read from a file and left unchanged, which keeps its stored
    /// sums, the writer computes them for the data it is given: sums already in the header are never kept.
    /// </para>
    /// <para>
    /// Unlike <see cref="FitsFile.Save(string, ChecksumPolicy)"/>, the writer cannot attach a <see cref="FitsFile"/> to the file it
    /// replaces: do not write over a file that an open <see cref="FitsFile"/> reads (on Windows the replacement fails while it is open).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is empty, or the header does not describe an image with pixels, has a <c>BITPIX</c> that is not the one of
    /// <typeparamref name="T"/>, lacks the <c>BZERO</c> of an unsigned type, or has rows longer than an array can hold.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="checksums"/> or <paramref name="mode"/> is not a value of its enumeration.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not a supported pixel type.</exception>
    /// <exception cref="FitsFileExistsException"><paramref name="mode"/> is <see cref="FitsSaveMode.CreateNew"/> and something exists at <paramref name="path"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="IOException">The temporary file could not be created or written.</exception>
    /// <param name="path">The path of the file to write.</param>
    /// <param name="header">The header of the image.</param>
    /// <param name="checksums">What to do with the <c>CHECKSUM</c> and <c>DATASUM</c> keywords.</param>
    /// <param name="mode">Whether an existing file may be replaced.</param>
    /// <param name="cancellationToken">The token that cancels the creation.</param>
    /// <returns>The writer, to dispose of once the file is complete or abandoned.</returns>
    public static FitsImageWriter<T> Create(string path, FitsHeader header, ChecksumPolicy checksums = ChecksumPolicy.Auto,
        FitsSaveMode mode = FitsSaveMode.Overwrite, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(header);
        if (checksums is not (ChecksumPolicy.Auto or ChecksumPolicy.Always or ChecksumPolicy.Never))
            throw new ArgumentOutOfRangeException(nameof(checksums), checksums, "Unknown checksum policy.");
        if (mode is not (FitsSaveMode.Overwrite or FitsSaveMode.CreateNew))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown save mode.");
        var (bitPix, bzero, flip) = ImageHdu.Describe<T>();

        var image = PrepareImage(header, bitPix, bzero);
        bool sums = image.PrepareChecksums(checksums, preserve: false);
        // The cards as Save writes them, with a sum to come: DATASUM and CHECKSUM have a fixed width, so the header keeps its length.
        if (sums) image.SetChecksums(0);

        cancellationToken.ThrowIfCancellationRequested();
        bool createNew = mode == FitsSaveMode.CreateNew;
        var (target, temp) = FitsFile.SaveTarget(path, createNew);
        return new FitsImageWriter<T>(image, target, temp, createNew, flip, sums);
    }

    /// <summary>A private image holding a copy of the header, normalized as the primary HDU of a file, and checked against <typeparamref name="T"/>.</summary>
    private static ImageHdu PrepareImage(FitsHeader header, BitPix bitPix, object? bzero)
    {
        var copy = new FitsHeader();
        copy.ReplaceAll(header.Cards);
        if (!copy.Contains("SIMPLE") && !copy.Contains("XTENSION")) copy.Insert(0, new FitsCard("SIMPLE", true, "conforms to FITS standard"));
        if (copy.Count > 0 && copy[0].Keyword == "XTENSION" && copy.Get<string>("XTENSION")?.Trim() is not ("IMAGE" or "IUEIMAGE"))
            throw new ArgumentException("The header is the one of an extension that is not an image.", nameof(header));

        var image = new ImageHdu(copy, DataSource.Empty);
        HduLayout layout;
        try
        {
            image.Normalize(primary: true, hasExtensions: false);
            layout = image.GetLayout(primary: true);
        }
        catch (FitsException e)
        {
            throw new ArgumentException($"The header does not describe an image: {e.Message}", nameof(header), e);
        }

        if (layout.Axes.Length == 0 || layout.ElementsPerGroup == 0)
            throw new ArgumentException("The image has no pixel: there is nothing to write band by band.", nameof(header));
        if (layout.Axes[0] > Array.MaxLength)
            throw new ArgumentException($"A row of {layout.Axes[0]} values is longer than an array can hold.", nameof(header));
        if (layout.BitPix != bitPix)
            throw new ArgumentException(
                $"BITPIX {(int)layout.BitPix} does not store {typeof(T).Name} values, which need BITPIX {(int)bitPix}.", nameof(header));
        if (bzero is not null)
        {
            var expected = bzero is BigInteger big ? big : new BigInteger(Convert.ToInt64(bzero, System.Globalization.CultureInfo.InvariantCulture));
            // an integer card is compared exactly (2^63 + 1 is not the offset of ulong); a real one is exact for these powers of two
            bool matches = copy.Find("BZERO")?.Value switch
            {
                long l => l == expected,
                int i => i == expected,
                BigInteger b => b == expected,
                double d => d == (double)expected,
                _ => false,
            };
            if (!matches)
                throw new ArgumentException(
                    $"{typeof(T).Name} values are stored with BZERO = {bzero}, which the header must have (ImageHdu.Create<{typeof(T).Name}> writes it).",
                    nameof(header));
            if (copy.TryGetValue("BSCALE", out double scale) && scale != 1)
                throw new ArgumentException($"{typeof(T).Name} values are stored with BSCALE = 1, not {scale}.", nameof(header));
        }
        return image;
    }

    /// <summary>
    /// Writes a band: whole rows of one plane, which must be the next ones in file order. The values are converted to the bytes of the
    /// file a block at a time, checking <paramref name="cancellationToken"/> between blocks; <paramref name="rows"/> is not kept.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="plane"/> and <paramref name="firstRow"/> are not the next row to write (rows out of order, written twice, or after the
    /// last one), or <paramref name="rows"/> is empty, is not a whole number of rows, or has more rows than are left in the plane.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="InvalidOperationException">The writer has completed, or an earlier call failed.</exception>
    /// <exception cref="ObjectDisposedException">The writer has been disposed of.</exception>
    /// <exception cref="IOException">The temporary file could not be written.</exception>
    /// <param name="plane">The zero-based index of the plane, 0 for an image of one or two axes.</param>
    /// <param name="firstRow">The zero-based index, in the plane, of the first row of the band.</param>
    /// <param name="rows">The values of the band, the rows one after the other, each of <see cref="RowLength"/> values.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    public void WriteRows(long plane, long firstRow, ReadOnlySpan<T> rows, CancellationToken cancellationToken = default)
    {
        EnsureWriting();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_plane == PlaneCount) throw new ArgumentException("Every row of the image has already been written.", nameof(plane));
            if (plane != _plane || firstRow != _row)
                throw new ArgumentException(
                    $"The rows are written in file order: the next one is row {_row} of plane {_plane}, not row {firstRow} of plane {plane}.",
                    plane != _plane ? nameof(plane) : nameof(firstRow));
            if (rows.IsEmpty || rows.Length % RowLength != 0)
                throw new ArgumentException($"A band is made of whole rows of {RowLength} values; {rows.Length} values were given.", nameof(rows));
            long count = rows.Length / RowLength;
            if (count > RowsPerPlane - _row)
                throw new ArgumentException(
                    $"Plane {_plane} has {RowsPerPlane - _row} rows left to write, not {count}: a band holds rows of one plane.", nameof(rows));

            Encode(rows, cancellationToken);
            _row += count;
            if (_row == RowsPerPlane)
            {
                _plane++;
                _row = 0;
            }
        }
        catch
        {
            Abort();
            throw;
        }
    }

    /// <summary>The bytes of <paramref name="values"/> to the temporary file (and to the data sum), a block at a time.</summary>
    private void Encode(ReadOnlySpan<T> values, CancellationToken cancellationToken)
    {
        int size = Unsafe.SizeOf<T>();
        int perBlock = BlockBytes / size;
        _buffer ??= ArrayPool<byte>.Shared.Rent((int)Math.Min(BlockBytes, _dataLength));
        for (int start = 0; start < values.Length; start += perBlock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = Math.Min(perBlock, values.Length - start);
            Span<byte> block = _buffer.AsSpan(0, n * size);
            PlaneDataSource<T>.Encode(values.Slice(start, n), block, _flipTopBit);
            _stream!.Write(block);
            _sum?.Write(block);
        }
    }

    /// <summary>
    /// Finishes the file and publishes it: pads the data to a whole block, fills in <c>CHECKSUM</c> and <c>DATASUM</c>, flushes the
    /// temporary file to the disk and moves it to the path, replacing an existing file unless the mode is
    /// <see cref="FitsSaveMode.CreateNew"/>. <paramref name="cancellationToken"/> is checked until the file is moved in place; the move is
    /// not interrupted.
    /// </summary>
    /// <exception cref="InvalidOperationException">Rows are missing (nothing is published), the writer has already completed, or an earlier call failed.</exception>
    /// <exception cref="FitsFileExistsException">The mode is <see cref="FitsSaveMode.CreateNew"/> and something has appeared at the path meanwhile: it is kept.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the file was put in place.</exception>
    /// <exception cref="ObjectDisposedException">The writer has been disposed of.</exception>
    /// <exception cref="IOException">The file could not be written or put in place.</exception>
    /// <param name="cancellationToken">The token that cancels the completion.</param>
    public void Complete(CancellationToken cancellationToken = default)
    {
        EnsureWriting();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_plane < PlaneCount)
                throw new InvalidOperationException(
                    $"The image is not complete: row {_row} of plane {_plane} is the next to write, and {Missing()} row(s) are missing. Nothing was published.");
            var stream = _stream!;
            long pad = HduLayout.Padded(_dataLength) - _dataLength;
            if (pad > 0)
            {
                Span<byte> fill = stackalloc byte[(int)pad];   // less than a block, and zeros: the fill of an image
                fill.Clear();
                stream.Write(fill);
                _sum?.Write(fill);
            }
            if (_sum is not null)
            {
                _image.SetChecksums(_sum.Result);
                byte[] header = _image.Header.ToBytes();
                if (header.Length != _headerLength) throw new InvalidOperationException("The header changed length when its checksums were set.");
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(header);
            }
            stream.Flush(flushToDisk: true);
            stream.Dispose();
            _stream = null;
            cancellationToken.ThrowIfCancellationRequested(); // the last point at which the write can be cancelled
            FitsFile.PublishTemporary(_temp, _target, _createNew);
        }
        catch
        {
            Abort();
            throw;
        }
        _state = State.Completed;
        ReturnBuffer();
    }

    private long Missing() => (PlaneCount - _plane) * RowsPerPlane - _row;

    /// <summary>Abandons the write if it has not completed: the temporary file is deleted and nothing is published.</summary>
    public void Dispose()
    {
        if (_state == State.Writing) Abort();
        _state = State.Disposed;
    }

    private void EnsureWriting()
    {
        switch (_state)
        {
            case State.Disposed:
                throw new ObjectDisposedException(typeof(FitsImageWriter<T>).ToString());
            case State.Completed:
                throw new InvalidOperationException("The image has already been completed.");
            case State.Failed:
                throw new InvalidOperationException("An earlier call failed or was cancelled: the write was abandoned and nothing was published.");
        }
    }

    /// <summary>Deletes the temporary file and releases the buffer; the writer can no longer be used.</summary>
    private void Abort()
    {
        _state = State.Failed;
        if (_stream is not null)
        {
            try
            {
                _stream.Dispose();   // flushes what is buffered, which can fail (a full disk): the file is deleted anyway
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
            _stream = null;
        }
        FitsFile.TryDelete(_temp);
        ReturnBuffer();
    }

    private void ReturnBuffer()
    {
        if (_buffer is null) return;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = null;
    }
}
