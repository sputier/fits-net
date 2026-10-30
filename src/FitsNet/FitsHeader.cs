using System.Collections;
using System.Numerics;
using System.Text;

namespace FitsNet;

/// <summary>Options controlling how strictly files are parsed.</summary>
/// <param name="Strict">When true, any deviation from the standard throws a <see cref="FitsException"/>.</param>
/// <param name="MaxHeaderBlocks">Upper bound on 2880-byte blocks read for one header, protecting against files without END.</param>
public sealed record FitsReadOptions(bool Strict = false, int MaxHeaderBlocks = 10_000)
{
    public static FitsReadOptions Default { get; } = new();
}

/// <summary>An ordered collection of header cards (without the END card, which is implicit).</summary>
public sealed class FitsHeader : IEnumerable<FitsCard>
{
    public const int BlockSize = 2880;
    private const int CardsPerBlock = BlockSize / FitsCard.CardLength;

    private readonly List<FitsCard> _cards = [];
    private int _version;

    /// <summary>Incremented by every modification: lets cached views of the header (table columns) know when they are stale.</summary>
    internal int Version => _version;

    public IReadOnlyList<FitsCard> Cards => _cards;

    public int Count => _cards.Count;

    public FitsCard this[int index] => _cards[index];

    /// <summary>Returns the first card with the given keyword, or null.</summary>
    public FitsCard? Find(string keyword)
    {
        int index = IndexOfKeyword(keyword);
        return index >= 0 ? _cards[index] : null;
    }

    // A header with many cards (a survey table has thousands) is searched through a dictionary rebuilt after each modification;
    // small headers are scanned, which is as fast and needs no index.
    private Dictionary<string, int>? _index;
    private int _indexVersion = -1;

    private int IndexOfKeyword(string keyword)
    {
        if (_cards.Count < 48) return _cards.FindIndex(c => c.Kind == FitsCardKind.Value && c.Keyword == keyword);
        if (_index is null || _indexVersion != _version)
        {
            var index = new Dictionary<string, int>(_cards.Count, StringComparer.Ordinal);
            for (int i = 0; i < _cards.Count; i++)
                if (_cards[i].Kind == FitsCardKind.Value) index.TryAdd(_cards[i].Keyword, i); // the first card with a keyword wins
            (_index, _indexVersion) = (index, _version);
        }
        return _index.GetValueOrDefault(keyword, -1);
    }

    public bool Contains(string keyword) => Find(keyword) is not null;

    /// <summary>Gets a value converted to <typeparamref name="T"/>; false if absent, undefined or not convertible.</summary>
    public bool TryGetValue<T>(string keyword, out T value)
    {
        value = default!;
        object? raw = Find(keyword)?.Value;
        if (raw is null) return false;
        return TryConvert(raw, out value);
    }

    /// <summary>Gets a value or <c>default</c> when absent; use <see cref="TryGetValue{T}"/> to tell absence from a zero/false value.</summary>
    public T? Get<T>(string keyword) => TryGetValue<T>(keyword, out var v) ? v : default;

    /// <summary>Replaces the value of the first matching card or appends a new card.</summary>
    public FitsHeader Set(string keyword, object? value, string? comment = null)
    {
        int index = _cards.FindIndex(c => c.Kind == FitsCardKind.Value && c.Keyword == keyword);
        // an existing HIERARCH card stays a HIERARCH card; new cards need a standard keyword (see SetHierarch)
        var card = index >= 0 && _cards[index].IsHierarch ? FitsCard.Hierarch(keyword, value, comment) : new FitsCard(keyword, value, comment);
        if (index >= 0)
            _cards[index] = comment is null ? card.WithValue(card.Value, _cards[index].Comment) : card;
        else
            _cards.Add(card);
        _version++;
        return this;
    }

    /// <summary>Like <see cref="Set"/> but keeps single-byte (Latin-1) text: used when text read from a file is written back.</summary>
    internal FitsHeader SetLenient(string keyword, object? value, string? comment = null)
    {
        int index = _cards.FindIndex(c => c.Kind == FitsCardKind.Value && c.Keyword == keyword);
        var card = index >= 0 && _cards[index].IsHierarch ? FitsCard.Hierarch(keyword, value, comment) : FitsCard.Lenient(keyword, value, comment);
        if (index >= 0)
            _cards[index] = comment is null ? card.WithValue(card.Value, _cards[index].Comment) : card;
        else
            _cards.Add(card);
        _version++;
        return this;
    }

    /// <summary>Like <see cref="Set"/> for a long or unusual keyword, written with the HIERARCH convention.</summary>
    public FitsHeader SetHierarch(string keyword, object? value, string? comment = null)
    {
        var card = FitsCard.Hierarch(keyword, value, comment);
        int index = _cards.FindIndex(c => c.Kind == FitsCardKind.Value && c.Keyword == keyword);
        if (index >= 0)
            _cards[index] = comment is null ? card.WithValue(card.Value, _cards[index].Comment) : card;
        else
            _cards.Add(card);
        _version++;
        return this;
    }

    /// <summary>Sets or clears (with <c>null</c>) the comment of the first card with the given keyword.</summary>
    public FitsHeader SetComment(string keyword, string? comment)
    {
        int index = _cards.FindIndex(c => c.Kind == FitsCardKind.Value && c.Keyword == keyword);
        if (index < 0) throw new KeyNotFoundException($"No card with keyword '{keyword}'.");
        _cards[index] = _cards[index].WithValue(_cards[index].Value, comment);
        _version++;
        return this;
    }

    public FitsHeader Add(FitsCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Kind == FitsCardKind.End) throw new ArgumentException("The END card is implicit.", nameof(card));
        _cards.Add(card);
        _version++;
        return this;
    }

    public FitsHeader Insert(int index, FitsCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Kind == FitsCardKind.End) throw new ArgumentException("The END card is implicit.", nameof(card));
        _cards.Insert(index, card);
        _version++;
        return this;
    }

    public FitsHeader AddComment(string text) => Add(FitsCard.CommentCard(text));

    public FitsHeader AddHistory(string text) => Add(FitsCard.HistoryCard(text));

    /// <summary>Removes every value card with the given keyword; returns how many were removed.</summary>
    public int Remove(string keyword)
    {
        int removed = _cards.RemoveAll(c => c.Kind == FitsCardKind.Value && c.Keyword == keyword);
        if (removed > 0) _version++;
        return removed;
    }

    public void RemoveAt(int index)
    {
        _cards.RemoveAt(index);
        _version++;
    }

    public void Clear()
    {
        _cards.Clear();
        _version++;
    }

    public IEnumerator<FitsCard> GetEnumerator() => _cards.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ---- Serialization -------------------------------------------------------------------------------------------

    /// <summary>Number of bytes this header occupies on disk (a multiple of 2880, END card included).</summary>
    public int SerializedLength => ((FormatLines().Count + 1 + CardsPerBlock - 1) / CardsPerBlock) * BlockSize;

    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(ToBytes());
    }

    public Task WriteAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return stream.WriteAsync(ToBytes(), cancellationToken).AsTask();
    }

    public byte[] ToBytes()
    {
        var lines = FormatLines();
        int blocks = (lines.Count + 1 + CardsPerBlock - 1) / CardsPerBlock;
        var bytes = new byte[blocks * BlockSize];
        Array.Fill(bytes, (byte)' ');
        int offset = 0;
        foreach (string line in lines)
        {
            Encoding.Latin1.GetBytes(line, 0, FitsCard.CardLength, bytes, offset); // Latin-1 keeps bytes read from non-conforming files
            offset += FitsCard.CardLength;
        }
        Encoding.Latin1.GetBytes("END", 0, 3, bytes, offset);
        return bytes;
    }

    private List<string> FormatLines()
    {
        var lines = new List<string>(_cards.Count);
        bool generatedContinue = false;
        foreach (var card in _cards)
        {
            var formatted = FitsCardCodec.Format(card).ToList();
            if (card.Images is null && formatted.Any(l => l.StartsWith("CONTINUE  ", StringComparison.Ordinal))) generatedContinue = true;
            lines.AddRange(formatted);
        }

        // The long string convention asks writers that use CONTINUE cards to say so (cards read from a file are written back as they were).
        if (generatedContinue && !Contains("LONGSTRN"))
            lines.AddRange(FitsCardCodec.Format(new FitsCard("LONGSTRN", "OGIP 1.0", "The HEASARC Long String Convention may be used.")));
        return lines;
    }

    // ---- Parsing -------------------------------------------------------------------------------------------------

    /// <summary>Reads a header (all 2880-byte blocks up to and including the one holding END) from the stream.</summary>
    public static FitsHeader Read(Stream stream, FitsReadOptions? options = null) =>
        ReadCore(stream, options, allowEndOfStream: false)!;

    internal void ReplaceAll(IEnumerable<FitsCard> cards)
    {
        var copy = cards.ToList();
        _cards.Clear();
        _cards.AddRange(copy);
        _version++;
    }

    /// <summary>Reads a header; returns null when the stream is already at its end and <paramref name="allowEndOfStream"/> is set.</summary>
    internal static FitsHeader? ReadCore(Stream stream, FitsReadOptions? options, bool allowEndOfStream, MemoryStream? capture = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= FitsReadOptions.Default;
        var header = new FitsHeader();
        var images = new List<string>();
        var block = new byte[BlockSize];
        bool ended = false;
        bool first = true;
        int blocks = 0;
        while (!ended)
        {
            if (++blocks > options.MaxHeaderBlocks)
                throw new FitsException($"Header exceeds {options.MaxHeaderBlocks} blocks without an END card.");
            int read = stream.ReadAtLeast(block, BlockSize, throwOnEndOfStream: false);
            if (read == 0 && first && allowEndOfStream) return null;
            if (read != BlockSize)
                throw new FitsException(first && read == 0
                    ? "Unexpected end of stream: no header found."
                    : "Unexpected end of stream inside a header block.");
            first = false;
            capture?.Write(block, 0, BlockSize);
            ended = ParseBlock(block, images, options);
        }
        header.AddParsed(images, options);
        return header;
    }

    public static async Task<FitsHeader> ReadAsync(Stream stream, FitsReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= FitsReadOptions.Default;
        var header = new FitsHeader();
        var images = new List<string>();
        var block = new byte[BlockSize];
        bool ended = false;
        bool first = true;
        int blocks = 0;
        while (!ended)
        {
            if (++blocks > options.MaxHeaderBlocks)
                throw new FitsException($"Header exceeds {options.MaxHeaderBlocks} blocks without an END card.");
            int read = await stream.ReadAtLeastAsync(block, BlockSize, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
            if (read != BlockSize)
                throw new FitsException(first && read == 0
                    ? "Unexpected end of stream: no header found."
                    : "Unexpected end of stream inside a header block.");
            first = false;
            ended = ParseBlock(block, images, options);
        }
        header.AddParsed(images, options);
        return header;
    }

    /// <summary>Collects card images from a block; returns true once the END card has been seen.</summary>
    private static bool ParseBlock(byte[] block, List<string> images, FitsReadOptions options)
    {
        for (int i = 0; i < CardsPerBlock; i++)
        {
            var span = block.AsSpan(i * FitsCard.CardLength, FitsCard.CardLength);
            var chars = new char[FitsCard.CardLength];
            for (int k = 0; k < span.Length; k++)
            {
                byte b = span[k];
                if (b is < 0x20 or > 0x7E && options.Strict) throw new FitsException($"Illegal character 0x{b:X2} in header.");
                chars[k] = (char)b; // tolerant mode keeps the byte (as Latin-1) so that a read/write round trip loses nothing
            }
            string image = new(chars);
            if (image.StartsWith("END     ", StringComparison.Ordinal) && image.AsSpan(8).IsWhiteSpace())
            {
                if (options.Strict)
                    for (int k = (i + 1) * FitsCard.CardLength; k < BlockSize; k++)
                        if (block[k] != (byte)' ') throw new FitsException("The block holding END must be padded with blanks.");
                return true;
            }
            images.Add(image);
        }
        return false;
    }

    private void AddParsed(List<string> images, FitsReadOptions options)
    {
        for (int i = 0; i < images.Count; i++)
        {
            var card = FitsCardCodec.Parse(images[i], options.Strict);

            // Long string convention: a string ending in '&' continued on CONTINUE cards.
            while (card.Kind == FitsCardKind.Value && card.Value is string s && s.EndsWith('&')
                   && i + 1 < images.Count && images[i + 1].StartsWith("CONTINUE  ", StringComparison.Ordinal))
            {
                var (cont, comment) = FitsCardCodec.ParseValueField(images[i + 1].AsSpan(8), options.Strict);
                if (cont is not string next) break;
                // comments found on several cards of one long string are joined
                string? joined = comment is null ? card.Comment : card.Comment is null ? comment : card.Comment + " " + comment;
                card = FitsCard.Raw(card.Keyword, s[..^1] + next, joined, FitsCardKind.Value, card.ForceHierarch, [.. card.Images!, images[i + 1]]);
                i++;
            }

            _cards.Add(card);
        }
    }

    // ---- Value conversion ----------------------------------------------------------------------------------------

    private static bool TryConvert<T>(object raw, out T value)
    {
        value = default!;
        Type t = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        try
        {
            if (t == typeof(object) || t.IsInstanceOfType(raw)) { value = (T)raw; return true; }
            if (raw is string || raw is Complex) return false;
            if (t == typeof(string)) return false;
            if (raw is bool) return false;
            if (raw is BigInteger bi)
            {
                if (t == typeof(double)) { value = (T)(object)(double)bi; return true; }
                return false;
            }
            if (t == typeof(BigInteger) && raw is long l) { value = (T)(object)new BigInteger(l); return true; }
            if (t == typeof(Complex)) { value = (T)(object)new Complex(Convert.ToDouble(raw), 0); return true; }
            if (t.IsPrimitive && t != typeof(bool) && t != typeof(char))
            {
                if (raw is double d && t != typeof(double) && t != typeof(float) && d != Math.Truncate(d)) return false;
                value = (T)Convert.ChangeType(raw, t, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception e) when (e is OverflowException or InvalidCastException or FormatException)
        {
        }
        return false;
    }
}
