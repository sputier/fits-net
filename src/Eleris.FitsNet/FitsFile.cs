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
    public void Save(Stream stream, ChecksumPolicy checksums = ChecksumPolicy.Auto)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_disposed, this);
        WriteCore(stream, checksums).Commit();
    }

    /// <summary>
    /// A file that has been written but not yet published: <see cref="Commit"/> makes the saved form the reference of the in-memory
    /// HDUs, <see cref="Rollback"/> puts their headers back as they were.
    /// </summary>
    private sealed record SaveTransaction(long[] Offsets, Action Commit, Action Rollback);

    /// <summary>Writes every HDU. The headers keep the changes of the save until the transaction is committed or rolled back.</summary>
    private SaveTransaction WriteCore(Stream stream, ChecksumPolicy checksums)
    {
        if (_hdus.Count == 0)
        {
            new ImageHdu().Header.Write(stream);
            return new SaveTransaction([], () => { }, () => { });
        }

        // Normalizing and checksumming rewrite headers in place; roll every header back if anything fails
        // (invalid HDU, unreadable data, failing output, or a file that cannot be put in place) so a failed save leaves the
        // in-memory model untouched. A successful save does update the headers (mandatory keywords, checksums) and makes them the reference.
        var snapshots = _hdus.Select(h => h.Header.Cards.ToList()).ToList();
        var states = _hdus.Select(h => checksums == ChecksumPolicy.Auto ? h.CaptureChecksumState() : (false, false)).ToList();
        var wasClean = _hdus.Select(h => h.IsClean).ToList();
        var headerBytes = new byte[_hdus.Count][];
        var offsets = new long[_hdus.Count];
        try
        {
            for (int i = 0; i < _hdus.Count; i++)
            {
                _hdus[i].Normalize(i == 0, _hdus.Count > 1);
                _hdus[i].ValidateForWrite(i == 0);
            }
            for (int i = 0; i < _hdus.Count; i++)
            {
                // An HDU that is as it was read keeps its checksum keywords untouched when its header bytes are unchanged
                // (so an unedited file is rewritten identically) and when they were wrong (so corruption is never hidden).
                bool preserve = states[i].Item1 && (states[i].Item2 || _hdus[i].HeaderBytesEqualOriginal());
                _hdus[i].ApplyChecksumPolicy(checksums, preserve);
            }

            long position = 0;
            for (int i = 0; i < _hdus.Count; i++)
            {
                var hdu = _hdus[i];
                byte[] header = hdu.Header.ToBytes();
                headerBytes[i] = header;
                stream.Write(header);
                position += header.Length;
                offsets[i] = position;
                hdu.Source.CopyTo(stream);
                long pad = HduLayout.Padded(hdu.DataLength) - hdu.DataLength;
                if (pad > 0)
                {
                    var padding = new byte[pad];
                    if (hdu.PaddingByte != 0) Array.Fill(padding, hdu.PaddingByte);
                    stream.Write(padding);
                }
                position += hdu.DataLength + pad;
            }
        }
        catch
        {
            Restore();
            throw;
        }

        void Restore()
        {
            for (int i = 0; i < _hdus.Count; i++)
            {
                _hdus[i].Header.ReplaceAll(snapshots[i]);
                if (wasClean[i]) _hdus[i].CleanVersion = _hdus[i].Header.Version; // the cards are what they were: so is "unchanged"
            }
        }

        void Commit()
        {
            for (int i = 0; i < _hdus.Count; i++)
            {
                // The saved form is now the reference for "unchanged": its header bytes, its header version and its data (the data
                // source is updated by Rebind when the file replaces the one it was read from). The fill written is the canonical one.
                var hdu = _hdus[i];
                bool hasSums = hdu.Header.Contains("CHECKSUM") || hdu.Header.Contains("DATASUM");
                hdu.OriginalHeader = hasSums ? headerBytes[i] : null;
                hdu.CleanVersion = hdu.Header.Version;
                hdu.OriginalSource = hdu.Source;
                hdu.OriginalFill = null;
                hdu.FillTruncated = false;
            }
        }

        return new SaveTransaction(offsets, Commit, Restore);
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
    public void Save(string path, ChecksumPolicy checksums = ChecksumPolicy.Auto)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ObjectDisposedException.ThrowIf(_disposed, this);
        string full = ResolvePath(path); // writing through a symbolic link updates its target
        bool overwritingSource = _sourcePath is not null && string.Equals(full, _sourcePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        string temp = full + ".tmp-" + Guid.NewGuid().ToString("N");
        SaveTransaction? written = null;
        try
        {
            using var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
            written = WriteCore(fs, checksums);
            fs.Flush(true);
        }
        catch
        {
            written?.Rollback();   // when WriteCore itself failed it has already restored the headers, and written is null
            TryDelete(temp);
            throw;
        }
        var transaction = written;

        if (!overwritingSource)
        {
            try
            {
                Publish(temp, full);
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

    private static void TryDelete(string path)
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
