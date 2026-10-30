using System.Collections;
using System.Text;

namespace FitsNet;

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

    public int Count => _hdus.Count;

    public Hdu this[int index] => _hdus[index];

    /// <summary>The primary HDU.</summary>
    public Hdu Primary => _hdus.Count > 0 ? _hdus[0] : throw new InvalidOperationException("The file has no HDU.");

    public IEnumerator<Hdu> GetEnumerator() => _hdus.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ---- Editing -------------------------------------------------------------------------------------------------

    /// <summary>Appends an HDU. An empty primary image is inserted first if the file is empty and the HDU cannot be primary.</summary>
    public FitsFile Add(Hdu hdu) => Insert(_hdus.Count, hdu);

    /// <summary>
    /// Inserts an HDU. Inserting at index 0 makes the current primary an extension, and a non-image HDU inserted at
    /// index 0 gets an empty primary image in front of it (shifting later indexes by one).
    /// </summary>
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

    public void RemoveAt(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index == 0 && _hdus.Count > 1 && !_hdus[1].CanBePrimary)
            throw new InvalidOperationException("The next HDU cannot become the primary HDU; replace the primary with an empty image instead.");
        _hdus.RemoveAt(index);
    }

    /// <summary>Removes an HDU; returns false if it is not part of this file. Same restrictions as <see cref="RemoveAt"/>.</summary>
    public bool Remove(Hdu hdu)
    {
        int index = _hdus.IndexOf(hdu);
        if (index < 0) return false;
        RemoveAt(index);
        return true;
    }

    // ---- Reading -------------------------------------------------------------------------------------------------

    /// <summary>Opens a file lazily: pixel data is read from disk on demand. Dispose the result to release the file.</summary>
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
    public static FitsFile Read(Stream stream, FitsReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        // One buffer for the whole file: every HDU is a slice of it, so the file is held in memory once.
        byte[] buffer;
        int length;
        if (stream.CanSeek)
        {
            long remaining = Math.Max(0, stream.Length - stream.Position);
            if (remaining > Array.MaxLength) throw new FitsException("The file is larger than 2 GB: use FitsFile.Open, which reads on demand.");
            buffer = new byte[remaining];
            stream.ReadExactly(buffer);
            length = buffer.Length;
        }
        else
        {
            var copy = new MemoryStream();
            try
            {
                stream.CopyTo(copy);
            }
            catch (IOException) when (copy.Length > int.MaxValue - (1 << 20))
            {
                throw new FitsException("The stream is larger than 2 GB: save it to a file and use FitsFile.Open, which reads on demand.");
            }
            buffer = copy.GetBuffer(); // the capacity can exceed the length: only [0, length) is used
            length = (int)copy.Length;
        }
        return ReadCore(new MemoryStream(buffer, 0, length, writable: false), options ?? FitsReadOptions.Default, lazy: false, buffer);
    }

    private static FitsFile ReadCore(Stream s, FitsReadOptions options, bool lazy, byte[]? shared = null)
    {
        var file = new FitsFile();
        Span<byte> peek = stackalloc byte[8];

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
        WriteCore(stream, checksums);
    }

    /// <summary>Writes every HDU and returns the offset of each HDU's data in the output.</summary>
    private long[] WriteCore(Stream stream, ChecksumPolicy checksums)
    {
        if (_hdus.Count == 0)
        {
            new ImageHdu().Header.Write(stream);
            return [];
        }

        // Normalizing and checksumming rewrite headers in place; roll every header back if anything fails
        // (invalid HDU, unreadable data, failing output) so a failed save leaves the in-memory model untouched.
        // A successful save does update the headers (mandatory keywords, checksums) and makes them the reference.
        var snapshots = _hdus.Select(h => h.Header.Cards.ToList()).ToList();
        var states = _hdus.Select(h => checksums == ChecksumPolicy.Auto ? h.CaptureChecksumState() : (false, false)).ToList();
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
            for (int i = 0; i < _hdus.Count; i++) _hdus[i].Header.ReplaceAll(snapshots[i]);
            throw;
        }

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
        return offsets;
    }

    /// <summary>
    /// Writes the file to <paramref name="path"/>: a temporary file next to the target is written completely, flushed,
    /// then moved over it, so a failure leaves the original untouched. When overwriting the file this instance was
    /// opened from, the lazily read HDUs are re-attached to the new file. HDUs previously removed from this file
    /// lose access to their data in that case. An existing target keeps its attributes and permissions (File.Replace), and a symbolic
    /// link is written through to its target.
    /// </summary>
    public void Save(string path, ChecksumPolicy checksums = ChecksumPolicy.Auto)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ObjectDisposedException.ThrowIf(_disposed, this);
        string full = ResolvePath(path); // writing through a symbolic link updates its target
        bool overwritingSource = _sourcePath is not null && string.Equals(full, _sourcePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        string temp = full + ".tmp-" + Guid.NewGuid().ToString("N");
        long[] offsets;
        try
        {
            using var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
            offsets = WriteCore(fs, checksums);
            fs.Flush(true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        if (!overwritingSource)
        {
            try { Publish(temp, full); }
            catch { TryDelete(temp); throw; }
            return;
        }

        // The old file is still read through _source: release it, replace the file, then re-attach.
        var oldStream = _source!;
        oldStream.Dispose();
        try
        {
            Publish(temp, full);
        }
        catch
        {
            TryDelete(temp);
            try
            {
                Rebind(OpenForRead(full), oldStream, null); // the original file is intact: re-attach to it
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // keep the error that made the replacement fail
            }
            throw;
        }
        try
        {
            Rebind(OpenForRead(full), oldStream, offsets);
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
