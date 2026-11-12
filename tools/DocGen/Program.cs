using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocGen;
using Eleris.FitsNet;

// Generates the API reference (docs/api) from the assembly and its XML documentation, copies the compiled examples into the guide
// pages, and keeps the API part of the navigation of mkdocs.yml up to date.
//
//   dotnet run --project tools/DocGen              rewrite the generated files
//   dotnet run --project tools/DocGen -- --check   fail when a generated file is not up to date (for continuous integration)

// The pages must not depend on the culture of the machine (minus sign, decimal separator).
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

bool check = args.Contains("--check");
string root = args.SkipWhile(a => a != "--root").Skip(1).FirstOrDefault() ?? FindRoot();
var generator = new Generator(root, check);
return generator.Run();

static string FindRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "Eleris.FitsNet.slnx"))) return dir.FullName;
    throw new InvalidOperationException("Eleris.FitsNet.slnx was not found above " + AppContext.BaseDirectory);
}

namespace DocGen
{
    internal sealed partial class Generator(string root, bool check)
    {
        private readonly string _docs = Path.Combine(root, "docs");
        private readonly Dictionary<string, string> _output = [];   // relative path -> content
        private readonly List<string> _warnings = [];
        private readonly List<string> _problems = [];
        private Dictionary<string, XElement> _xml = [];
        private Dictionary<string, string> _pages = [];
        private MarkdownRenderer _md = null!;

        public int Run()
        {
            var assembly = typeof(FitsFile).Assembly;
            _xml = XDocument.Load(Path.ChangeExtension(assembly.Location, ".xml")).Descendants("member")
                .ToDictionary(m => (string)m.Attribute("name")!, m => m);

            var types = assembly.GetExportedTypes().Where(t => !t.IsNested).OrderBy(t => t.Namespace, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
            CheckSupported(assembly);
            _pages = IndexPages(types);
            _md = new MarkdownRenderer(_pages);

            foreach (var t in types) _output[Path.Combine("docs", "api", PageName(t))] = TypePage(t);
            _output[Path.Combine("docs", "api", "index.md")] = IndexPage(types);
            UpdateNavigation(types);
            InjectExamples();

            foreach (string w in _warnings.Distinct()) Console.Error.WriteLine("warning: " + w);
            // A broken example reference or a lost navigation marker leaves the documentation wrong without any file being stale.
            if (_problems.Count > 0)
            {
                foreach (string p in _problems.Distinct()) Console.Error.WriteLine("error: " + p);
                return 1;
            }
            return check ? Compare() : Write();
        }

        private static string ReadLf(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        // ---- Output -------------------------------------------------------------------------------------------------

        private int Write()
        {
            string api = Path.Combine(root, "docs", "api");
            Directory.CreateDirectory(api);
            foreach (string stale in Directory.GetFiles(api, "*.md"))
                if (!_output.ContainsKey(Path.GetRelativePath(root, stale))) File.Delete(stale);
            foreach (var (path, content) in _output)
            {
                string full = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                if (!File.Exists(full) || ReadLf(full) != content) File.WriteAllText(full, content, new UTF8Encoding(false));
            }
            Console.WriteLine($"{_output.Count} files up to date.");
            return 0;
        }

        private int Compare()
        {
            var stale = new List<string>();
            foreach (var (path, content) in _output)
            {
                string full = Path.Combine(root, path);
                if (!File.Exists(full) || ReadLf(full) != content) stale.Add(path);
            }
            string api = Path.Combine(root, "docs", "api");
            if (Directory.Exists(api))
                foreach (string existing in Directory.GetFiles(api, "*.md"))
                    if (!_output.ContainsKey(Path.GetRelativePath(root, existing))) stale.Add(Path.GetRelativePath(root, existing) + " (no longer generated)");
            if (stale.Count == 0)
            {
                Console.WriteLine("The generated documentation is up to date.");
                return 0;
            }
            Console.Error.WriteLine("The generated documentation is out of date; run `dotnet run --project tools/DocGen` and commit the result:");
            foreach (string s in stale) Console.Error.WriteLine("  " + s.Replace('\\', '/'));
            return 1;
        }

        // ---- Identifiers and pages --------------------------------------------------------------------------------

        // A generic type is named Type`1: the backtick, which Markdown links do not like, becomes a hyphen (FitsImageWriter-1.md).
        private static string PageName(Type t) => $"{t.Namespace}.{t.Name.Replace('`', '-')}.md";

        private Dictionary<string, string> IndexPages(List<Type> types)
        {
            var pages = new Dictionary<string, string>();
            foreach (var t in types)
            {
                string page = PageName(t);
                pages[Signatures.IdOfType(t)] = page;
                foreach (var m in Members(t)) pages[Signatures.Id(m)] = page;
            }
            return pages;
        }

        /// <summary>Events, protected members and nested public types are not rendered: fail instead of leaving them undocumented.</summary>
        private void CheckSupported(System.Reflection.Assembly assembly)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var t in assembly.GetExportedTypes())
            {
                if (t.IsNested) _problems.Add($"{t.FullName} is a nested public type, which the generator does not document");
                foreach (var m in t.GetMembers(all))
                {
                    if (m.Name == "Finalize") continue;
                    bool exposed = m switch
                    {
                        EventInfo e => e.AddMethod?.IsPublic == true || e.AddMethod?.IsFamily == true || e.AddMethod?.IsFamilyOrAssembly == true,
                        MethodBase mb => mb.IsFamily || mb.IsFamilyOrAssembly,
                        FieldInfo f => f.IsFamily || f.IsFamilyOrAssembly,
                        PropertyInfo p => (p.GetMethod ?? p.SetMethod) is { IsFamily: true } or { IsFamilyOrAssembly: true },
                        _ => false,
                    };
                    if (exposed && !t.IsSealed) _problems.Add($"{t.FullName}.{m.Name} is an event or a protected member, which the generator does not document");
                    else if (m is EventInfo && exposed) _problems.Add($"{t.FullName}.{m.Name} is an event, which the generator does not document");
                }
            }
        }

        private static IEnumerable<MemberInfo> Members(Type t)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var m in t.GetMembers(flags))
            {
                if (m is Type) continue;
                if (m is MethodInfo mi && (mi.IsSpecialName && !mi.Name.StartsWith("op_", StringComparison.Ordinal) || mi.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))) continue;
                if (m is MethodInfo { Name: "<Clone>$" or "PrintMembers" or "Deconstruct" or "GetHashCode" or "Equals" } rec && rec.DeclaringType!.GetMethod("<Clone>$") is not null) continue;
                if (m is PropertyInfo { Name: "EqualityContract" }) continue;
                if (m is FieldInfo { IsSpecialName: true }) continue;
                if (t.IsEnum && m is FieldInfo f && !f.IsLiteral) continue;
                if (m is ConstructorInfo ci && (ci.IsStatic || t.IsAbstract && t.IsSealed)) continue;
                if (m is PropertyInfo p && p.GetMethod?.IsPublic != true && p.SetMethod?.IsPublic != true) continue;
                yield return m;
            }
        }

        // ---- Documentation lookup ---------------------------------------------------------------------------------

        private XElement? Doc(string id, MemberInfo? member, int depth = 0)
        {
            if (!_xml.TryGetValue(id, out XElement? e) || depth > 5) return null;
            XElement? inherit = e.Element("inheritdoc");
            if (inherit is null) return e;
            string? cref = (string?)inherit.Attribute("cref");
            if (cref is not null) return Doc(cref, null, depth + 1) ?? e;
            // The documented member is kept along the chain, so that an inheritdoc of an inheritdoc finds its own base in turn.
            MemberInfo? baseMember = member is null ? null : BaseMember(member);
            return baseMember is not null ? Doc(Signatures.Id(baseMember), baseMember, depth + 1) ?? e : e;
        }

        private static MemberInfo? BaseMember(MemberInfo member)
        {
            MethodInfo? method = member switch { MethodInfo mi => mi, PropertyInfo pi => pi.GetMethod ?? pi.SetMethod, _ => null };
            if (method is null || member.DeclaringType!.IsInterface) return null;
            var baseMethod = method.GetBaseDefinition();
            if (baseMethod.DeclaringType != member.DeclaringType)
                return member is PropertyInfo ? baseMethod.DeclaringType!.GetProperties().First(p => p.GetMethod == baseMethod || p.SetMethod == baseMethod) : baseMethod;
            foreach (var i in member.DeclaringType!.GetInterfaces())
            {
                var map = member.DeclaringType.GetInterfaceMap(i);
                int at = Array.IndexOf(map.TargetMethods, method);
                if (at < 0) continue;
                var target = map.InterfaceMethods[at];
                return member is PropertyInfo ? i.GetProperties().First(p => p.GetMethod == target || p.SetMethod == target) : target;
            }
            return null;
        }

        // ---- Type pages -------------------------------------------------------------------------------------------

        private string TypePage(Type t)
        {
            string page = PageName(t);
            string id = Signatures.IdOfType(t);
            XElement? doc = Doc(id, null);
            var sb = new StringBuilder();
            string title = Signatures.TypeName(t);
            sb.Append($"# {Escape(title)} {Capitalize(Signatures.Kind(t))}\n\n");
            sb.Append($"**Namespace:** `{t.Namespace}`\n\n");
            string chain = InheritanceChain(t);
            if (chain.Length > 0) sb.Append($"**Inheritance:** {chain}\n\n");
            string summary = _md.Blocks(doc?.Element("summary"), page);
            if (summary.Length == 0) _warnings.Add($"{title} has no summary");
            sb.Append(summary).Append("\n\n");
            sb.Append("```csharp\n").Append(Signatures.TypeDeclaration(t)).Append("\n```\n\n");
            AppendSections(sb, doc, page, includeSummary: false);

            if (t.IsEnum)
            {
                sb.Append("## Members\n\n| Name | Value | Description |\n| --- | ---: | --- |\n");
                foreach (var f in Members(t).OfType<FieldInfo>().OrderBy(EnumValue).ThenBy(f => f.Name, StringComparer.Ordinal))
                {
                    var fdoc = Doc(Signatures.Id(f), f);
                    sb.Append($"| <a id=\"{Signatures.Anchor(Signatures.Id(f))}\"></a>`{f.Name}` | {EnumValue(f).ToString(System.Globalization.CultureInfo.InvariantCulture)} | {_md.OneLine(fdoc?.Element("summary"), page)} |\n");
                    if (fdoc?.Element("summary") is null) _warnings.Add($"{title}.{f.Name} has no summary");
                }
                sb.Append('\n');
                return Finish(sb);
            }

            var members = Members(t).ToList();
            var groups = new (string Name, IEnumerable<MemberInfo> Items)[]
            {
                ("Constructors", members.OfType<ConstructorInfo>().OrderBy(c => c.GetParameters().Length).ThenBy(c => Signatures.Constructor(c), StringComparer.Ordinal)),
                ("Fields", members.OfType<FieldInfo>().OrderBy(f => f.Name, StringComparer.Ordinal)),
                ("Properties", members.OfType<PropertyInfo>().OrderBy(p => p.GetIndexParameters().Length).ThenBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => Signatures.Property(p), StringComparer.Ordinal)),
                ("Methods", members.OfType<MethodInfo>().Where(m => !m.Name.StartsWith("op_", StringComparison.Ordinal)).OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.GetParameters().Length).ThenBy(m => Signatures.Method(m), StringComparer.Ordinal)),
                ("Operators", members.OfType<MethodInfo>().Where(m => m.Name.StartsWith("op_", StringComparison.Ordinal)).OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => Signatures.Method(m), StringComparer.Ordinal)),
            };

            foreach (var (name, items) in groups)
            {
                var list = items.ToList();
                if (list.Count == 0) continue;
                sb.Append($"## {name}\n\n| Member | Description |\n| --- | --- |\n");
                foreach (var m in list)
                {
                    string mid = Signatures.Id(m);
                    sb.Append($"| [`{ShortSignature(m).Replace("|", "\\|")}`](#{Signatures.Anchor(mid)}) | {_md.OneLine(Doc(mid, m)?.Element("summary"), page)} |\n");
                }
                sb.Append('\n');
            }

            foreach (var (name, items) in groups)
            {
                var list = items.ToList();
                if (list.Count == 0) continue;
                sb.Append($"## {Singular(name)} details\n\n");
                foreach (var m in list) MemberSection(sb, t, m, page);
            }

            AppendInherited(sb, t, page);
            return Finish(sb);
        }

        /// <summary>The value of an enum member whatever its underlying type (decimal holds every integral type, ulong included).</summary>
        private static decimal EnumValue(FieldInfo f) => Convert.ToDecimal(f.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture);

        private static string Singular(string group) => group switch { "Properties" => "Property", _ => group.TrimEnd((char)115) };

        private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..];

        private static string Escape(string s) => s.Replace("<", "&lt;").Replace(">", "&gt;");

        private static string Finish(StringBuilder sb) => Regex.Replace(sb.ToString().TrimEnd(), "\n{3,}", "\n\n") + "\n";

        private string InheritanceChain(Type t)
        {
            if (t.IsEnum || t.IsInterface) return "";
            var chain = new List<string>();
            for (var b = t; b is not null; b = b.BaseType)
            {
                string name = Signatures.TypeName(b);
                string id = Signatures.IdOfType(b);
                chain.Add(b == t ? $"`{name}`" : _pages.ContainsKey(id) ? $"[`{name}`]({PageName(b)})" : MarkdownRenderer.Code(name));
            }
            chain.Reverse();
            return chain.Count > 1 ? string.Join(" → ", chain) : "";
        }

        private void AppendSections(StringBuilder sb, XElement? doc, string page, bool includeSummary)
        {
            if (doc is null) return;
            if (includeSummary && _md.Blocks(doc.Element("summary"), page) is { Length: > 0 } s) sb.Append(s).Append("\n\n");
            Labeled(sb, "Remarks", _md.Blocks(doc.Element("remarks"), page));
            foreach (XElement ex in doc.Elements("example")) Labeled(sb, "Example", _md.Blocks(ex, page));
        }

        private static void Labeled(StringBuilder sb, string label, string body)
        {
            if (body.Length > 0) sb.Append($"**{label}**\n\n{body}\n\n");
        }

        // ---- Members ----------------------------------------------------------------------------------------------

        private static string ShortSignature(MemberInfo m) => m switch
        {
            ConstructorInfo c => $"{c.DeclaringType!.Name.Split('`')[0]}({string.Join(", ", c.GetParameters().Select(p => Signatures.TypeName(p.ParameterType)))})",
            MethodInfo mi when mi.Name.StartsWith("op_", StringComparison.Ordinal) => OperatorHeading(mi) + "(" + string.Join(", ", mi.GetParameters().Select(p => Signatures.TypeName(p.ParameterType))) + ")",
            MethodInfo mi => $"{mi.Name}{(mi.IsGenericMethod ? "<" + string.Join(", ", mi.GetGenericArguments().Select(a => a.Name)) + ">" : "")}({string.Join(", ", mi.GetParameters().Select(p => Signatures.TypeName(p.ParameterType)))})",
            PropertyInfo { } p when p.GetIndexParameters().Length > 0 => $"this[{string.Join(", ", p.GetIndexParameters().Select(i => Signatures.TypeName(i.ParameterType)))}]",
            _ => m.Name,
        };

        /// <summary>The declaration of an operator without its accessibility and parameters, e.g. "static bool operator ==".</summary>
        private static string OperatorHeading(MethodInfo mi)
        {
            const string accessibility = "public ";
            string declaration = Signatures.Method(mi).Split('(')[0];
            return declaration.StartsWith(accessibility, StringComparison.Ordinal) ? declaration[accessibility.Length..] : declaration;
        }

        private void MemberSection(StringBuilder sb, Type owner, MemberInfo m, string page)
        {
            string id = Signatures.Id(m);
            XElement? doc = Doc(id, m);
            string label = $"{owner.Name.Split('`')[0]}.{ShortSignature(m)}";
            // The symbol of an operator is dropped from heading ids, so the operator's name tells == from !=.
            string operatorName = m is MethodInfo { Name: var n } && n.StartsWith("op_", StringComparison.Ordinal) ? $" ({n[3..].ToLowerInvariant()})" : "";
            sb.Append($"<a id=\"{Signatures.Anchor(id)}\"></a>\n### `{ShortSignature(m)}`{operatorName}\n\n");
            string summary = _md.Blocks(doc?.Element("summary"), page);
            if (summary.Length == 0) _warnings.Add($"{label} has no summary");
            else sb.Append(summary).Append("\n\n");

            string signature = m switch
            {
                ConstructorInfo c => Signatures.Constructor(c),
                MethodInfo mi => Signatures.Method(mi),
                PropertyInfo p => Signatures.Property(p),
                FieldInfo f => Signatures.Field(f),
                _ => m.Name,
            };
            sb.Append("```csharp\n").Append(signature).Append("\n```\n\n");

            if (doc is null) return;
            // type parameters, parameters, return value
            if (m is MethodInfo { IsGenericMethod: true } generic)
                Bullets(sb, "Type parameters", generic.GetGenericArguments().Select(a => (a.Name, _md.Blocks(doc.Elements("typeparam").FirstOrDefault(x => (string?)x.Attribute("name") == a.Name), page))), $"{label} type parameter");
            var parameters = m switch { MethodBase mb => mb.GetParameters(), PropertyInfo p => p.GetIndexParameters(), _ => [] };
            if (parameters.Length > 0)
                Bullets(sb, "Parameters", parameters.Select(p => (p.Name!, _md.Blocks(doc.Elements("param").FirstOrDefault(x => (string?)x.Attribute("name") == p.Name), page))), $"{label} parameter");
            if (m is MethodInfo mi2 && mi2.ReturnType != typeof(void))
                Labeled(sb, "Returns", _md.Blocks(doc.Element("returns"), page));
            if (m is PropertyInfo or FieldInfo && doc.Element("value") is { } value) Labeled(sb, "Value", _md.Blocks(value, page));

            var exceptions = doc.Elements("exception").Select(x => (Link: _md.Link((string?)x.Attribute("cref") ?? "", page), Text: _md.Blocks(x, page))).ToList();
            if (exceptions.Count > 0)
            {
                sb.Append("**Exceptions**\n\n");
                foreach (var (link, text) in exceptions) sb.Append(text.Length > 0 ? $"- {link} — {text.Replace("\n\n", " ")}\n" : $"- {link}\n");
                sb.Append('\n');
            }
            Labeled(sb, "Remarks", _md.Blocks(doc.Element("remarks"), page));
            foreach (XElement ex in doc.Elements("example")) Labeled(sb, "Example", _md.Blocks(ex, page));
            var see = doc.Elements("seealso").Select(x => _md.Link((string?)x.Attribute("cref") ?? "", page)).Where(l => l.Length > 0).ToList();
            if (see.Count > 0) Labeled(sb, "See also", string.Join("\n", see.Select(l => "- " + l)));
        }

        private void Bullets(StringBuilder sb, string label, IEnumerable<(string Name, string Text)> items, string warning)
        {
            var list = items.ToList();
            sb.Append($"**{label}**\n\n");
            foreach (var (name, text) in list)
            {
                if (text.Length == 0) _warnings.Add($"{warning} '{name}' is not documented");
                sb.Append(text.Length > 0 ? $"- `{name}` — {text.Replace("\n\n", " ")}\n" : $"- `{name}`\n");
            }
            sb.Append('\n');
        }

        private void AppendInherited(StringBuilder sb, Type t, string page)
        {
            if (t.IsEnum || t.IsInterface || t.IsValueType) return;
            var own = Members(t).Select(m => (m.Name, Params: m is MethodBase mb ? string.Join(",", mb.GetParameters().Select(p => p.ParameterType.FullName)) : "")).ToHashSet();
            var lines = new List<string>();
            for (var b = t.BaseType; b is not null && b != typeof(object); b = b.BaseType)
            {
                if (!_pages.ContainsKey(Signatures.IdOfType(b))) continue;
                foreach (var m in Members(b).Where(m => m is not ConstructorInfo))
                {
                    var key = (m.Name, Params: m is MethodBase mb ? string.Join(",", mb.GetParameters().Select(p => p.ParameterType.FullName)) : "");
                    if (!own.Add(key)) continue; // overridden or hidden in a more derived class
                    string id = Signatures.Id(m);
                    string summary = _md.OneLine(Doc(id, m)?.Element("summary"), page);
                    lines.Add($"- [`{b.Name}.{ShortSignature(m)}`]({PageName(b)}#{Signatures.Anchor(id)}){(summary.Length > 0 ? " — " + summary : "")}");
                }
            }
            if (lines.Count == 0) return;
            sb.Append("## Inherited members\n\n").Append(string.Join("\n", lines)).Append("\n\n");
        }

        // ---- Index and navigation ---------------------------------------------------------------------------------

        private string IndexPage(List<Type> types)
        {
            var sb = new StringBuilder();
            sb.Append("# API reference\n\nEvery public type of the library, by namespace. The pages are generated from the XML documentation comments of the code.\n\n");
            foreach (var group in types.GroupBy(t => t.Namespace))
            {
                sb.Append($"## Namespace `{group.Key}`\n\n| Type | Kind | Description |\n| --- | --- | --- |\n");
                foreach (var t in group)
                {
                    string summary = _md.OneLine(Doc(Signatures.IdOfType(t), null)?.Element("summary"), "index.md");
                    // in a code span the name is shown as written: an entity (&lt;) would be shown literally
                    sb.Append($"| [`{Signatures.TypeName(t)}`]({PageName(t)}) | {Signatures.Kind(t)} | {summary} |\n");
                }
                sb.Append('\n');
            }
            return Finish(sb);
        }

        private void UpdateNavigation(List<Type> types)
        {
            string path = Path.Combine(root, "mkdocs.yml");
            if (!File.Exists(path)) return;
            string text = ReadLf(path);
            const string begin = "  # BEGIN API NAVIGATION", end = "  # END API NAVIGATION";
            int a = text.IndexOf(begin, StringComparison.Ordinal), b = text.IndexOf(end, StringComparison.Ordinal);
            if (a < 0 || b < a)
            {
                _problems.Add("mkdocs.yml has no '# BEGIN API NAVIGATION' / '# END API NAVIGATION' markers, in this order");
                return;
            }
            var sb = new StringBuilder(begin).Append('\n');
            sb.Append("  - API reference:\n    - Overview: api/index.md\n");
            foreach (var group in types.GroupBy(t => t.Namespace))
            {
                sb.Append($"    - {group.Key}:\n");
                foreach (var t in group) sb.Append($"      - \"{Signatures.TypeName(t).Replace("\"", "\\\"")}\": api/{PageName(t)}\n");
            }
            _output["mkdocs.yml"] = text[..a] + sb + text[b..];
            if (!_output["mkdocs.yml"].EndsWith('\n')) _output["mkdocs.yml"] += "\n";
        }

        // ---- Examples ---------------------------------------------------------------------------------------------

        private void InjectExamples()
        {
            string examples = Path.Combine(_docs, "examples");
            var regions = new Dictionary<string, string>();
            if (Directory.Exists(examples))
            {
                foreach (string file in Directory.GetFiles(examples, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")).Order(StringComparer.Ordinal))
                {
                    var lines = ReadLf(file).Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var m = RegionStart().Match(lines[i]);
                        if (!m.Success) continue;
                        int j = i + 1;
                        while (j < lines.Length && !lines[j].TrimStart().StartsWith("#endregion", StringComparison.Ordinal)) j++;
                        string body = DedentLines(lines[(i + 1)..j]);
                        if (!regions.TryAdd(m.Groups[1].Value, body)) _problems.Add($"example '{m.Groups[1].Value}' is defined twice");
                    }
                }
            }

            var used = new HashSet<string>();
            foreach (string file in Directory.Exists(_docs) ? Directory.GetFiles(_docs, "*.md", SearchOption.TopDirectoryOnly) : [])
            {
                string original = ReadLf(file);
                string updated = ExampleBlock().Replace(original, match =>
                {
                    string name = match.Groups[1].Value;
                    if (!regions.TryGetValue(name, out string? code))
                    {
                        _problems.Add($"{Path.GetFileName(file)} uses the unknown example '{name}'");
                        return match.Value;
                    }
                    used.Add(name);
                    return $"<!-- example: {name} -->\n```csharp\n{code}\n```\n<!-- /example -->";
                });
                _output[Path.GetRelativePath(root, file)] = updated;
            }
            foreach (string unused in regions.Keys.Except(used)) _problems.Add($"example '{unused}' is not used by any page");
        }

        private static string DedentLines(string[] lines)
        {
            var list = lines.ToList();
            while (list.Count > 0 && list[0].Trim().Length == 0) list.RemoveAt(0);
            while (list.Count > 0 && list[^1].Trim().Length == 0) list.RemoveAt(list.Count - 1);
            int indent = list.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            return string.Join("\n", list.Select(l => l.Length >= indent ? l[indent..].TrimEnd() : l.TrimEnd()));
        }

        [GeneratedRegex(@"^\s*#region\s+Example:(\S+)\s*$")]
        private static partial Regex RegionStart();

        [GeneratedRegex(@"<!-- example: (\S+) -->\n.*?<!-- /example -->", RegexOptions.Singleline)]
        private static partial Regex ExampleBlock();
    }
}
