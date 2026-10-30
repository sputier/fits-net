using System.Numerics;

namespace FitsNet;

/// <summary>Kind of a header card.</summary>
public enum FitsCardKind
{
    /// <summary><c>KEYWORD = value / comment</c>. <see cref="FitsCard.Value"/> may be <c>null</c> (undefined value).</summary>
    Value,

    /// <summary>COMMENT, HISTORY, blank keyword, or any card without a value indicator. <see cref="FitsCard.Value"/> is the text.</summary>
    Commentary,

    /// <summary>The END card.</summary>
    End,
}

/// <summary>
/// One 80-character header card. <see cref="Value"/> is one of <see cref="bool"/>, <see cref="long"/>,
/// <see cref="BigInteger"/>, <see cref="double"/>, <see cref="Complex"/>, <see cref="string"/> or <c>null</c> (undefined).
/// </summary>
public sealed class FitsCard
{
    public const int CardLength = 80;

    public string Keyword { get; }

    public object? Value { get; }

    public string? Comment { get; }

    public FitsCardKind Kind { get; }

    /// <summary>True when the keyword cannot be written as a standard 8-character keyword and uses the HIERARCH convention.</summary>
    public bool IsHierarch =>
        Kind == FitsCardKind.Value && (ForceHierarch || Keyword.Length > 8 || Keyword.Contains(' ') || Keyword.Contains('='));

    /// <summary>The card was written with HIERARCH even though its keyword would fit in 8 characters.</summary>
    internal bool ForceHierarch { get; init; }

    /// <summary>
    /// The 80-character image(s) this card was read from. A card that was read and not modified is written back exactly as it
    /// was (spacing, comment column, bytes), whatever its layout.
    /// </summary>
    internal string[]? Images { get; init; }

    private FitsCard(string keyword, object? value, string? comment, FitsCardKind kind)
    {
        Keyword = keyword;
        Value = value;
        Comment = comment;
        Kind = kind;
    }

    /// <summary>Creates a value card, validating keyword and value.</summary>
    public FitsCard(string keyword, object? value, string? comment = null)
        : this(ValidateKeyword(keyword), Normalize(value), ValidateText(comment, nameof(comment)), FitsCardKind.Value)
    {
    }

    /// <summary>
    /// Creates a card with a long or unusual keyword using the HIERARCH convention (which is not part of the FITS standard):
    /// printable ASCII, single-blank separated words, up to what fits in 80 characters.
    /// </summary>
    public static FitsCard Hierarch(string keyword, object? value, string? comment = null) =>
        new(ValidateHierarchKeyword(keyword), Normalize(value), ValidateText(comment, nameof(comment)), FitsCardKind.Value) { ForceHierarch = true };

    /// <summary>
    /// Like the public constructor but accepts single-byte (Latin-1) text in strings and comments: used to keep text that was read
    /// from a file when the structure of an HDU is rewritten.
    /// </summary>
    internal static FitsCard Lenient(string keyword, object? value, string? comment) =>
        new(ValidateKeyword(keyword), Normalize(value, lenientText: true), comment is null ? null : ValidateLenientText(comment), FitsCardKind.Value);

    /// <summary>Creates a commentary card (COMMENT, HISTORY or blank keyword).</summary>
    public static FitsCard Commentary(string keyword, string text)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        if (keyword is "END" or "CONTINUE" or "HIERARCH")
            throw new ArgumentException($"'{keyword}' is reserved and cannot be a commentary keyword.", nameof(keyword));
        if (keyword.Length > 8 || !IsStandardKeywordOrEmpty(keyword))
            throw new ArgumentException($"Invalid commentary keyword '{keyword}'.", nameof(keyword));
        return new FitsCard(keyword, ValidateText(text, nameof(text)) ?? "", null, FitsCardKind.Commentary);
    }

    public static FitsCard CommentCard(string text) => Commentary("COMMENT", text);

    public static FitsCard HistoryCard(string text) => Commentary("HISTORY", text);

    public static FitsCard End { get; } = new("END", null, null, FitsCardKind.End);

    /// <summary>Creates a card without validation (used by the parser to preserve non-conforming files).</summary>
    internal static FitsCard Raw(string keyword, object? value, string? comment, FitsCardKind kind, bool hierarch = false, string[]? images = null) =>
        new(keyword, value, comment, kind) { ForceHierarch = hierarch, Images = images };

    internal FitsCard WithValue(object? value, string? comment) => new(Keyword, value, comment, Kind) { ForceHierarch = ForceHierarch };

    public override string ToString() => Kind switch
    {
        FitsCardKind.End => "END",
        FitsCardKind.Commentary => $"{Keyword} {Value}",
        _ => string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{Keyword} = {(Value is string s ? "'" + s + "'" : Value ?? "<undefined>")}{(Comment is null ? "" : " / " + Comment)}"),
    };

    internal static bool IsStandardKeyword(string keyword)
    {
        if (keyword.Length is 0 or > 8) return false;
        foreach (char c in keyword)
            if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')) return false;
        return true;
    }

    private static bool IsStandardKeywordOrEmpty(string keyword) => keyword.Length == 0 || IsStandardKeyword(keyword);

    private static string ValidateKeyword(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        if (keyword is "END" or "CONTINUE" or "COMMENT" or "HISTORY" or "HIERARCH")
            throw new ArgumentException($"'{keyword}' is a reserved keyword and cannot carry a value.", nameof(keyword));
        if (IsStandardKeyword(keyword)) return keyword;

        // A keyword of 8 characters or less made of letters/digits but with lowercase letters is almost surely a mistake.
        if (keyword.Length <= 8 && keyword.Length > 0 && keyword.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw new ArgumentException($"Standard keyword '{keyword}' must be uppercase.", nameof(keyword));
        throw new ArgumentException(
            $"'{keyword}' is not a standard FITS keyword (1 to 8 characters among A-Z, 0-9, '-' and '_'); use FitsCard.Hierarch for the HIERARCH convention.",
            nameof(keyword));
    }

    private static string ValidateHierarchKeyword(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        if (keyword is "END" or "CONTINUE" or "COMMENT" or "HISTORY" or "HIERARCH")
            throw new ArgumentException($"'{keyword}' is a reserved keyword and cannot carry a value.", nameof(keyword));
        // printable ASCII, no '=' and no leading/trailing blank, single-space separated words
        if (keyword.Length == 0 || keyword.Length > CardLength - "HIERARCH  = ".Length - 1
            || keyword.Any(c => c is < ' ' or > '~' or '=')
            || keyword.StartsWith(' ') || keyword.EndsWith(' ') || keyword.Contains("  "))
            throw new ArgumentException($"Invalid HIERARCH keyword '{keyword}'.", nameof(keyword));
        return keyword;
    }

    private static double RequireFinite(double d) =>
        double.IsFinite(d) ? d : throw new ArgumentException("NaN and infinity cannot be stored in a FITS header value.");

    private static string ValidateLenientText(string text)
    {
        foreach (char c in text)
            if (c is < ' ' or > '\u00FF') throw new ArgumentException("FITS text must be single-byte characters without control codes.");
        return text;
    }

    private static string? ValidateText(string? text, string paramName)
    {
        if (text is null) return null;
        foreach (char c in text)
            if (c is < ' ' or > '~')
                throw new ArgumentException("FITS text must be printable ASCII (0x20-0x7E).", paramName);
        return text;
    }

    private static object? Normalize(object? value, bool lenientText = false) => value switch
    {
        null => null,
        bool or long => value,
        BigInteger b => b >= long.MinValue && b <= long.MaxValue ? (long)b : value,
        double d => RequireFinite(d),
        Complex c => Complex.IsFinite(c) ? value : throw new ArgumentException("NaN and infinity cannot be stored in a FITS header value."),
        sbyte or byte or short or ushort or int or uint => Convert.ToInt64(value),
        ulong u => u <= long.MaxValue ? (long)u : new BigInteger(u),
        // Go through the shortest round-trip text so 0.1f stays 0.1 rather than 0.10000000149011612.
        float f => RequireFinite(double.Parse(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture)),
        decimal m => (double)m,
        string s => lenientText ? ValidateLenientText(s) : ValidateText(s, nameof(value)),
        _ => throw new ArgumentException($"Unsupported FITS value type {value.GetType()}.", nameof(value)),
    };
}
