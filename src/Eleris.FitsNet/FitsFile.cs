using System.Collections;
using System.Globalization;
using System.Text;

namespace Eleris.FitsNet;

/// <summary>
/// A FITS file: a primary HDU followed by zero or more extensions. Not thread-safe.
/// HDUs belong to one file at a time: do not share an HDU (it shares its <see cref="Hdu.Header"/>) between files.
/// </summary>
public sealed class FitsFile : IReadOnlyList<Hdu>, IDisposable
{
    private readonly List<Hdu> _hdus = [];
    private Stream? _source;
    private string? _sourcePath;
    private bool _disposed;

    /// <summary>Creates an empty file; the first HDU added becomes the primary HDU.</summary>
    public FitsFile() { }

    /// <summary>The number of HDUs.</summary>
    public int Count => _hdus.Count;

    /// <summary>The HDU at a zero-based index (0 is the primary HDU).</summary>
    /// <param name="index">The zero-based index of the HDU (0 is the primary HDU).</param>
    public Hdu this[int index] => _hdus[index];

    /// <summary>The primary HDU.</summary>
    public Hdu Primary => _hdus.Count > 0 ? _hdus[0] : throw new InvalidOperationException("The file has no HDU.");

    /// <summary>Enumerates the HDUs in file order.</summary>
    public IEnumerator<Hdu> GetEnumerator() => _hdus.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ---- Editing -------------------------------------------------------------------------------------------------

    /// <summary>Appends an HDU. An empty primary image is inserted first if the file is empty and the HDU cannot be primary.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="hdu"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The HDU is already part of this file.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <param name="hdu">The HDU to add.</param>
    public FitsFile Add(Hdu hdu) => Insert(_hdus.Count, hdu);

    /// <summary>
    /// Inserts an HDU. Inserting at index 0 makes the current primary an extension, and a non-image HDU inserted at
    /// index 0 gets an empty primary image in front of it (shifting later indexes by one).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or greater than the number of HDUs.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="hdu"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The HDU is already part of this file, a random groups HDU would not be the primary HDU, or the current primary is a random groups HDU and <paramref name="index"/> is 0.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <param name="index">The zero-based position of the new HDU.</param>
    /// <param name="hdu">The HDU to insert.</param>
    public FitsFile Insert(int index, Hdu hdu)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(hdu);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _hdus.Count);
        if (_hdus.Contains(hdu)) throw new ArgumentException("The HDU is already part of this file.", nameof(hdu));
        if (index > 0 && hdu is RandomGroupsHdu)
            throw new ArgumentException("A random groups HDU can only be the primary HDU.", nameof(hdu));
        if (index == 0 && _hdus.Count > 0 && _hdus[0] is RandomGroupsHdu)
            throw new ArgumentException("The current primary HDU is a random groups HDU and cannot become an extension.", nameof(index));

        // Data still backed by another file's stream would break when that file is disposed: take a private copy.
        if (hdu.Source is StreamDataSource sds && !ReferenceEquals(sds.Stream, _source))
            hdu.Source = new MemoryDataSource(sds.ReadAll());

        if (index == 0 && !hdu.CanBePrimary)
        {
            _hdus.Insert(0, new ImageHdu());
            index = 1;
        }
        _hdus.Insert(index, hdu);
        return this;
    }

    /// <summary>
    /// Removes the HDU at an index.
    /// </summary>
    /// <exception cref="InvalidOperationException">The next HDU cannot become the primary HDU.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not the index of an HDU.</exception>
    /// <exception cref="ObjectDisposedException">The file is disposed.</exception>
    /// <param name="index">The zero-based index of the HDU to remove.</param>
    public void RemoveAt(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index == 0 && _hdus.Count > 1 && !_hdus[1].CanBePrimary)
            throw new InvalidOperationException("The next HDU cannot become the primary HDU; replace the primary with an empty image instead.");
        _hdus.RemoveAt(index);
    }

    /// <summary>Removes an HDU; returns false if it is not part of this file. Same restrictions as <see cref="RemoveAt"/>.</summary>
    /// <param name="hdu">The HDU to remove.</param>
    public bool Remove(Hdu hdu)
    {
        int index = _hdus.IndexOf(hdu);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    // ---- Reading -------------------------------------------------------------------------------------------------

    /// <summary>Opens a file lazily: pixel data is read from disk on demand. Dispose the result to release the file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The reading options, or null for the defaults.</param>
    public static FitsFile Open(string path, FitsReadOptions? options = null)
    {
        var stream = OpenForRead(path);
        try
        {
            var file = ReadCore(stream, options ?? FitsReadOptions.Default, lazy: true);
            file._source = stream;
            file._sourcePath = ResolvePath(path);
            return file;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static FileStream OpenForRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess); // small buffer: scattered region reads must not fill 64 KB each time

    /// <summary>Reads a whole file from a stream into memory. The stream is left open.</summary>
    /// <param name="stream">The stream to read from, positioned at the start of the file; it is left open.</param>
    /// <param name="options">The reading options, or null for the defaults.</param>
    public static FitsFile Read(Stream stream, FitsReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= FitsReadOptions.Default;
        if (options.MaxInputBytes < 0) throw new ArgumentOutOfRangeException(nameof(options), "MaxInputBytes cannot be negative.");
        if (options.MaxTotalHeaderBlocks < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxTotalHeaderBlocks must be at least 1.");
        long limit = Math.Min(options.MaxInputBytes, Array.MaxLength);
        string advice = limit == Array.MaxLength ? ": use FitsFile.Open, which reads on demand" : " (MaxInputBytes)";
        string size = limit.ToString("N0", CultureInfo.InvariantCulture);
        // One buffer for the whole file: every HDU is a slice of it, so the file is held in memory once.
        byte[] buffer;
        int length;
        try
        {
            if (stream.CanSeek)
            {
                long remaining = Math.Max(0, stream.Length - stream.Position);
                if (remaining > limit) throw new FitsException($"The file is larger than {size} bytes{advice}.");
                buffer = new byte[remaining];
                stream.ReadExactly(buffer);
                length = buffer.Length;
            }
            else
            {
                // a stream of unknown length is copied block by block, so that the budget is checked before the memory is taken
                var copy = new MemoryStream();
                var block = new byte[81920];
                int read;
                while ((read = stream.Read(block, 0, block.Length)) > 0)
                {
                    if (copy.Length + read > limit)
                        throw new FitsException($"The stream is larger than {size} bytes{advice}.");
                    copy.Write(block, 0, read);
                }
                buffer = copy.GetBuffer(); // the capacity can exceed the length: only [0, length) is used
                length = (int)copy.Length;
            }
        }
        catch (OutOfMemoryException e)
        {
            throw new FitsException("There is not enough memory to read this stream.", e);
        }
        return ReadCore(new MemoryStream(buffer, 0, length, writable: false), options, lazy: false, buffer);
    }

    private static FitsFile ReadCore(Stream s, FitsReadOptions options, bool lazy, byte[]? shared = null)
    {
        var file = new FitsFile();
        Span<byte> peek = stackalloc byte[8];

        long headerBlocks = 0;
        while (s.Position < s.Length)
        {
            long start = s.Position;
            int n = s.ReadAtLeast(peek, peek.Length, throwOnEndOfStream: false);
            s.Position = start;
            string key = Encoding.ASCII.GetString(peek[..n]);
            bool first = file._hdus.Count == 0;

            if (first && key != "SIMPLE  ")
                throw new FitsException("Not a FITS file: the first card must be SIMPLE.");
            if (!first && key != "XTENSION")
            {
                if (options.Strict) throw new FitsException("Unexpected data after the last HDU.");
                break; // lenient: ignore trailing bytes that do not start an extension
            }

            var rawHeader = new MemoryStream();
            var header = FitsHeader.ReadCore(s, options, allowEndOfStream: false, rawHeader)!;
            headerBlocks += rawHeader.Length / FitsHeader.BlockSize;
            if (headerBlocks > options.MaxTotalHeaderBlocks)
                throw new FitsException($"The headers of the file take more than {options.MaxTotalHeaderBlocks.ToString("N0", CultureInfo.InvariantCulture)} blocks (MaxTotalHeaderBlocks).");
            if (first && options.Strict && header.Get<bool>("SIMPLE") is false)
                throw new FitsException("SIMPLE must be T in a conforming file.");

            var layout = HduLayout.Parse(header, first, options.Strict);
            long dataStart = s.Position;
            long available = s.Length - dataStart;
            if (layout.DataLength > available)
                throw new FitsException($"The file is truncated: HDU {file._hdus.Count} announces {layout.DataLength} data bytes.");
            long padded = HduLayout.Padded(layout.DataLength);
            if (options.Strict && padded > available)
                throw new FitsException($"The file is truncated: the padding of HDU {file._hdus.Count} is missing.");

            DataSource source;
            if (lazy)
            {
                source = new StreamDataSource(s, dataStart, layout.DataLength);
            }
            else
            {
                if (layout.DataLength > Array.MaxLength) throw new FitsException("HDU data is larger than 2 GB.");
                if (shared is not null)
                {
                    source = new SegmentDataSource(shared, (int)dataStart, (int)layout.DataLength);
                }
                else
                {
                    var buffer = new byte[layout.DataLength];
                    s.ReadExactly(buffer);
                    source = new MemoryDataSource(buffer);
                }
            }

            Hdu hdu;
            if (first) hdu = layout.IsRandomGroups ? new RandomGroupsHdu(header, source) : new ImageHdu(header, source);
            else if (layout.Xtension is "IMAGE" or "IUEIMAGE") hdu = new ImageHdu(header, source);
            else if (layout.Xtension is "BINTABLE" or "A3DTABLE" && CompressedTableHdu.IsCompressedTable(header))
                hdu = new CompressedTableHdu(header, source);
            else if (layout.Xtension is "BINTABLE" or "A3DTABLE")
                hdu = CompressedImageHdu.IsCompressedImage(header) ? new CompressedImageHdu(header, source) : new BinaryTableHdu(header, source);
            else if (layout.Xtension == "TABLE") hdu = new AsciiTableHdu(header, source);
            else hdu = new GenericHdu(header, source);
            hdu.OriginalSource = source;
            if (header.Contains("CHECKSUM") || header.Contains("DATASUM"))
            {
                hdu.OriginalHeader = rawHeader.ToArray();
                hdu.CleanVersion = header.Version;
            }
            long fillLength = padded - layout.DataLength;
            if (fillLength > 0)
            {
                int have = (int)Math.Min(fillLength, available - layout.DataLength);
                s.Position = dataStart + layout.DataLength;
                var fill = new byte[have];
                s.ReadExactly(fill);
                if (have < fillLength || fill.Any(x => x != hdu.PaddingByte))
                {
                    hdu.OriginalFill = fill;
                    hdu.FillTruncated = have < fillLength;
                }
            }
            file._hdus.Add(hdu);

            s.Position = dataStart + Math.Min(padded, available);
        }

        if (file._hdus.Count == 0) throw new FitsException("The stream is empty.");
        return file;
    }

    // ---- Writing -------------------------------------------------------------------------------------------------

    /// <summary>Checks CHECKSUM and DATASUM of every HDU (see <see cref="Hdu.VerifyChecksum"/>).</summary>
    public IReadOnlyList<ChecksumResult> VerifyChecksums() => _hdus.Select(h => h.VerifyChecksum()).ToList();

    /// <summary>Writes the file to a stream.</summary>
    /// <param name="stream">The destination.</param>
    /// <param name="checksums">What to do with the CHECKSUM and DATASUM keywords.</param>
    public void Save(Stream stream, ChecksumPolicy checksums = ChecksumPolicy.Auto) => Save(stream, checksums, CancellationToken.None);

    /// <summary>
    /// Writes the file to a stream, checking <paramref name="cancellationToken"/> between blocks of data. A cancelled save throws an
    /// <see cref="OperationCanceledException"/> and leaves the headers and the state of the HDUs as they were, but the stream may already
    /// hold the beginning of the file: discard it.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the file was written completely.</exception>
    /// <param name="stream">The destination.</param>
    /// <param name="checksums">What to do with the CHECKSUM and DATASUM keywords.</param>
    /// <param name="cancellationToken">The token that cancels the save.</param>
    public void Save(Stream stream, ChecksumPolicy checksums, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_disposed, this);
        WriteCore(stream, checksums, cancellationToken).Commit();
    }

    /// <summary>
    /// Writes the file to a stream asynchronously: the data of a lazily opened file is read asynchronously and every block is written
    /// with <see cref="Stream.WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/>; the conversion of the data runs between the writes.
    /// The bytes written are those of <see cref="Save(Stream, ChecksumPolicy, CancellationToken)"/>, with the same cancellation: the
    /// headers and the state of the HDUs are restored, but the stream may already hold the beginning of the file. Like every member of this
    /// class, it must not run at the same time as another use of the file or of its HDUs: do not edit them until the task
    /// has completed.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the file was written completely.</exception>
    /// <param name="stream">The destination.</param>
    /// <param name="checksums">What to do with the CHECKSUM and DATASUM keywords.</param>
    /// <param name="cancellationToken">A token that cancels the save.</param>
    public async Task SaveAsync(Stream stream, ChecksumPolicy checksums = ChecksumPolicy.Auto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_disposed, this);
        (await WriteCoreAsync(stream, checksums, cancellationToken).ConfigureAwait(false)).Commit();
    }

    /// <summary>
    /// A file that has been written but not yet published: <see cref="Commit"/> makes the saved form the reference of the in-memory
    /// HDUs, <see cref="Rollback"/> puts their headers back as they were.
    /// </summary>
    private sealed record SaveTransaction(long[] Offsets, Action Commit, Action Rollback)
    {
        public static SaveTransaction Empty { get; } = new([], () => { }, () => { });
    }

    /// <summary>
    /// The state of a save in progress, shared by the synchronous and the asynchronous writes: the headers as they were (to roll back),
    /// the header bytes and data offsets written (to commit), and the position in the output.
    /// </summary>
    /// <remarks>
    /// Normalizing and checksumming rewrite headers in place; every header is rolled back if anything fails (invalid HDU, unreadable data,
    /// failing output, or a file that cannot be put in place) so a failed save leaves the in-memory model untouched. A successful save does
    /// update the headers (mandatory keywords, checksums) and makes them the reference.
    /// </remarks>
    private sealed class SaveState(IReadOnlyList<Hdu> hdus)
    {
        private readonly List<List<FitsCard>> _snapshots = hdus.Select(h => h.Header.Cards.ToList()).ToList();
        private readonly List<bool> _wasClean = hdus.Select(h => h.IsClean).ToList();
        private readonly byte[][] _headerBytes = new byte[hdus.Count][];
        private readonly long[] _offsets = new long[hdus.Count];
        private long _position;

        /// <summary>Makes the mandatory keywords consistent and checks that each header describes its data.</summary>
        public void Normalize()
        {
            for (int i = 0; i < hdus.Count; i++)
            {
                hdus[i].Normalize(i == 0, hdus.Count > 1);
                hdus[i].ValidateForWrite(i == 0);
            }
        }

        /// <summary>
        /// An HDU that is as it was read keeps its checksum keywords untouched when its header bytes are unchanged (so an unedited file is
        /// rewritten identically) and when they were wrong (so corruption is never hidden).
        /// </summary>
        public static bool Preserve(Hdu hdu, (bool Clean, bool AnyInvalid) state) =>
            state.Clean && (state.AnyInvalid || hdu.HeaderBytesEqualOriginal());

        /// <summary>The header of HDU <paramref name="i"/>, to be written now, followed by its data.</summary>
        public byte[] Header(int i)
        {
            byte[] header = hdus[i].Header.ToBytes();
            _headerBytes[i] = header;
            _position += header.Length;
            _offsets[i] = _position;
            return header;
        }

        /// <summary>The fill to write after the data of HDU <paramref name="i"/> (empty when there is none).</summary>
        public byte[] Fill(int i)
        {
            var hdu = hdus[i];
            long pad = HduLayout.Padded(hdu.DataLength) - hdu.DataLength;
            _position += hdu.DataLength + pad;
            if (pad == 0) return [];
            var padding = new byte[pad];
            if (hdu.PaddingByte != 0) Array.Fill(padding, hdu.PaddingByte);
            return padding;
        }

        public void Restore()
        {
            for (int i = 0; i < hdus.Count; i++)
            {
                hdus[i].Header.ReplaceAll(_snapshots[i]);
                if (_wasClean[i]) hdus[i].CleanVersion = hdus[i].Header.Version; // the cards are what they were: so is "unchanged"
            }
        }

        public SaveTransaction Transaction() => new(_offsets, Commit, Restore);

        private void Commit()
        {
            for (int i = 0; i < hdus.Count; i++)
            {
                // The saved form is now the reference for "unchanged": its header bytes, its header version and its data (the data
                // source is updated by Rebind when the file replaces the one it was read from). The fill written is the canonical one.
                var hdu = hdus[i];
                bool hasSums = hdu.Header.Contains("CHECKSUM") || hdu.Header.Contains("DATASUM");
                hdu.OriginalHeader = hasSums ? _headerBytes[i] : null;
                hdu.CleanVersion = hdu.Header.Version;
                hdu.OriginalSource = hdu.Source;
                hdu.OriginalFill = null;
                hdu.FillTruncated = false;
            }
        }
    }

    /// <summary>Writes every HDU. The headers keep the changes of the save until the transaction is committed or rolled back.</summary>
    private SaveTransaction WriteCore(Stream stream, ChecksumPolicy checksums, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_hdus.Count == 0)
        {
            new ImageHdu().Header.Write(stream);
            return SaveTransaction.Empty;
        }

        var hdus = _hdus.ToArray();
        var save = new SaveState(hdus);
        var states = hdus.Select(h => checksums == ChecksumPolicy.Auto ? h.CaptureChecksumState(cancellationToken) : (false, false)).ToList();
        try
        {
            save.Normalize();
            for (int i = 0; i < hdus.Length; i++)
                hdus[i].ApplyChecksumPolicy(checksums, SaveState.Preserve(hdus[i], states[i]), cancellationToken);
            for (int i = 0; i < hdus.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Write(save.Header(i));
                hdus[i].Source.CopyTo(stream, cancellationToken);
                byte[] fill = save.Fill(i);
                if (fill.Length > 0) stream.Write(fill);
            }
        }
        catch
        {
            save.Restore();
            throw;
        }
        return save.Transaction();
    }

    /// <summary>Asynchronous version of <see cref="WriteCore"/>: the same bytes, the same rollback.</summary>
    private async Task<SaveTransaction> WriteCoreAsync(Stream stream, ChecksumPolicy checksums, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_hdus.Count == 0)
        {
            await new ImageHdu().Header.WriteAsync(stream, cancellationToken).ConfigureAwait(false);
            return SaveTransaction.Empty;
        }

        var hdus = _hdus.ToArray();   // the HDUs the save started with, whatever the caller does meanwhile
        var save = new SaveState(hdus);
        var states = new (bool Clean, bool AnyInvalid)[hdus.Length];
        if (checksums == ChecksumPolicy.Auto)
            for (int i = 0; i < hdus.Length; i++) states[i] = await hdus[i].CaptureChecksumStateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            save.Normalize();
            for (int i = 0; i < hdus.Length; i++)
                await hdus[i].ApplyChecksumPolicyAsync(checksums, SaveState.Preserve(hdus[i], states[i]), cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < hdus.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await stream.WriteAsync(save.Header(i), cancellationToken).ConfigureAwait(false);
                await hdus[i].Source.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
                byte[] fill = save.Fill(i);
                if (fill.Length > 0) await stream.WriteAsync(fill, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            save.Restore();
            throw;
        }
        return save.Transaction();
    }

    /// <summary>
    /// Writes the file to <paramref name="path"/>: a temporary file next to the target is written completely, flushed,
    /// then moved over it, so a failure leaves the original untouched. When overwriting the file this instance was
    /// opened from, the lazily read HDUs are re-attached to the new file. HDUs previously removed from this file
    /// lose access to their data in that case. An existing target keeps its attributes and permissions (File.Replace), and a symbolic
    /// link is written through to its target. A save that fails leaves the headers and the state of the HDUs as they were.
    /// </summary>
    /// <exception cref="IOException">
    /// The file could not be written or put in place. When the file this instance reads from was being replaced and could not be
    /// reopened afterwards (or, in the worst case, after a failed replacement), the instance can no longer read its data: dispose it and open
    /// the file again; the message says which case it is.
    /// </exception>
    /// <param name="path">The path of the file to write.</param>
    /// <param name="checksums">What to do with the <c>CHECKSUM</c> and <c>DATASUM</c> keywords.</param>
    public void Save(string path, ChecksumPolicy checksums = ChecksumPolicy.Auto) =>
        SaveToPath(path, checksums, FitsSaveMode.Overwrite, CancellationToken.None);

    /// <summary>
    /// Writes the file to <paramref name="path"/> as <see cref="Save(string, ChecksumPolicy)"/> does, checking
    /// <paramref name="cancellationToken"/> between blocks of data. A cancelled save deletes its temporary file and leaves the target, the
    /// headers and the state of the HDUs as they were. Once the temporary file is complete and is being put in place, the save is no longer
    /// cancelled.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the file was put in place.</exception>
    /// <exception cref="IOException">The file could not be written or put in place (see <see cref="Save(string, ChecksumPolicy)"/>).</exception>
    /// <param name="path">The path of the file to write.</param>
    /// <param name="checksums">What to do with the <c>CHECKSUM</c> and <c>DATASUM</c> keywords.</param>
    /// <param name="cancellationToken">The token that cancels the save.</param>
    public void Save(string path, ChecksumPolicy checksums, CancellationToken cancellationToken) =>
        SaveToPath(path, checksums, FitsSaveMode.Overwrite, cancellationToken);

    /// <summary>
    /// Writes the file to <paramref name="path"/>, choosing with <paramref name="mode"/> what happens when a file already exists there.
    /// <see cref="FitsSaveMode.Overwrite"/> is <see cref="Save(string, ChecksumPolicy, CancellationToken)"/>.
    /// <see cref="FitsSaveMode.CreateNew"/> never replaces anything: the file is written to a temporary file in the same directory, then
    /// put in place by an operation that fails if the path exists, so a file that appears while the save runs is not replaced either (no
    /// check-then-write race), and of concurrent <see cref="FitsSaveMode.CreateNew"/> saves to one path exactly one succeeds. On Windows that
    /// operation is the move itself. On Unix, where the move of .NET checks the path then renames, the name is first claimed with an
    /// exclusive creation (<c>O_CREAT | O_EXCL</c>), then the temporary file is renamed over the claim; until that rename, a 16-byte file
    /// is at the path, and a process killed in between leaves it there. In that mode <paramref name="path"/> is taken as it is (a symbolic link there counts as
    /// an existing file), and a failure, <see cref="FitsFileExistsException"/> included, leaves the headers and the state of the HDUs as
    /// they were.
    /// </summary>
    /// <exception cref="FitsFileExistsException"><paramref name="mode"/> is <see cref="FitsSaveMode.CreateNew"/> and something exists at <paramref name="path"/>, before or after the file was written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the file was put in place.</exception>
    /// <exception cref="IOException">The file could not be written or put in place (see <see cref="Save(string, ChecksumPolicy)"/>).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a value of <see cref="FitsSaveMode"/>.</exception>
    /// <param name="path">The path of the file to write.</param>
    /// <param name="checksums">What to do with the <c>CHECKSUM</c> and <c>DATASUM</c> keywords.</param>
    /// <param name="mode">Whether an existing file may be replaced.</param>
    /// <param name="cancellationToken">The token that cancels the save.</param>
    public void Save(string path, ChecksumPolicy checksums, FitsSaveMode mode, CancellationToken cancellationToken = default) =>
        SaveToPath(path, checksums, mode, cancellationToken);

    /// <summary>
    /// Writes the file to <paramref name="path"/> asynchronously, with the guarantees of
    /// <see cref="Save(string, ChecksumPolicy, FitsSaveMode, CancellationToken)"/>: the temporary file is written with asynchronous I/O
    /// (and the data of a lazily opened file read asynchronously), a cancelled or failed save leaves no temporary file, an existing target
    /// untouched and the headers as they were, and <see cref="FitsSaveMode.CreateNew"/> never replaces a file. Flushing the temporary file
    /// to the disk and moving it in place are done synchronously: the system offers no asynchronous version of them, and the flush of a large
    /// file can take a noticeable time. Do not use the file or its HDUs until the task has completed.
    /// </summary>
    /// <exception cref="FitsFileExistsException"><paramref name="mode"/> is <see cref="FitsSaveMode.CreateNew"/> and something exists at <paramref name="path"/>, before or after the file was written.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the file was put in place.</exception>
    /// <exception cref="IOException">The file could not be written or put in place (see <see cref="Save(string, ChecksumPolicy)"/>).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a value of <see cref="FitsSaveMode"/>.</exception>
    /// <param name="path">The path of the file to write.</param>
    /// <param name="checksums">What to do with the <c>CHECKSUM</c> and <c>DATASUM</c> keywords.</param>
    /// <param name="mode">Whether an existing file may be replaced.</param>
    /// <param name="cancellationToken">A token that cancels the save.</param>
    public async Task SaveAsync(string path, ChecksumPolicy checksums = ChecksumPolicy.Auto, FitsSaveMode mode = FitsSaveMode.Overwrite,
        CancellationToken cancellationToken = default)
    {
        var target = BeginPathSave(path, mode, cancellationToken);
        SaveTransaction? written = null;
        try
        {
            var fs = new FileStream(target.Temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
            await using (fs.ConfigureAwait(false))
            {
                written = await WriteCoreAsync(fs, checksums, cancellationToken).ConfigureAwait(false);
                await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
                fs.Flush(flushToDisk: true);   // no asynchronous version: the buffer is empty, this only asks the system to write to the disk
            }
            cancellationToken.ThrowIfCancellationRequested(); // the last point at which the save can be cancelled
        }
        catch
        {
            written?.Rollback();   // when WriteCoreAsync itself failed it has already restored the headers, and written is null
            TryDelete(target.Temp);
            throw;
        }
        PublishSaved(target, written);
    }

    private void SaveToPath(string path, ChecksumPolicy checksums, FitsSaveMode mode, CancellationToken cancellationToken)
    {
        var target = BeginPathSave(path, mode, cancellationToken);
        SaveTransaction? written = null;
        try
        {
            using var fs = new FileStream(target.Temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
            written = WriteCore(fs, checksums, cancellationToken);
            fs.Flush(true);
            cancellationToken.ThrowIfCancellationRequested(); // the last point at which the save can be cancelled
        }
        catch
        {
            written?.Rollback();   // when WriteCore itself failed it has already restored the headers, and written is null
            TryDelete(target.Temp);
            throw;
        }
        PublishSaved(target, written);
    }

    /// <summary>Where a save to a path writes, and how it puts the file in place.</summary>
    private sealed record PathSave(string Full, string Temp, bool CreateNew, bool OverwritingSource);

    private PathSave BeginPathSave(string path, FitsSaveMode mode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (mode is not (FitsSaveMode.Overwrite or FitsSaveMode.CreateNew))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown save mode.");
        bool createNew = mode == FitsSaveMode.CreateNew;
        cancellationToken.ThrowIfCancellationRequested();

        var (full, temp) = SaveTarget(path, createNew);
        // In CreateNew mode the source file is never the target (it exists); should it have been deleted meanwhile, the HDUs keep reading it.
        bool overwritingSource = !createNew && _sourcePath is not null && string.Equals(full, _sourcePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        return new PathSave(full, temp, createNew, overwritingSource);
    }

    /// <summary>
    /// The path a save writes and the temporary file it writes first, next to it (shared with <see cref="FitsImageWriter{T}"/>). A new file
    /// is created where the caller said, link or not; an overwrite writes through a symbolic link to its target.
    /// </summary>
    /// <exception cref="FitsFileExistsException"><paramref name="createNew"/> is set and something is at the path already.</exception>
    internal static (string Full, string Temp) SaveTarget(string path, bool createNew)
    {
        string full = createNew ? Path.GetFullPath(path) : ResolvePath(path);
        if (createNew && ExistsAnything(full)) throw new FitsFileExistsException(full); // an early answer only: the final move decides
        return (full, full + ".tmp-" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>Puts a complete temporary file in place: <see cref="PublishNew"/> for a new file, else over the target.</summary>
    internal static void PublishTemporary(string temp, string target, bool createNew)
    {
        if (createNew) PublishNew(temp, target);
        else Publish(temp, target);
    }

    /// <summary>Puts the complete temporary file in place and commits the save, or rolls it back.</summary>
    private void PublishSaved(PathSave target, SaveTransaction transaction)
    {
        string full = target.Full, temp = target.Temp;
        if (!target.OverwritingSource)
        {
            try
            {
                PublishTemporary(temp, full, target.CreateNew);
            }
            catch
            {
                TryDelete(temp);
                transaction.Rollback();
                throw;
            }
            transaction.Commit();
            return;
        }

        // The old file is still read through _source: release it, replace the file, then re-attach.
        var oldStream = _source!;
        oldStream.Dispose();
        try
        {
            Publish(temp, full);
        }
        catch (Exception published)
        {
            TryDelete(temp);
            transaction.Rollback();
            try
            {
                Rebind(OpenForRead(full), oldStream, null); // the original file is intact: re-attach to it
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Neither the new file nor the original can be read through this instance any more.
                throw new IOException(
                    $"'{full}' could not be replaced ({published.Message}) and the original could not be reopened: dispose this FitsFile and open the file again.",
                    new AggregateException(published, e));
            }
            throw;
        }
        transaction.Commit();
        try
        {
            Rebind(OpenForRead(full), oldStream, transaction.Offsets);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"'{full}' was saved but could not be reopened: dispose this FitsFile and open the file again. {e.Message}", e);
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // best effort: the caller's error is the one that matters
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Full path, with symbolic links resolved.</summary>
    private static string ResolvePath(string path)
    {
        string full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        if (info.LinkTarget is null) return full; // not a link, or not there yet: no exception to pay for
        try
        {
            return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
        }
        catch (IOException)
        {
            return full;
        }
    }

    /// <summary>True when a file, a directory or a link (even a broken one) is at <paramref name="path"/>.</summary>
    private static bool ExistsAnything(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return true;
        try
        {
            return new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Moves the finished temporary file to a target that must not exist, without ever replacing a file created by someone else after the
    /// early check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On Windows, <c>File.Move(overwrite: false)</c> is <c>MoveFileEx</c> without <c>MOVEFILE_REPLACE_EXISTING</c>: the move itself refuses
    /// an existing target, atomically.
    /// </para>
    /// <para>
    /// On Unix, <c>File.Move(overwrite: false)</c> is not atomic: .NET checks the target with <c>lstat</c>, then calls <c>rename</c>, which
    /// replaces a file created in between (two concurrent saves both succeed, the last one wins). The name is therefore claimed first with
    /// <see cref="FileMode.CreateNew"/>, that is <c>open(O_CREAT | O_EXCL)</c>, which only one process can win and which fails on any existing
    /// entry, a symbolic link included. The temporary file is then renamed over this claim, which is ours. The claim holds a random token,
    /// so that a failed write or rename removes it only if it is still ours. What remains: from the claim to the rename, a 16-byte file
    /// exists at the target (a directory watcher sees it created); a process killed in between leaves that 16-byte file, which then makes every
    /// <see cref="FitsSaveMode.CreateNew"/> save to the path fail until it is deleted; and a writer that does not use an exclusive creation
    /// (an <see cref="FitsSaveMode.Overwrite"/> save, a program that renames its file into place) and writes the path in between has its file
    /// replaced.
    /// </para>
    /// </remarks>
    internal static void PublishNew(string temp, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                File.Move(temp, target, overwrite: false);
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && ExistsAnything(target))
            {
                throw new FitsFileExistsException(target, e);
            }
            return;
        }

        byte[] token = Guid.NewGuid().ToByteArray();
        FileStream claim;
        try
        {
            // FileShare.Read: no lock is needed (O_EXCL already decides), and a reader of the claim gets its bytes, not a sharing error
            claim = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && ExistsAnything(target))
        {
            throw new FitsFileExistsException(target, e);
        }
        try
        {
            using (claim) claim.Write(token);
            File.Move(temp, target, overwrite: true);   // rename(2): replaces our own claim, atomically
        }
        catch
        {
            RemoveClaim(target, token);   // a failed write (disk full) or rename: the claim must not stay in place of a file
            throw;
        }
    }

    /// <summary>
    /// Deletes the claim of <see cref="PublishNew"/> if the file at <paramref name="target"/> is still that claim, whole or partly written
    /// (a prefix of <paramref name="token"/>). Between the check and the deletion another program could replace it: a window of two system
    /// calls, after a failure that is already rare.
    /// </summary>
    internal static void RemoveClaim(string target, byte[] token)
    {
        try
        {
            var info = new FileInfo(target);
            if (!info.Exists || info.LinkTarget is not null || info.Length > token.Length) return;
            var content = new byte[info.Length];
            using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                content = content[..stream.ReadAtLeast(content, content.Length, throwOnEndOfStream: false)];
            if (content.Length == info.Length && token.AsSpan().StartsWith(content)) File.Delete(target);
        }
        catch (Exception)
        {
            // best effort: the caller's error is the one that matters
        }
    }

    /// <summary>Moves the finished temporary file over the target; an existing target keeps its attributes and permissions.</summary>
    private static void Publish(string temp, string target)
    {
        if (File.Exists(target)) File.Replace(temp, target, destinationBackupFileName: null);
        else File.Move(temp, target);
    }

    private void Rebind(FileStream newStream, Stream oldStream, long[]? newOffsets)
    {
        for (int i = 0; i < _hdus.Count; i++)
        {
            if (_hdus[i].Source is StreamDataSource sds && ReferenceEquals(sds.Stream, oldStream))
            {
                var previous = _hdus[i].Source;
                var reattached = new StreamDataSource(newStream, newOffsets?[i] ?? sds.Offset, sds.Length);
                _hdus[i].Source = reattached;
                if (ReferenceEquals(_hdus[i].OriginalSource, previous)) _hdus[i].OriginalSource = reattached; // still "as written"
            }
        }
        _source = newStream;
    }

    /// <summary>Releases the source file of a lazily opened file; HDUs still reading from it become unusable.</summary>
    public void Dispose()
    {
        _disposed = true;
        _source?.Dispose();
        _source = null;
    }
}
