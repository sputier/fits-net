using System.Globalization;
using System.Numerics;
using System.Text;

namespace Eleris.FitsNet;

/// <summary>Converts between 80-character card images and <see cref="FitsCard"/>.</summary>
internal static class FitsCardCodec
{
    private const int ValueStart = 10;
    private const int CommentColumn = 31;

    /// <summary>Parses a single 80-character card image. CONTINUE cards come back as commentary cards.</summary>
    public static FitsCard Parse(string image, bool strict)
    {
        ReadOnlySpan<char> card = image;
        if (card.Length != FitsCard.CardLength)
            throw new FitsException($"A card must be {FitsCard.CardLength} characters long.");

        string keyword = card[..8].TrimEnd().ToString();

        if (keyword == "END")
        {
            if (card[8..].IsWhiteSpace()) return FitsCard.End;
            if (strict) throw new FitsException("END card must be blank after column 8.");
            return FitsCard.Raw(keyword, card[8..].TrimEnd().ToString(), null, FitsCardKind.Commentary, images: [image]);
        }

        if (strict && keyword.Length > 0 && !FitsCard.IsStandardKeyword(keyword))
            throw new FitsException($"Illegal keyword '{keyword}'.");

        if (keyword == "HIERARCH")
        {
            int eq = card.IndexOf('=');
            if (eq > 8)
            {
                string name = card[8..eq].Trim().ToString();
                if (name.Length > 0)
                {
                    var (value, comment) = ParseValueField(card[(eq + 1)..], strict);
                    return FitsCard.Raw(name, value, comment, FitsCardKind.Value, hierarch: true, images: [image]);
                }
            }
        }

        if (strict && card[8] == '=' && card[9] != ' ' && keyword is not ("COMMENT" or "HISTORY" or "CONTINUE" or ""))
            throw new FitsException($"The value indicator of {keyword} must be \"= \" (columns 9 and 10).");
        bool hasValueIndicator = card[8] == '=' && (!strict || card[9] == ' ');
        if (hasValueIndicator && keyword is not ("COMMENT" or "HISTORY" or "CONTINUE" or ""))
        {
            var (value, comment) = ParseValueField(card[9..], strict);
            if (strict && value is not null && IsFixedFormatKeyword(keyword)) CheckFixedFormat(card, keyword, value);
            return FitsCard.Raw(keyword, value, comment, FitsCardKind.Value, images: [image]);
        }

        return FitsCard.Raw(keyword, card[8..].TrimEnd().ToString(), null, FitsCardKind.Commentary, images: [image]);
    }

    private static bool IsIndexed(string key, string prefix) =>
        key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal) && key.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;

    /// <summary>The mandatory keywords of the standard HDUs, whose values must be in fixed format (standard 4.2).</summary>
    private static bool IsFixedFormatKeyword(string k) =>
        k is "SIMPLE" or "BITPIX" or "NAXIS" or "EXTEND" or "PCOUNT" or "GCOUNT" or "GROUPS" or "TFIELDS" or "XTENSION"
        || IsIndexed(k, "NAXIS") || IsIndexed(k, "TFORM") || IsIndexed(k, "TBCOL");

    private static void CheckFixedFormat(ReadOnlySpan<char> card, string keyword, object value)
    {
        // strings start in column 11; numbers and logicals are right justified to column 30
        bool ok = value is string ? card[10] == '\'' : card[29] != ' ';
        if (!ok) throw new FitsException($"The value of {keyword} must be in fixed format (columns 11 to 30).");
    }

    /// <summary>Parses the value/comment portion of a card (everything after the value indicator).</summary>
    public static (object? Value, string? Comment) ParseValueField(ReadOnlySpan<char> field, bool strict)
    {
        int i = 0;
        while (i < field.Length && field[i] == ' ') i++;
        if (i == field.Length) return (null, null);

        if (field[i] == '/') return (null, ParseComment(field[(i + 1)..]));

        if (field[i] == '\'')
        {
            var sb = new StringBuilder();
            i++;
            bool closed = false;
            while (i < field.Length)
            {
                char c = field[i];
                if (c == '\'')
                {
                    if (i + 1 < field.Length && field[i + 1] == '\'') { sb.Append('\''); i += 2; continue; }
                    closed = true;
                    i++;
                    break;
                }
                sb.Append(c);
                i++;
            }
            if (!closed && strict) throw new FitsException("Unterminated string value.");
            string text = sb.ToString().TrimEnd(' ');
            while (i < field.Length && field[i] == ' ') i++;
            if (strict && i < field.Length && field[i] != '/') throw new FitsException("Unexpected text after string value.");
            string? comment = i < field.Length && field[i] == '/' ? ParseComment(field[(i + 1)..]) : null;
            return (text, comment);
        }

        int slash = field[i..].IndexOf('/');
        ReadOnlySpan<char> token = slash < 0 ? field[i..] : field[i..(i + slash)];
        string? trailingComment = slash < 0 ? null : ParseComment(field[(i + slash + 1)..]);
        object? parsed = ParseToken(token.Trim(), strict);
        return (parsed, trailingComment);
    }

    private static string? ParseComment(ReadOnlySpan<char> text)
    {
        if (text.Length > 0 && text[0] == ' ') text = text[1..];
        text = text.TrimEnd();
        return text.Length == 0 ? null : text.ToString();
    }

    private static object ParseToken(ReadOnlySpan<char> token, bool strict)
    {
        if (token is "T") return true;
        if (token is "F") return false;

        if (token.Length > 0 && token[0] == '(')
        {
            int close = token.IndexOf(')');
            int comma = token.IndexOf(',');
            if (close > 0 && comma > 0
                && TryParseDouble(token[1..comma], strict, out double re)
                && TryParseDouble(token[(comma + 1)..close], strict, out double im))
                return new Complex(re, im);
        }
        else if (IsInteger(token))
        {
            if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
            if (BigInteger.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var b)) return b;
        }
        else if (TryParseDouble(token, strict, out double d))
        {
            return d;
        }

        if (strict) throw new FitsException($"Invalid value '{token}'.");
        return token.ToString();
    }

    private static bool IsInteger(ReadOnlySpan<char> s)
    {
        int i = s.Length > 0 && s[0] is '+' or '-' ? 1 : 0;
        if (i >= s.Length) return false;
        for (; i < s.Length; i++)
            if (s[i] is < '0' or > '9') return false;
        return true;
    }

    private static bool TryParseDouble(ReadOnlySpan<char> s, bool strict, out double value)
    {
        s = s.Trim();
        value = 0;
        if (s.IsEmpty) return false;
        Span<char> buf = stackalloc char[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            buf[i] = c is 'D' or 'd' ? 'E' : c;
            // Reject tokens like "NaN", "Infinity", hex etc.: only digits, sign, '.', exponent letters allowed.
            if (!(c is >= '0' and <= '9' or '+' or '-' or '.' or 'E' or 'D')
                && !(!strict && c is 'e' or 'd')) return false;
        }
        // Overflowing literals (e.g. 1.0E+400) parse to infinity, which cannot be written back: reject them.
        return double.TryParse(buf, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    /// <summary>Formats a card as one or more 80-character images (long strings use CONTINUE).</summary>
    public static IEnumerable<string> Format(FitsCard card)
    {
        if (card.Images is { } stored)
        {
            foreach (string line in stored) yield return line;
            yield break;
        }

        switch (card.Kind)
        {
            case FitsCardKind.End:
                yield return Pad("END");
                yield break;

            case FitsCardKind.Commentary:
            {
                string text = (string?)card.Value ?? "";
                if (text.Length == 0) { yield return Pad(card.Keyword); yield break; }
                const int width = 72;
                for (int pos = 0; pos < text.Length; pos += width)
                    yield return Pad(card.Keyword.PadRight(8) + text.Substring(pos, Math.Min(width, text.Length - pos)));
                yield break;
            }
        }

        string prefix = card.IsHierarch ? "HIERARCH " + card.Keyword + " = " : card.Keyword.PadRight(8) + "= ";

        if (card.Value is string s)
        {
            foreach (string line in FormatString(prefix, s, card.Comment))
                yield return line;
            yield break;
        }

        string value = card.Value switch
        {
            null => "",
            bool b => (b ? "T" : "F").PadLeft(20),
            long l => l.ToString(CultureInfo.InvariantCulture).PadLeft(20),
            BigInteger bi => bi.ToString(CultureInfo.InvariantCulture).PadLeft(20),
            double d => FormatDouble(d).PadLeft(20),
            Complex c => "(" + FormatDouble(c.Real) + ", " + FormatDouble(c.Imaginary) + ")",
            _ => throw new FitsException($"Unsupported value type {card.Value.GetType()}."),
        };
        if (prefix.Length + value.Length > FitsCard.CardLength) value = value.TrimStart();
        if (prefix.Length + value.Length > FitsCard.CardLength)
            throw new FitsException($"Card '{card.Keyword}' does not fit in {FitsCard.CardLength} characters.");
        yield return WithComment(prefix + value, card.Comment, prefix + value.TrimStart());
    }

    private static IEnumerable<string> FormatString(string prefix, string s, string? comment)
    {
        string escaped = s.Replace("'", "''");
        int firstCapacity = FitsCard.CardLength - prefix.Length - 2; // text capacity incl. optional '&'

        if (escaped.Length <= firstCapacity)
        {
            // Pad to 8 characters when there is room (recommended by the standard, not required).
            yield return WithComment(prefix + "'" + escaped.PadRight(Math.Min(8, firstCapacity)) + "'", comment, prefix + "'" + escaped + "'");
            yield break;
        }

        // A piece must hold at least one escaped quote pair plus the '&'.
        if (firstCapacity < 3)
            throw new FitsException($"Keyword '{prefix.Trim()}' leaves no room for a long string value.");

        // Long string convention: split into pieces ending with '&', continued on CONTINUE cards.
        var pieces = new List<string>();
        int pos = 0;
        int capacity = firstCapacity - 1;
        while (escaped.Length - pos > (pieces.Count == 0 ? firstCapacity : 68))
        {
            int take = pieces.Count == 0 ? capacity : 67;
            // Never split an escaped quote pair.
            if (escaped[pos + take - 1] == '\'' && CountTrailingQuotes(escaped, pos + take) % 2 == 1) take--;
            pieces.Add(escaped.Substring(pos, take));
            pos += take;
        }
        pieces.Add(escaped[pos..]);

        yield return Pad(prefix + "'" + pieces[0] + "&'");
        for (int k = 1; k < pieces.Count; k++)
        {
            bool last = k == pieces.Count - 1;
            string line = "CONTINUE  '" + pieces[k] + (last ? "'" : "&'");
            yield return last ? WithComment(line, comment) : Pad(line);
        }
    }

    private static int CountTrailingQuotes(string s, int endExclusive)
    {
        int n = 0;
        for (int i = endExclusive - 1; i >= 0 && s[i] == '\''; i--) n++;
        return n;
    }

    /// <summary>
    /// Appends the comment at column 32, or right after the value when the aligned layout does not fit: with the optional padding
    /// removed (<paramref name="compactText"/>) and a single separating space. Only a comment that cannot fit at all is refused.
    /// </summary>
    private static string WithComment(string text, string? comment, string? compactText = null)
    {
        if (comment is null) return Pad(text);
        string sep = text.Length < CommentColumn ? new string(' ', CommentColumn - text.Length) : " ";
        string line = text + sep + "/ " + comment;
        if (line.Length > FitsCard.CardLength)
        {
            line = (compactText ?? text).TrimEnd() + " / " + comment;
            if (line.Length > FitsCard.CardLength)
                throw new FitsException($"Comment does not fit on the card ({line.Length} > {FitsCard.CardLength} characters).");
        }
        return Pad(line);
    }

    private static string Pad(string s)
    {
        if (s.Length > FitsCard.CardLength) throw new FitsException($"Card image longer than {FitsCard.CardLength} characters.");
        return s.PadRight(FitsCard.CardLength);
    }

    internal static string FormatDouble(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d))
            throw new FitsException("NaN and infinity cannot be represented in a FITS header value.");
        string s = d.ToString("R", CultureInfo.InvariantCulture);
        if (s.IndexOfAny(['.', 'E']) < 0) s += ".0";
        return s;
    }
}
