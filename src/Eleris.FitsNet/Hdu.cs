
namespace Eleris.FitsNet;

/// <summary>A Header Data Unit: a header plus its data.</summary>
public abstract class Hdu
{
    /// <summary>The header. Mandatory structural keywords are rewritten on save; edit data through the typed members.</summary>
    public FitsHeader Header { get; }

    internal DataSource Source { get; set; }

    /// <summary>Length in bytes of the data, without block padding.</summary>
    public long DataLength => Source.Length;

    /// <summary>Byte used to pad the data up to a multiple of 2880 bytes.</summary>
    internal virtual byte PaddingByte => 0;

    private protected Hdu(FitsHeader header, DataSource source)
    {
        Header = header;
        Source = source;
    }

    // ---- State as read from a file, needed to verify checksums faithfully ----------------------------------------

    /// <summary>The header bytes exactly as read from the file (only kept when the header carries a checksum keyword).</summary>
    internal byte[]? OriginalHeader { get; set; }

    /// <summary>The data source the HDU had when it was read; the original fill only applies to it.</summary>
    internal DataSource? OriginalSource { get; set; }

    /// <summary>The fill bytes found on disk after the data, kept only when they differ from the canonical fill.</summary>
    internal byte[]? OriginalFill { get; set; }

    /// <summary>True when the file ended before the fill of this HDU was complete.</summary>
    internal bool FillTruncated { get; set; }

    private bool DataUnchanged => ReferenceEquals(Source, OriginalSource);

    /// <summary>True when the header still holds exactly the cards that were read from the file.</summary>
    /// <summary>Header version at the time the HDU was last read from, or written to, a file.</summary>
    internal int CleanVersion { get; set; } = -1;

    /// <summary>True when the header is as it was when last read from, or written to, a file.</summary>
    internal bool IsClean => CleanVersion >= 0 && Header.Version == CleanVersion;

    /// <summary>True when the header has not been modified since it was last read or written (stored checksums describe it).</summary>
    private bool HeaderUnchanged() => OriginalHeader is not null && Header.Version == CleanVersion;

    /// <summary>True when the header serializes to exactly the bytes of the file it was read from or written to.</summary>
    internal bool HeaderBytesEqualOriginal() => OriginalHeader is not null && Header.ToBytes().AsSpan().SequenceEqual(OriginalHeader);

    /// <summary>Checksum of the data unit including its block fill (the fill found on disk while the data is untouched).</summary>
    internal uint ComputeDataSum()
    {
        using var stream = new ChecksumStream();
        Source.CopyTo(stream);
        long pad = HduLayout.Padded(Source.Length) - Source.Length;
        long done = 0;
        if (OriginalFill is { } fill && DataUnchanged)
        {
            stream.Write(fill);
            done = fill.Length;
        }
        if (done < pad)
        {
            var padding = new byte[Math.Min(pad - done, 1 << 16)];
            Array.Fill(padding, PaddingByte);
            for (long left = pad - done; left > 0; left -= padding.Length)
                stream.Write(padding.AsSpan(0, (int)Math.Min(left, padding.Length)));
        }
        return stream.Result;
    }

    /// <summary>
    /// Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the
    /// fill bytes found on disk while the data is untouched. For an HDU that has not been modified since it was read, this is
    /// exactly a verification of the file on disk; after any edit the stored sums no longer match, so they are reported
    /// <see cref="ChecksumStatus.Invalid"/>. Only the canonical <c>0xFFFFFFFF</c> total (minus zero) is accepted.
    /// </summary>
    public ChecksumResult VerifyChecksum()
    {
        bool truncated = FillTruncated && DataUnchanged;
        uint dataSum = ComputeDataSum();

        var dataCard = Header.Find("DATASUM");
        var dataStatus = ChecksumStatus.Missing;
        if (dataCard is not null)
        {
            string text = (dataCard.Value as string ?? Convert.ToString(dataCard.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "").Trim();
            dataStatus = !truncated
                         && uint.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out uint stored)
                         && stored == dataSum
                ? ChecksumStatus.Valid
                : ChecksumStatus.Invalid;
        }

        var sumStatus = ChecksumStatus.Missing;
        if (Header.Contains("CHECKSUM"))
        {
            byte[] header = HeaderUnchanged() ? OriginalHeader! : Header.ToBytes();
            uint total = Checksum.Add(Checksum.Sum(header), dataSum);
            sumStatus = !truncated && total == 0xFFFFFFFF ? ChecksumStatus.Valid : ChecksumStatus.Invalid;
        }
        return new ChecksumResult(sumStatus, dataStatus);
    }

    /// <summary>
    /// Called before the header is normalized for writing. <c>Clean</c>: the HDU carries checksum keywords and neither its header nor its
    /// data changed since it was read or written. <c>AnyInvalid</c>: in that case, whether the stored sums are wrong - they must not be
    /// refreshed, which would hide the corruption.
    /// </summary>
    internal (bool Clean, bool AnyInvalid) CaptureChecksumState()
    {
        if (!(Header.Contains("CHECKSUM") || Header.Contains("DATASUM"))) return (false, false);
        if (!DataUnchanged || !HeaderUnchanged()) return (false, false);
        var r = VerifyChecksum();
        return (true, r.Checksum == ChecksumStatus.Invalid || r.DataSum == ChecksumStatus.Invalid);
    }

    /// <summary>Adds, refreshes or removes CHECKSUM and DATASUM; call after the header is final.</summary>
    internal void ApplyChecksumPolicy(ChecksumPolicy policy, bool preserve)
    {
        bool has = Header.Contains("CHECKSUM") || Header.Contains("DATASUM");
        if (policy == ChecksumPolicy.Never)
        {
            Header.Remove("CHECKSUM");
            Header.Remove("DATASUM");
            return;
        }
        if (policy == ChecksumPolicy.Auto && (!has || preserve)) return;

        uint dataSum = ComputeDataSum();
        Header.Set("DATASUM", Checksum.ToDecimal(dataSum), "data unit checksum");
        Header.Set("CHECKSUM", new string('0', 16), "HDU checksum");
        uint total = Checksum.Add(Checksum.Sum(Header.ToBytes()), dataSum);
        Header.Set("CHECKSUM", Checksum.Encode(total, complement: true), "HDU checksum");
    }

    internal bool IsPrimaryForm => Header.Count > 0 && Header[0].Keyword == "SIMPLE";

    internal abstract HduLayout GetLayout(bool primary);

    /// <summary>Makes the mandatory keywords consistent with the data and the position in the file.</summary>
    internal abstract void Normalize(bool primary, bool hasExtensions);

    /// <summary>Whether this HDU may be the primary HDU of a file.</summary>
    internal virtual bool CanBePrimary => false;

    internal void ValidateForWrite(bool primary)
    {
        var layout = GetLayout(primary);
        if (layout.DataLength != Source.Length)
            throw new FitsException(
                $"The header describes {layout.DataLength} data bytes but the HDU holds {Source.Length}; update the data through the typed API.");
    }

    private static bool IsNaxisN(string k) => k.Length > 5 && k.StartsWith("NAXIS", StringComparison.Ordinal) && k.AsSpan(5).IndexOfAnyExceptInRange('0', '9') < 0;

    private static bool IsStructural(FitsCard c) =>
        c.Kind == FitsCardKind.Value
        && (c.Keyword is "SIMPLE" or "XTENSION" or "BITPIX" or "NAXIS" or "PCOUNT" or "GCOUNT" or "EXTEND" or "GROUPS"
            || IsNaxisN(c.Keyword));

    /// <summary>
    /// Rewrites the structural cards at the top of an image-like header, keeping every other card in order.
    /// Comments of cards whose value did not change are preserved.
    /// </summary>
    private protected static void RebuildMandatory(
        FitsHeader header, bool primary, string? xtension, BitPix bitPix, long[] axes, long pcount, long gcount, bool groups, bool extend)
    {
        FitsCard Make(string keyword, object value, string defaultComment)
        {
            var old = header.Find(keyword);
            var fresh = new FitsCard(keyword, value, defaultComment);
            return old is not null && Equals(old.Value, fresh.Value) ? old : fresh; // an unchanged card is kept exactly as it was
        }

        var others = header.Where(c => !IsStructural(c)).ToList();
        var cards = new List<FitsCard>
        {
            primary ? Make("SIMPLE", true, "conforms to FITS standard") : Make("XTENSION", xtension ?? "IMAGE", "extension type"),
            Make("BITPIX", (int)bitPix, "bits per data value"),
            Make("NAXIS", axes.Length, "number of data axes"),
        };
        for (int i = 0; i < axes.Length; i++)
        {
            var old = header.Find("NAXIS" + (i + 1));
            cards.Add(old is not null && Equals(old.Value, axes[i]) ? old : new FitsCard("NAXIS" + (i + 1), axes[i]));
        }
        if (primary && extend) cards.Add(Make("EXTEND", true, "extensions may be present"));
        else if (primary && header.Find("EXTEND") is { } existing) cards.Add(existing); // an EXTEND card of the file is not dropped
        if (groups) cards.Add(Make("GROUPS", true, "random groups structure"));
        if (!primary || groups)
        {
            cards.Add(Make("PCOUNT", pcount, "parameters per group / heap size"));
            cards.Add(Make("GCOUNT", gcount, "number of groups"));
        }
        cards.AddRange(others);
        header.ReplaceAll(cards);
    }
}

/// <summary>An extension this version does not interpret (kept verbatim so it survives a round trip).</summary>
public sealed class GenericHdu : Hdu
{
    internal GenericHdu(FitsHeader header, DataSource source) : base(header, source) { }

    /// <summary>A copy of the raw data bytes (unpadded).</summary>
    public byte[] ReadRawData()
    {
        return Source.ReadMemory().ToArray(); // always a private copy
    }

    internal override HduLayout GetLayout(bool primary) => HduLayout.Parse(Header, primary);

    internal override void Normalize(bool primary, bool hasExtensions)
    {
        if (primary) throw new FitsException("This kind of HDU cannot be the primary HDU.");
    }
}
