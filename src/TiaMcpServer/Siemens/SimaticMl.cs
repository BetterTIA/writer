using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>One edit made to an exported SimaticML document, as reported to the caller.</summary>
    public sealed class XmlChange
    {
        /// <summary>Name of the block or PLC data type the edit is in.</summary>
        public string Object { get; set; } = string.Empty;

        /// <summary>"interface", "network 3", "network 3 Title [en-US]".</summary>
        public string Where { get; set; } = string.Empty;

        public string Old { get; set; } = string.Empty;

        public string New { get; set; } = string.Empty;
    }

    public sealed class MemberRenameResult
    {
        public List<XmlChange> Changes { get; } = new List<XmlChange>();

        /// <summary>Places that mention an old member name but could not be resolved to a declaration.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Names of the documents that were edited and have to be imported back.</summary>
        public HashSet<string> Changed { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Names of the documents that are not edited but depend on the changed interface: data
        /// blocks built from the owner, and every block that reads one of those or calls the
        /// owner. TIA Portal marks them inconsistent after the import - also a block that reads
        /// a member that was not renamed at all.
        /// </summary>
        public HashSet<string> Dependents { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Edits on exported SimaticML (the .xml of 'ExportXmlBlock' / 'ExportXmlType') for the things
    /// Openness has no setter for: network titles and comments, and the names of block interface,
    /// DB and PLC data type members.
    ///
    /// Callers: Portal.AnetFork.cs (export -> these edits -> import) and Test9SimaticMl. Fork
    /// addition - no existing API changed. Works on XDocuments in memory and touches neither
    /// Openness nor the file system, which is what lets the tests run without TIA Portal.
    ///
    /// Elements are matched by local name only: the Interface / FlgNet / StructuredText / Graph
    /// namespaces carry a schema version (v4, v5, v6) that changes between TIA Portal versions.
    /// </summary>
    public static class SimaticMl
    {
        #region xml helpers

        private static IEnumerable<XElement> Kids(XElement? parent, string name) =>
            parent == null ? Enumerable.Empty<XElement>() : parent.Elements().Where(e => e.Name.LocalName == name);

        private static XElement? Kid(XElement? parent, string name) => Kids(parent, name).FirstOrDefault();

        private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string NameOf(XElement element) => (string?)element.Attribute("Name") ?? string.Empty;

        /// <summary>The exported object itself: the 'SW.Blocks.FB', 'SW.Types.PlcStruct', ... element.</summary>
        private static XElement? Body(XDocument doc) =>
            doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("SW.", StringComparison.Ordinal));

        private static string? Attribute(XDocument doc, string name) => Kid(Kid(Body(doc), "AttributeList"), name)?.Value;

        /// <summary>Name of the exported block or PLC data type.</summary>
        public static string ObjectName(XDocument doc) => Attribute(doc, "Name") ?? string.Empty;

        private static IEnumerable<XElement> SectionMembers(XElement? parent) =>
            Kids(parent, "Sections").SelectMany(s => Kids(s, "Section")).SelectMany(s => Kids(s, "Member"));

        /// <summary>The members an object declares in its own interface, all sections together.</summary>
        private static IEnumerable<XElement> Own(XDocument doc) =>
            SectionMembers(Kid(Kid(Body(doc), "AttributeList"), "Interface"));

        /// <summary>
        /// The user type a data type string refers to: "\"Motor\"" and "Array[0..3] of \"Motor\""
        /// both give "Motor". Null for elementary and system types, which are not quoted.
        /// </summary>
        public static string? TypeName(string? datatype)
        {
            if (datatype == null)
            {
                return null;
            }

            var type = datatype.Trim();
            var of = type.IndexOf("] of ", StringComparison.OrdinalIgnoreCase);

            if (of >= 0 && type.StartsWith("Array", StringComparison.OrdinalIgnoreCase))
            {
                type = type.Substring(of + 5).Trim();
            }

            return type.Length > 2 && type[0] == '"' ? type.Trim('"') : null;
        }

        private static string Location(XElement element)
        {
            var unit = element.AncestorsAndSelf().FirstOrDefault(e => e.Name.LocalName == "SW.Blocks.CompileUnit");

            return unit == null
                ? "interface"
                : "network " + (unit.ElementsBeforeSelf().Count(e => e.Name.LocalName == "SW.Blocks.CompileUnit") + 1);
        }

        #endregion

        #region network titles and comments

        private static readonly string[] NetworkTexts = { "Title", "Comment" };

        private static List<XElement> Networks(XDocument doc) =>
            Kids(Kid(Body(doc), "ObjectList"), "SW.Blocks.CompileUnit").ToList();

        /// <summary>The text of a network title or comment, one entry per project language.</summary>
        private static IEnumerable<(string Culture, XElement Text)> Texts(XElement network, string composition)
        {
            foreach (var text in Kids(Kid(network, "ObjectList"), "MultilingualText")
                         .Where(t => (string?)t.Attribute("CompositionName") == composition))
            {
                foreach (var item in Kids(Kid(text, "ObjectList"), "MultilingualTextItem"))
                {
                    var attributes = Kid(item, "AttributeList");
                    var value = Kid(attributes, "Text");

                    if (value != null)
                    {
                        yield return (Kid(attributes, "Culture")?.Value ?? string.Empty, value);
                    }
                }
            }
        }

        private static void SetText(List<XmlChange> changes, XDocument doc, string where, XElement text, string value)
        {
            if (text.Value == value)
            {
                return;
            }

            changes.Add(new XmlChange { Object = ObjectName(doc), Where = where, Old = text.Value, New = value });

            text.Value = value;
        }

        /// <summary>
        /// Sets the title and/or comment of one network. A null <paramref name="title"/> or
        /// <paramref name="comment"/> keeps the current text.
        /// </summary>
        /// <param name="network">1-based, as TIA Portal numbers the networks.</param>
        /// <param name="culture">E.g. "en-US"; may be empty when the project has one language.</param>
        public static List<XmlChange> SetNetworkText(XDocument doc, int network, string? title, string? comment, string culture)
        {
            var networks = Networks(doc);

            if (network < 1 || network > networks.Count)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"Network {network} does not exist; '{ObjectName(doc)}' has {networks.Count} network(s).");
            }

            var changes = new List<XmlChange>();
            var values = new[] { title, comment };

            for (var i = 0; i < NetworkTexts.Length; i++)
            {
                if (values[i] == null)
                {
                    continue;
                }

                var all = Texts(networks[network - 1], NetworkTexts[i]).ToList();
                var chosen = all.Where(t => string.IsNullOrEmpty(culture) || Same(t.Culture, culture)).ToList();

                if (chosen.Count != 1)
                {
                    throw new PortalException(PortalErrorCode.InvalidParams,
                        $"The network {NetworkTexts[i]} exists in {all.Count} language(s) ({string.Join(", ", all.Select(t => t.Culture))}); " +
                        "pass one of them as 'culture'.");
                }

                SetText(changes, doc, $"network {network} {NetworkTexts[i]} [{chosen[0].Culture}]", chosen[0].Text, values[i]!);
            }

            return changes;
        }

        /// <summary>
        /// Replaces every key of <paramref name="replacements"/> by its value in one pass
        /// (literal, case-sensitive), so "1" to "2" together with "2" to "1" swaps instead of
        /// collapsing both into one.
        /// </summary>
        public static string ReplaceAll(string text, IDictionary<string, string> replacements)
        {
            if (replacements.Count == 0 || replacements.Keys.Any(string.IsNullOrEmpty))
            {
                throw new PortalException(PortalErrorCode.InvalidParams, "Give at least one 'find=replace' pair with a non-empty 'find'.");
            }

            // Longest first: "Pump 10" must not be eaten by the pair for "Pump 1".
            var pattern = string.Join("|", replacements.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));

            return Regex.Replace(text, pattern, match => replacements[match.Value]);
        }

        /// <summary>
        /// Applies <see cref="ReplaceAll"/> to the titles and comments of every network of the
        /// block and, with <paramref name="all"/>, also to the title and comment of the block
        /// itself and to the texts inside GRAPH sequences (sequence comment, step titles,
        /// transition comments). Step and transition names are left alone: a step name is also
        /// a member of the block's interface, so changing it is a member rename, not a text edit.
        /// </summary>
        /// <param name="culture">Empty replaces in every language.</param>
        public static List<XmlChange> ReplaceNetworkText(XDocument doc, IDictionary<string, string> replacements, string culture, bool all = false)
        {
            var changes = new List<XmlChange>();
            var networks = Networks(doc);

            void Replace(string where, string textCulture, XElement text)
            {
                if (string.IsNullOrEmpty(culture) || Same(textCulture, culture))
                {
                    SetText(changes, doc, $"{where} [{textCulture}]", text, ReplaceAll(text.Value, replacements));
                }
            }

            if (all)
            {
                foreach (var composition in NetworkTexts)
                {
                    foreach (var (textCulture, text) in Texts(Body(doc)!, composition))
                    {
                        Replace($"block {composition}", textCulture, text);
                    }
                }
            }

            for (var n = 0; n < networks.Count; n++)
            {
                foreach (var composition in NetworkTexts)
                {
                    foreach (var (textCulture, text) in Texts(networks[n], composition))
                    {
                        Replace($"network {n + 1} {composition}", textCulture, text);
                    }
                }

                if (!all)
                {
                    continue;
                }

                foreach (var text in networks[n].Descendants().Where(e =>
                             e.Name.LocalName == "MultiLanguageText" && e.Ancestors().Any(a => a.Name.LocalName == "Graph")))
                {
                    var node = text.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Step" || a.Name.LocalName == "Transition");
                    var owner = node == null ? "sequence" : $"{node.Name.LocalName.ToLowerInvariant()} {(string?)node.Attribute("Number")}";

                    Replace($"network {n + 1} {owner} {text.Parent?.Name.LocalName}", (string?)text.Attribute("Lang") ?? string.Empty, text);
                }
            }

            return changes;
        }

        #endregion

        #region member rename

        // An operand as GRAPH writes it into an action: plain text, not an <Access> element.
        private static readonly Regex Segment = new Regex(@"(""[^""]+""|[A-Za-z_]\w*)(\[[^\]]*\])?", RegexOptions.Compiled);

        private static readonly Regex Operand = new Regex(
            @"(?<![\w""#])(#|(?=""))" + Segment + @"(\." + Segment + @")*", RegexOptions.Compiled);

        private static readonly Regex PlainName = new Regex(@"^[A-Za-z_]\w*$", RegexOptions.Compiled);

        /// <summary>
        /// Renames members declared by <paramref name="owner"/> and every reference to them in
        /// <paramref name="docs"/>. All renames are resolved against the unedited documents first
        /// and applied together, so a swap (A to B, B to A) works in one call.
        ///
        /// A reference is renamed only when its whole access path resolves, declaration by
        /// declaration, to the member being renamed - a member of the same name in another
        /// structure is left alone. What mentions an old name but cannot be resolved (a block that
        /// was not exported, a GRAPH action in an unknown shape) is reported as a warning instead
        /// of being guessed.
        /// </summary>
        /// <param name="docs">Every exported block and PLC data type of the PLC, by object name.</param>
        /// <param name="tagTypes">Data type of every PLC tag, by tag name.</param>
        /// <param name="owner">The block or PLC data type that declares the members.</param>
        /// <param name="renames">Member path inside the owner ("Config.Speed") to its new name ("Velocity").</param>
        public static MemberRenameResult RenameMembers(
            IDictionary<string, XDocument> docs,
            IDictionary<string, string> tagTypes,
            string owner,
            IDictionary<string, string> renames)
        {
            if (!docs.TryGetValue(owner, out var ownerDoc))
            {
                throw new PortalException(PortalErrorCode.NotFound, $"'{owner}' was not exported, so its members cannot be renamed.");
            }

            var result = new MemberRenameResult();
            var resolver = new Resolver(docs, tagTypes);
            var targets = new Dictionary<XElement, string>();
            var paths = new Dictionary<XElement, string[]>();

            foreach (var rename in renames)
            {
                // shortcut: a member path is split at '.', so members whose own name contains a dot are not addressable; quote-aware parsing if that is ever needed.
                var path = rename.Key.Split('.').Select(s => s.Trim().Trim('"')).ToArray();
                var target = Navigate(Own(ownerDoc), path)
                    ?? throw new PortalException(PortalErrorCode.NotFound,
                        $"Member '{rename.Key}' is not declared in '{owner}' itself. A member that comes from a PLC data type " +
                        "or a function block has to be renamed in that type.");

                targets[target] = rename.Value;
                paths[target] = path;
            }

            foreach (var target in targets.Keys)
            {
                // Members of all interface sections share one namespace, members of a struct their own.
                var siblings = target.Parent?.Name.LocalName == "Section" ? Own(ownerDoc) : Kids(target.Parent, "Member");
                var duplicate = siblings
                    .Select(s => targets.TryGetValue(s, out var renamed) ? renamed : NameOf(s))
                    .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(g => g.Count() > 1);

                if (duplicate != null)
                {
                    throw new PortalException(PortalErrorCode.InvalidParams,
                        $"'{owner}' would have two members named '{duplicate.Key}' next to each other.");
                }
            }

            // Everything whose layout follows from the owner: instance DBs, DBs and types with a
            // member of the owner's type, and so on until nothing new is found.
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { owner };

            for (var grew = true; grew;)
            {
                grew = false;

                foreach (var pair in docs.Where(d => !changed.Contains(d.Key)))
                {
                    var members = Kid(Kid(Body(pair.Value), "AttributeList"), "Interface")?.Descendants()
                        .Where(e => e.Name.LocalName == "Member") ?? Enumerable.Empty<XElement>();

                    if (changed.Contains(Attribute(pair.Value, "InstanceOfName") ?? string.Empty)
                        || members.Any(m => changed.Contains(TypeName((string?)m.Attribute("Datatype")) ?? string.Empty)))
                    {
                        changed.Add(pair.Key);
                        grew = true;
                    }
                }
            }

            var oldNames = new HashSet<string>(targets.Keys.Select(NameOf), StringComparer.OrdinalIgnoreCase);
            var parameters = targets
                .Where(t => t.Key.Parent?.Name.LocalName == "Section")
                .ToDictionary(t => NameOf(t.Key), t => t.Value, StringComparer.OrdinalIgnoreCase);
            var edits = new List<Action>();

            void Record(string doc, XElement at, string oldText, string newText)
            {
                result.Changes.Add(new XmlChange { Object = doc, Where = Location(at), Old = oldText, New = newText });
                result.Changed.Add(doc);
            }

            foreach (var target in targets)
            {
                var declaration = target.Key;
                var newName = target.Value;

                edits.Add(() => declaration.SetAttributeValue("Name", newName));
                Record(owner, declaration, string.Join(".", paths[declaration]), newName);
            }

            foreach (var pair in docs)
            {
                var name = pair.Key;
                var doc = pair.Value;

                if (changed.Contains(name) || doc.Descendants().Any(e => e.Name.LocalName == "CallInfo" && changed.Contains(NameOf(e))))
                {
                    result.Dependents.Add(name);
                }

                // Operands of LAD, FBD, STL and SCL, and the instance of a call.
                foreach (var symbol in doc.Descendants().Where(e => e.Name.LocalName == "Symbol" || e.Name.LocalName == "Instance"))
                {
                    var scope = (string?)(symbol.Name.LocalName == "Symbol" ? symbol.Parent : symbol)?.Attribute("Scope");
                    var components = Kids(symbol, "Component").ToList();

                    if ((scope != "LocalVariable" && scope != "GlobalVariable") || components.Count == 0)
                    {
                        continue;
                    }

                    var names = components.Select(NameOf).ToList();
                    var local = scope == "LocalVariable";
                    var (declarations, unresolved) = resolver.Resolve(names, local, doc);
                    var renamed = names.ToList();

                    if (resolver.Touches(names, local, declarations, changed))
                    {
                        result.Dependents.Add(name);
                    }

                    for (var i = 0; i < names.Count; i++)
                    {
                        if (declarations[i] != null && targets.TryGetValue(declarations[i]!, out var newName))
                        {
                            var component = components[i];

                            edits.Add(() => component.SetAttributeValue("Name", newName));
                            renamed[i] = newName;
                        }
                    }

                    if (!renamed.SequenceEqual(names))
                    {
                        Record(name, symbol, Render(names, local), Render(renamed, local));
                    }
                    else if (names.Skip(unresolved).Any(oldNames.Contains))
                    {
                        result.Warnings.Add($"{name}, {Location(symbol)}: '{Render(names, local)}' could not be resolved and was left unchanged.");
                    }
                }

                // Operands of GRAPH actions, which the export keeps as text.
                foreach (var token in doc.Descendants().Where(e => e.Name.LocalName == "Token" && e.Parent?.Name.LocalName == "Action"))
                {
                    var text = (string?)token.Attribute("Text") ?? string.Empty;
                    var action = token;
                    var replaced = Operand.Replace(text, match =>
                    {
                        var local = match.Value[0] == '#';
                        var segments = Segment.Matches(match.Value).Cast<Match>().ToList();
                        var names = segments.Select(s => s.Groups[1].Value.Trim('"')).ToList();
                        var (declarations, unresolved) = resolver.Resolve(names, local, doc);
                        var rebuilt = local ? "#" : string.Empty;

                        if (resolver.Touches(names, local, declarations, changed))
                        {
                            result.Dependents.Add(name);
                        }

                        for (var i = 0; i < segments.Count; i++)
                        {
                            var part = segments[i].Groups[1].Value;

                            if (declarations[i] != null && targets.TryGetValue(declarations[i]!, out var newName))
                            {
                                part = part[0] == '"' || !PlainName.IsMatch(newName) ? $"\"{newName}\"" : newName;
                            }

                            rebuilt += (i == 0 ? string.Empty : ".") + part + segments[i].Groups[2].Value;
                        }

                        if (rebuilt == match.Value && names.Skip(unresolved).Any(oldNames.Contains))
                        {
                            result.Warnings.Add($"{name}, {Location(action)}: GRAPH action '{match.Value}' could not be resolved and was left unchanged.");
                        }

                        return rebuilt;
                    });

                    if (replaced != text)
                    {
                        edits.Add(() => action.SetAttributeValue("Text", replaced));
                        Record(name, action, text, replaced);
                    }
                }

                // Formal parameters at the calls of the owner: the parameter list, and in LAD/FBD the wire that names the pin.
                if (parameters.Count > 0)
                {
                    foreach (var call in doc.Descendants().Where(e => e.Name.LocalName == "CallInfo" && Same(NameOf(e), owner)))
                    {
                        var pins = Kids(call, "Parameter");

                        if (call.Parent?.Name.LocalName == "Call")
                        {
                            var uid = (string?)call.Parent.Attribute("UId");

                            pins = pins.Concat(call.Ancestors()
                                .Where(e => e.Name.LocalName == "FlgNet")
                                .Take(1)
                                .SelectMany(net => net.Descendants())
                                .Where(e => e.Name.LocalName == "NameCon" && (string?)e.Attribute("UId") == uid));
                        }

                        foreach (var pin in pins.Where(p => parameters.ContainsKey(NameOf(p))).ToList())
                        {
                            var newName = parameters[NameOf(pin)];

                            edits.Add(() => pin.SetAttributeValue("Name", newName));

                            if (pin.Name.LocalName == "Parameter")
                            {
                                Record(name, pin, $"{owner}({NameOf(pin)})", $"{owner}({newName})");
                            }
                        }
                    }
                }

                // The copy of the owner's members that an instance DB, or a member of the owner's
                // type, carries. Renamed so an edited document stays consistent; it makes the
                // document worth importing only where it holds a start value of its own.
                var copies = Kid(Kid(Body(doc), "AttributeList"), "Interface")?.Descendants()
                    .Where(e => e.Name.LocalName == "Member" && Same(TypeName((string?)e.Attribute("Datatype")), owner))
                    .Select(SectionMembers)
                    .ToList() ?? new List<IEnumerable<XElement>>();

                if (!ReferenceEquals(doc, ownerDoc) && Same(Attribute(doc, "InstanceOfName"), owner))
                {
                    copies.Add(Own(doc));
                }

                foreach (var copy in copies)
                {
                    foreach (var target in targets)
                    {
                        var member = Navigate(copy, paths[target.Key]);
                        var newName = target.Value;

                        if (member == null)
                        {
                            continue;
                        }

                        edits.Add(() => member.SetAttributeValue("Name", newName));

                        if (member.DescendantsAndSelf().Any(e => e.Name.LocalName == "StartValue" && (string?)e.Attribute("Informative") != "true"))
                        {
                            Record(name, member, $"start value of {NameOf(member)}", $"start value of {newName}");
                        }
                    }
                }

                foreach (var element in doc.Descendants().Where(e => e.Name.LocalName == "Subelement"))
                {
                    var path = (string?)element.Attribute("Path") ?? string.Empty;

                    if (path.Split('.', ',', '[', ']').Any(oldNames.Contains))
                    {
                        result.Warnings.Add($"{name}, interface: the start value path '{path}' mentions a renamed member and was left unchanged.");
                    }
                }
            }

            foreach (var edit in edits)
            {
                edit();
            }

            result.Dependents.ExceptWith(result.Changed);

            return result;
        }

        private static XElement? Navigate(IEnumerable<XElement> level, IEnumerable<string> path)
        {
            XElement? found = null;

            foreach (var name in path)
            {
                found = level.FirstOrDefault(m => Same(NameOf(m), name));

                if (found == null)
                {
                    return null;
                }

                level = Kids(found, "Member");
            }

            return found;
        }

        private static string Render(IList<string> names, bool local) =>
            local ? "#" + string.Join(".", names) : "\"" + names[0] + "\"" + string.Concat(names.Skip(1).Select(n => "." + n));

        /// <summary>Follows an access path through the declarations of the exported objects.</summary>
        private sealed class Resolver
        {
            private readonly IDictionary<string, XDocument> _docs;
            private readonly IDictionary<string, string> _tagTypes;

            public Resolver(IDictionary<string, XDocument> docs, IDictionary<string, string> tagTypes)
            {
                _docs = docs;
                _tagTypes = tagTypes;
            }

            /// <summary>
            /// The declaration each component resolves to, and the index of the first component
            /// that could not be resolved (the component count when all were).
            /// </summary>
            public (XElement?[] Declarations, int Unresolved) Resolve(IList<string> names, bool local, XDocument current)
            {
                var declarations = new XElement?[names.Count];
                IEnumerable<XElement> level;
                var i = 0;

                if (local)
                {
                    level = Own(current);
                }
                else if (_docs.TryGetValue(names[0], out var block))
                {
                    // A DB that is an instance of an FB or of a PLC data type is declared there.
                    var of = Attribute(block, "InstanceOfName");

                    level = of != null && _docs.TryGetValue(of, out var type) ? Own(type) : Own(block);
                    i = 1;
                }
                else if (_tagTypes.TryGetValue(names[0], out var tagType))
                {
                    level = TypeMembers(tagType) ?? Enumerable.Empty<XElement>();
                    i = 1;
                }
                else
                {
                    return (declarations, 0);
                }

                for (; i < names.Count; i++)
                {
                    var declaration = level.FirstOrDefault(m => Same(NameOf(m), names[i]));

                    if (declaration == null)
                    {
                        return (declarations, i);
                    }

                    declarations[i] = declaration;

                    // An inline struct declares its members here; a typed member is declared by its
                    // type; for a system type (TON_TIME) the informative copy is all there is.
                    var inline = Kids(declaration, "Member").ToList();

                    level = inline.Count > 0
                        ? inline
                        : TypeMembers((string?)declaration.Attribute("Datatype")) ?? SectionMembers(declaration);
                }

                return (declarations, names.Count);
            }

            /// <summary>
            /// True when an access reaches into something whose layout follows from the owner:
            /// its root is one of the <paramref name="changed"/> blocks or a tag of such a type,
            /// or one of its components is declared in, or typed as, one of them.
            /// </summary>
            public bool Touches(IList<string> names, bool local, XElement?[] declarations, HashSet<string> changed)
            {
                if (!local && (changed.Contains(names[0])
                               || (_tagTypes.TryGetValue(names[0], out var tagType) && changed.Contains(TypeName(tagType) ?? tagType.Trim()))))
                {
                    return true;
                }

                return declarations.Any(d => d != null
                    && (changed.Contains(d.Document == null ? string.Empty : ObjectName(d.Document))
                        || changed.Contains(TypeName((string?)d.Attribute("Datatype")) ?? string.Empty)));
            }

            private IEnumerable<XElement>? TypeMembers(string? datatype)
            {
                var type = TypeName(datatype) ?? datatype?.Trim();

                return type != null && _docs.TryGetValue(type, out var doc) ? Own(doc) : null;
            }
        }

        #endregion
    }
}
