using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DocGen;

/// <summary>Turns the XML of documentation comments into Markdown that is valid CommonMark, tables and links included.</summary>
internal sealed partial class MarkdownRenderer(Dictionary<string, string> pages)
{
    /// <summary>For each documented identifier, the page that holds it (the file name without directory).</summary>
    private readonly Dictionary<string, string> _pages = pages;

    // ---- Public entry points ----------------------------------------------------------------------------------------

    /// <summary>The content of an element as paragraphs, code blocks and lists separated by blank lines (no trailing newline).</summary>
    public string Blocks(XElement? element, string page)
    {
        if (element is null) return "";
        var output = new StringBuilder();
        AppendBlocks(output, element, page);
        return Squeeze(output.ToString());
    }

    /// <summary>The first paragraph of an element on one line, safe inside a table cell.</summary>
    public string OneLine(XElement? element, string page)
    {
        string text = Blocks(element, page);
        if (text.Length == 0) return "";
        string first = text.Split("\n\n", 2)[0];
        if (first.StartsWith("```", StringComparison.Ordinal) || first.StartsWith("- ", StringComparison.Ordinal)) return "";
        first = first.Replace('\n', ' ');
        // a table cell holds the first sentence only: the full text is in the details below
        var end = SentenceEnd().Match(first);
        if (end.Success) first = first[..(end.Index + 1)];
        return first.Replace("|", "\\|");
    }

    // ---- Blocks -----------------------------------------------------------------------------------------------------

    private void AppendBlocks(StringBuilder output, XElement element, string page)
    {
        var paragraph = new StringBuilder();

        void Flush()
        {
            string text = Tidy(paragraph.ToString());
            if (text.Length > 0) output.Append(text).Append("\n\n");
            paragraph.Clear();
        }

        foreach (XNode node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    paragraph.Append(Escape(text.Value));
                    break;
                case XElement child:
                    switch (child.Name.LocalName)
                    {
                        case "para":
                            Flush();
                            AppendBlocks(output, child, page);
                            break;
                        case "code":
                            Flush();
                            output.Append("```csharp\n").Append(Dedent(child.Value)).Append("\n```\n\n");
                            break;
                        case "list":
                            Flush();
                            output.Append(List(child, page)).Append("\n\n");
                            break;
                        case "summary" or "remarks" or "example" or "returns" or "value":
                            Flush();
                            AppendBlocks(output, child, page);
                            break;
                        default:
                            paragraph.Append(Inline(child, page));
                            break;
                    }
                    break;
            }
        }
        Flush();
    }

    private string List(XElement list, string page)
    {
        bool numbered = (string?)list.Attribute("type") == "number";
        var lines = new List<string>();
        int n = 1;
        foreach (XElement item in list.Elements("item"))
        {
            string? term = item.Element("term") is { } t ? InlineContent(t, page) : null;
            string description = item.Element("description") is { } d ? InlineContent(d, page) : term is null ? InlineContent(item, page) : "";
            string text = term is not null && description.Length > 0 ? $"**{term.Trim()}** — {description.Trim()}" : (term ?? description).Trim();
            lines.Add((numbered ? $"{n++}. " : "- ") + Tidy(text).Replace("\n", " "));
        }
        return string.Join("\n", lines);
    }

    // ---- Inline content ---------------------------------------------------------------------------------------------

    private string InlineContent(XElement element, string page)
    {
        var sb = new StringBuilder();
        foreach (XNode node in element.Nodes())
        {
            if (node is XText text) sb.Append(Escape(text.Value));
            else if (node is XElement child) sb.Append(Inline(child, page));
        }
        return sb.ToString();
    }

    private string Inline(XElement e, string page)
    {
        switch (e.Name.LocalName)
        {
            case "c" or "tt":
                return Code(e.Value);
            case "paramref" or "typeparamref":
                return Code((string?)e.Attribute("name") ?? "");
            case "see":
                return See(e, page);
            case "b" or "strong":
                return "**" + InlineContent(e, page).Trim() + "**";
            case "i" or "em":
                return "*" + InlineContent(e, page).Trim() + "*";
            case "a" when e.Attribute("href") is { } href:
                return $"[{InlineContent(e, page).Trim()}]({href.Value})";
            case "br":
                return " ";
            case "para":
                return " " + InlineContent(e, page) + " ";
            default:
                return InlineContent(e, page);
        }
    }

    private string See(XElement e, string page)
    {
        string? label = e.Nodes().Any() ? InlineContent(e, page).Trim() : null;
        if (e.Attribute("langword") is { } word) return Code(word.Value);
        if (e.Attribute("href") is { } href) return $"[{label ?? href.Value}]({href.Value})";
        string cref = (string?)e.Attribute("cref") ?? "";
        return Link(cref, page, label is { Length: > 0 } ? label : null);
    }

    /// <summary>A link to the documentation of an identifier, or plain code when there is none.</summary>
    public string Link(string cref, string page, string? label = null)
    {
        if (cref.Length < 3 || cref[1] != ':') return Code(cref);
        string text = label ?? $"`{DisplayName(cref)}`";
        if (label is null && text.Contains('`') && text.Count(c => c == '`') > 2) text = Code(DisplayName(cref));

        if (_pages.TryGetValue(cref, out string? target))
        {
            string anchor = cref[0] == 'T' ? "" : "#" + Signatures.Anchor(cref);
            return $"[{text}]({(target == page ? "" : target)}{anchor})";
        }
        string? external = ExternalUrl(cref);
        return external is null ? (label ?? Code(DisplayName(cref))) : $"[{text}]({external})";
    }

    private static string? ExternalUrl(string cref)
    {
        string body = cref[2..];
        int paren = body.IndexOf('(');
        if (paren >= 0) body = body[..paren];
        if (!(body.StartsWith("System", StringComparison.Ordinal) || body.StartsWith("Microsoft", StringComparison.Ordinal))) return null;
        if (cref[0] != 'T')
        {
            int dot = body.LastIndexOf('.');
            if (dot < 0) return null;
            body = body[..dot];
        }
        body = body.Replace("{", "<").Split('<')[0];
        string slug = body.ToLowerInvariant().Replace('`', '-');
        return "https://learn.microsoft.com/dotnet/api/" + slug;
    }

    private static string DisplayName(string cref)
    {
        string body = cref[2..];
        int paren = body.IndexOf('(');
        if (paren >= 0) body = body[..paren];
        body = Regex.Replace(body, "`+[0-9]+", "").Replace("{", "<").Replace("}", ">");
        var parts = body.Split('.');
        if (cref[0] == 'T') return parts[^1];
        string member = parts[^1] == "#ctor" ? parts[^2] : parts[^1];
        return parts.Length >= 2 && parts[^1] != "#ctor" ? parts[^2] + "." + member : member;
    }

    // ---- Text -------------------------------------------------------------------------------------------------------

    public static string Code(string text)
    {
        text = WhiteSpace().Replace(text.Trim(), " ");
        if (text.Length == 0) return "";
        if (!text.Contains('`')) return "`" + text + "`";
        return "`` " + text + " ``";
    }

    /// <summary>Escapes what Markdown would take for formatting in running text.</summary>
    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in WhiteSpace().Replace(text, " "))
        {
            switch (c)
            {
                case '\\' or '`' or '*' or '_' or '[' or ']': sb.Append('\\').Append(c); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Trims a paragraph and protects a first character that would start a block.</summary>
    private static string Tidy(string paragraph)
    {
        string text = WhiteSpace().Replace(paragraph, " ").Trim();
        if (text.Length == 0) return "";
        if (text[0] is '#' or '+' or '-' or '=' or '~' || LeadingNumber().IsMatch(text)) text = "\\" + text;
        return text;
    }

    private static string Dedent(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..].TrimEnd() : l.TrimEnd()));
    }

    private static string Squeeze(string text) => Regex.Replace(text.Trim(), "\n{3,}", "\n\n");

    [GeneratedRegex(@"(?<=[a-z0-9)`])[.!?](?=\s+[A-Z`\[])")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex(@"^\d+[.)](\s|$)")]
    private static partial Regex LeadingNumber();
}
