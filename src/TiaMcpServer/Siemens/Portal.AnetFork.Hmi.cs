using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    /// <summary>One HMI tag of a WinCC Unified device, as GetHmiTags lists it.</summary>
    public sealed class HmiTagInfo
    {
        public string Name { get; set; } = string.Empty;

        public string? Table { get; set; }

        public string? Connection { get; set; }

        public string? PlcName { get; set; }

        public string? PlcTag { get; set; }

        public string? Address { get; set; }

        public string? DataType { get; set; }

        /// <summary>True when the tag has a connection but neither a PLC tag nor an address - the binding is lost.</summary>
        public bool Unbound { get; set; }
    }

    /// <summary>A place in the screens that refers to a tag or a screen by name.</summary>
    public sealed class HmiUsage
    {
        public string Screen { get; set; } = string.Empty;

        public string Item { get; set; } = string.Empty;

        /// <summary>"dynamization ProcessValue", "event Tapped", "property event Visible".</summary>
        public string Where { get; set; } = string.Empty;

        /// <summary>'tag' for a tag dynamization, 'script' for a name written as a string in script code.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>The tag of the dynamization, or the script line that holds the name.</summary>
        public string Text { get; set; } = string.Empty;
    }

    public sealed class HmiEditResult
    {
        public bool Applied { get; set; }

        public List<XmlChange> Changes { get; set; } = new List<XmlChange>();

        public List<HmiUsage> Usages { get; set; } = new List<HmiUsage>();

        public List<string> Warnings { get; set; } = new List<string>();
    }

    public partial class Portal
    {
        // ANet fork additions for WinCC Unified (branch 'anet').
        //
        // Callers: the tools in McpServer.AnetFork.Hmi.cs. Affected API: none existing. No file
        // I/O. Everything here uses setters; changes stay in memory until SaveProject.
        //
        // The screen side is walked through IEngineeringObject (GetComposition / GetAttribute):
        // every widget has its own event handler type and there is no common typed base that
        // carries them, so typed access would mean one branch per widget.
        //
        // Not verified against a running TIA Portal when written: which of these setters TIA
        // Portal accepts, whether renaming an HMI tag updates tag dynamizations by itself, and
        // which object offers the compiler for a Unified device.

        #region hmi plumbing

        private HmiSoftware GetHmiSoftwareOrThrow(string softwarePath)
        {
            if (IsProjectNull())
            {
                throw new PortalException(PortalErrorCode.InvalidState, "No project is open in TIA Portal");
            }

            return GetSoftwareContainer(softwarePath)?.Software as HmiSoftware
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"No WinCC Unified HMI software found at '{softwarePath}'. Use 'GetProjectTree' to discover valid software paths.");
        }

        private static IEnumerable<IEngineeringObject> Children(IEngineeringObject parent, string composition)
        {
            IEnumerable? items;

            if (!HasComposition(parent, composition))
            {
                yield break;
            }

            try
            {
                items = parent.GetComposition(composition) as IEnumerable;
            }
            catch (Exception)
            {
                // The object has no composition of that name.
                yield break;
            }

            foreach (var item in items ?? Array.Empty<object>())
            {
                if (item is IEngineeringObject child)
                {
                    yield return child;
                }
            }
        }

        /// <summary>
        /// Asked first because GetComposition throws for a name the object does not have, and an
        /// exception out of Openness costs about 0.1 s: measured live, minutes on a larger project.
        /// </summary>
        private static bool HasComposition(IEngineeringObject parent, string composition)
        {
            try
            {
                return parent.GetCompositionInfos().Any(c => c.Name == composition);
            }
            catch (Exception)
            {
                // Let GetComposition decide.
                return true;
            }
        }

        private static string Text(IEngineeringObject obj, string attribute)
        {
            try
            {
                return obj.GetAttribute(attribute)?.ToString() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Every screen of the device, the ones in screen groups included.</summary>
        private static IEnumerable<IEngineeringObject> HmiScreens(IEngineeringObject parent)
        {
            foreach (var screen in Children(parent, "Screens"))
            {
                yield return screen;
            }

            foreach (var group in Children(parent, "ScreenGroups").Concat(Children(parent, "Groups")))
            {
                foreach (var screen in HmiScreens(group))
                {
                    yield return screen;
                }
            }
        }

        /// <summary>A script or a tag dynamization, with where it sits.</summary>
        private sealed class HmiSpot
        {
            public string Screen = string.Empty;
            public string Item = string.Empty;
            public string Where = string.Empty;
            public IHmiScript? Script;
            public TagDynamization? Tag;
        }

        /// <summary>
        /// The scripts and tag dynamizations of a screen or screen item and of everything below it.
        /// </summary>
        private static IEnumerable<HmiSpot> Spots(string screen, string item, IEngineeringObject obj, int depth = 0)
        {
            foreach (var dynamization in Children(obj, "Dynamizations"))
            {
                var where = "dynamization " + Text(dynamization, "PropertyName");

                if (dynamization is IHmiScript script)
                {
                    yield return new HmiSpot { Screen = screen, Item = item, Where = where, Script = script };
                }
                else if (dynamization is TagDynamization tag)
                {
                    yield return new HmiSpot { Screen = screen, Item = item, Where = where, Tag = tag };
                }
            }

            foreach (var handlers in new[] { "EventHandlers", "PropertyEventHandlers" })
            {
                foreach (var handler in Children(obj, handlers))
                {
                    IHmiScript? script = null;

                    try
                    {
                        script = handler.GetAttribute("Script") as IHmiScript;
                    }
                    catch (Exception)
                    {
                        // A handler without a script.
                    }

                    if (script != null)
                    {
                        var where = handlers == "EventHandlers"
                            ? "event " + Text(handler, "EventType")
                            : $"property event {Text(handler, "PropertyName")} {Text(handler, "EventType")}";

                        yield return new HmiSpot { Screen = screen, Item = item, Where = where, Script = script };
                    }
                }
            }

            // shortcut: only items are walked, not the parts of an item (button content, text part), whose own dynamizations are therefore not seen; walk object-valued attributes too if such a case turns up.
            if (depth < 4)
            {
                foreach (var child in Children(obj, "ScreenItems"))
                {
                    var name = Text(child, "Name");

                    foreach (var spot in Spots(screen, item.Length == 0 ? name : $"{item}/{name}", child, depth + 1))
                    {
                        yield return spot;
                    }
                }
            }
        }

        private static IEnumerable<HmiSpot> AllSpots(HmiSoftware hmi, string screenName = "")
        {
            return HmiScreens(hmi)
                .Select(s => (Name: Text(s, "Name"), Screen: s))
                .Where(s => screenName.Length == 0 || s.Name.Equals(screenName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(s => Spots(s.Name, string.Empty, s.Screen));
        }

        /// <summary>Lines of the scripts that hold <paramref name="name"/> as a quoted string.</summary>
        private static IEnumerable<HmiUsage> ScriptUsages(IEnumerable<HmiSpot> spots, string name)
        {
            var quoted = new Regex("[\"'`]" + Regex.Escape(name) + "[\"'`]");

            foreach (var spot in spots.Where(s => s.Script != null))
            {
                foreach (var line in (spot.Script!.ScriptCode ?? string.Empty).Split('\n').Where(l => quoted.IsMatch(l)))
                {
                    yield return new HmiUsage { Screen = spot.Screen, Item = spot.Item, Where = spot.Where, Kind = "script", Text = line.Trim() };
                }
            }
        }

        #endregion

        #region hmi tags

        public List<HmiTagInfo> GetHmiTags(string softwarePath, string regexName = "")
        {
            return Operation.Run(_logger, nameof(GetHmiTags), PortalErrorCode.NotFound,
                () => GetHmiSoftwareOrThrow(softwarePath).Tags
                    .Where(t => string.IsNullOrEmpty(regexName) || MatchesRegex(t.Name, regexName))
                    .Select(t => new HmiTagInfo
                    {
                        Name = t.Name,
                        Table = t.TagTableName,
                        Connection = t.Connection,
                        PlcName = t.PlcName,
                        PlcTag = t.PlcTag,
                        Address = t.Address,
                        DataType = t.DataType,

                        // Internal tags carry no connection, or a placeholder in angle brackets.
                        Unbound = !string.IsNullOrEmpty(t.Connection) && !t.Connection.StartsWith("<", StringComparison.Ordinal)
                                  && string.IsNullOrEmpty(t.PlcTag) && string.IsNullOrEmpty(t.Address)
                    })
                    .ToList(),
                ("softwarePath", softwarePath), ("regexName", regexName));
        }

        /// <summary>The first segment of a PLC path: the tag or data block it starts at.</summary>
        private static string PlcRoot(string plcPath)
        {
            var match = Regex.Match(plcPath.Trim(), "^\"([^\"]+)\"|^([^.\\[]+)");

            return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        }

        /// <summary>
        /// Points HMI tags at PLC tags or data block members.
        /// </summary>
        /// <param name="assignments">HMI tag name to PLC path.</param>
        /// <param name="plcSoftwarePath">
        /// When given, every path is checked to start at a tag or block that exists in that PLC;
        /// the members after it are not checked - that needs the block interfaces.
        /// </param>
        public HmiEditResult SetHmiTagPlcTags(string softwarePath, IDictionary<string, string> assignments, string plcSoftwarePath = "", bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(SetHmiTagPlcTags), PortalErrorCode.RenameFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var result = new HmiEditResult();
                    var targets = new List<(HmiTag Tag, string Path)>();
                    var missing = new List<string>();
                    HashSet<string>? roots = null;

                    if (!string.IsNullOrEmpty(plcSoftwarePath))
                    {
                        roots = new HashSet<string>(
                            GetTags(plcSoftwarePath).Select(t => t.Name).Concat(GetBlocks(plcSoftwarePath).Select(b => b.Name)),
                            StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        result.Warnings.Add("No plcSoftwarePath was given, so the PLC paths were not checked.");
                    }

                    foreach (var assignment in assignments)
                    {
                        var tag = hmi.Tags.Find(assignment.Key);

                        if (tag == null)
                        {
                            missing.Add(assignment.Key);
                            continue;
                        }

                        if (roots != null && !roots.Contains(PlcRoot(assignment.Value)))
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"'{assignment.Value}' (for HMI tag '{assignment.Key}') does not start at a tag or block of '{plcSoftwarePath}'.");
                        }

                        targets.Add((tag, assignment.Value));
                        result.Changes.Add(new XmlChange { Object = tag.Name, Where = "PlcTag", Old = tag.PlcTag ?? string.Empty, New = assignment.Value });
                    }

                    if (missing.Count > 0)
                    {
                        throw new PortalException(PortalErrorCode.NotFound,
                            $"No HMI tag named: {string.Join(", ", missing)}. Use 'GetHmiTags' to list the available tags.");
                    }

                    if (dryRun)
                    {
                        return result;
                    }

                    foreach (var (tag, path) in targets)
                    {
                        tag.PlcTag = path;

                        // TIA Portal may store the path in its own notation, or refuse it silently.
                        if (string.IsNullOrEmpty(tag.PlcTag))
                        {
                            result.Warnings.Add($"'{tag.Name}': PlcTag is empty after setting '{path}' - TIA Portal did not accept the path.");
                        }
                    }

                    result.Applied = true;

                    return result;
                },
                ("softwarePath", softwarePath), ("count", assignments.Count), ("dryRun", dryRun));
        }

        /// <summary>
        /// Renames HMI tags in a batch (swaps included, see BatchRename.Plan) and lists where the
        /// old names are used: tag dynamizations, and script lines that hold the name as a string.
        /// </summary>
        /// <param name="updateDynamizations">
        /// After the rename, point the tag dynamizations that still carry an old name at the new
        /// one. Script code is never changed here - see ReplaceInHmiScripts.
        /// </param>
        public HmiEditResult RenameHmiTags(string softwarePath, IDictionary<string, string> renames, bool dryRun = true, bool updateDynamizations = true)
        {
            return Operation.Run(_logger, nameof(RenameHmiTags), PortalErrorCode.RenameFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var result = new HmiEditResult();
                    var tags = new List<HmiTag>();
                    var missing = new List<string>();

                    foreach (var rename in renames)
                    {
                        EnsureValidName(rename.Value, allowSlash: true);

                        var tag = hmi.Tags.Find(rename.Key);

                        if (tag == null)
                        {
                            missing.Add(rename.Key);
                            continue;
                        }

                        tags.Add(tag);
                        result.Changes.Add(new XmlChange { Object = tag.Name, Where = "Name", Old = tag.Name, New = rename.Value });
                    }

                    if (missing.Count > 0)
                    {
                        throw new PortalException(PortalErrorCode.NotFound,
                            $"No HMI tag named: {string.Join(", ", missing)}. Use 'GetHmiTags' to list the available tags.");
                    }

                    var oldNames = tags.Select(t => t.Name).ToList();
                    var newNames = oldNames.Select(n => renames[n]).ToList();
                    var steps = BatchRename.Plan(oldNames, newNames, hmi.Tags.Select(t => t.Name).ToList());
                    var spots = AllSpots(hmi).ToList();
                    var rebind = new List<(TagDynamization Dynamization, string NewTag)>();

                    for (var i = 0; i < oldNames.Count; i++)
                    {
                        foreach (var spot in spots.Where(s => s.Tag != null))
                        {
                            var current = spot.Tag!.Tag ?? string.Empty;

                            // The tag itself, or a member of a structured tag.
                            if (current.Equals(oldNames[i], StringComparison.OrdinalIgnoreCase)
                                || current.StartsWith(oldNames[i] + ".", StringComparison.OrdinalIgnoreCase))
                            {
                                rebind.Add((spot.Tag, newNames[i] + current.Substring(oldNames[i].Length)));
                                result.Usages.Add(new HmiUsage { Screen = spot.Screen, Item = spot.Item, Where = spot.Where, Kind = "tag", Text = current });
                            }
                        }

                        result.Usages.AddRange(ScriptUsages(spots, oldNames[i]));
                    }

                    if (result.Usages.Any(u => u.Kind == "script"))
                    {
                        result.Warnings.Add("Script code names renamed tags as strings (see Usages, kind 'script'); it is not changed here - use 'ReplaceInHmiScripts'.");
                    }

                    if (dryRun || steps.Count == 0)
                    {
                        return result;
                    }

                    foreach (var (index, name) in steps)
                    {
                        tags[index].Name = name;
                    }

                    if (updateDynamizations)
                    {
                        // Only where TIA Portal did not follow the rename by itself.
                        foreach (var (dynamization, newTag) in rebind)
                        {
                            if (!newTag.Equals(dynamization.Tag, StringComparison.OrdinalIgnoreCase))
                            {
                                dynamization.Tag = newTag;
                            }
                        }
                    }

                    result.Applied = true;

                    return result;
                },
                ("softwarePath", softwarePath), ("count", renames.Count), ("dryRun", dryRun));
        }

        #endregion

        #region hmi screens and scripts

        private static IEngineeringObject FindScreen(HmiSoftware hmi, string screenName)
        {
            return HmiScreens(hmi).FirstOrDefault(s => Text(s, "Name").Equals(screenName, StringComparison.OrdinalIgnoreCase))
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Screen '{screenName}' not found. Available: {string.Join(", ", HmiScreens(hmi).Select(s => Text(s, "Name")))}.");
        }

        /// <summary>
        /// Renames screens in a batch (swaps included, see BatchRename.Plan) and lists the script
        /// lines that hold an old name as a string - TIA Portal does not rewrite those. The
        /// screens are walked once for the whole batch; a walk takes minutes on a real device.
        /// </summary>
        /// <param name="rewriteScripts">Also write the new names into those script lines.</param>
        public HmiEditResult RenameHmiScreens(string softwarePath, IDictionary<string, string> renames, bool dryRun = true, bool rewriteScripts = false)
        {
            return Operation.Run(_logger, nameof(RenameHmiScreens), PortalErrorCode.RenameFailed,
                () =>
                {
                    var all = HmiScreens(GetHmiSoftwareOrThrow(softwarePath)).Select(s => (Name: Text(s, "Name"), Screen: s)).ToList();
                    var result = new HmiEditResult();
                    var screens = new List<IEngineeringObject>();
                    var oldNames = new List<string>();
                    var newNames = new List<string>();
                    var missing = new List<string>();

                    foreach (var rename in renames)
                    {
                        EnsureValidName(rename.Value);

                        var hit = all.FirstOrDefault(s => s.Name.Equals(rename.Key, StringComparison.OrdinalIgnoreCase));

                        if (hit.Screen == null)
                        {
                            missing.Add(rename.Key);
                            continue;
                        }

                        screens.Add(hit.Screen);
                        oldNames.Add(hit.Name);
                        newNames.Add(rename.Value);
                        result.Changes.Add(new XmlChange { Object = hit.Name, Where = "Name", Old = hit.Name, New = rename.Value });
                    }

                    if (missing.Count > 0)
                    {
                        throw new PortalException(PortalErrorCode.NotFound,
                            $"No screen named: {string.Join(", ", missing)}. Available: {string.Join(", ", all.Select(s => s.Name))}.");
                    }

                    if (oldNames.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No renames were given.");
                    }

                    var steps = BatchRename.Plan(oldNames, newNames, all.Select(s => s.Name).ToList());
                    var map = Enumerable.Range(0, oldNames.Count).ToDictionary(i => oldNames[i], i => newNames[i]);

                    // One pass over all names, so a swap does not rewrite a line twice.
                    var quoted = new Regex("([\"'`])(" + string.Join("|", oldNames.OrderByDescending(n => n.Length).Select(Regex.Escape)) + ")\\1");

                    foreach (var spot in all.SelectMany(s => Spots(s.Name, string.Empty, s.Screen)).Where(s => s.Script != null).ToList())
                    {
                        var code = spot.Script!.ScriptCode ?? string.Empty;

                        if (!quoted.IsMatch(code))
                        {
                            continue;
                        }

                        result.Usages.AddRange(code.Split('\n').Where(l => quoted.IsMatch(l))
                            .Select(l => new HmiUsage { Screen = spot.Screen, Item = spot.Item, Where = spot.Where, Kind = "script", Text = l.Trim() }));

                        if (rewriteScripts && !dryRun)
                        {
                            spot.Script.ScriptCode = quoted.Replace(code, m => m.Groups[1].Value + map[m.Groups[2].Value] + m.Groups[1].Value);
                        }
                    }

                    if (result.Usages.Count > 0)
                    {
                        result.Warnings.Add(rewriteScripts
                            ? "The script lines in Usages name a renamed screen as a string; the new names are written into them."
                            : "Script code names a renamed screen as a string (see Usages); it is not changed here - pass rewriteScripts, or use 'ReplaceInHmiScripts'.");
                    }

                    if (dryRun)
                    {
                        return result;
                    }

                    foreach (var (index, name) in steps)
                    {
                        screens[index].SetAttribute("Name", name);
                    }

                    result.Applied = true;

                    return result;
                },
                ("softwarePath", softwarePath), ("count", renames.Count), ("dryRun", dryRun), ("rewriteScripts", rewriteScripts));
        }

        /// <summary>
        /// Replaces text in the script code of event handlers, property event handlers and script
        /// dynamizations, in one pass over all pairs (SimaticMl.ReplaceAll).
        /// </summary>
        /// <param name="screenName">Empty covers every screen.</param>
        public HmiEditResult ReplaceInHmiScripts(string softwarePath, IDictionary<string, string> replacements, string screenName = "", bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(ReplaceInHmiScripts), PortalErrorCode.RenameFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var result = new HmiEditResult();

                    if (screenName.Length > 0)
                    {
                        FindScreen(hmi, screenName);
                    }

                    foreach (var spot in AllSpots(hmi, screenName).Where(s => s.Script != null).ToList())
                    {
                        var script = spot.Script!;
                        var code = script.ScriptCode ?? string.Empty;
                        var globals = script.GlobalDefinitionAreaScriptCode ?? string.Empty;
                        var newCode = SimaticMl.ReplaceAll(code, replacements);
                        var newGlobals = SimaticMl.ReplaceAll(globals, replacements);
                        var where = $"{(spot.Item.Length == 0 ? "screen" : spot.Item)}, {spot.Where}";

                        // Reported line by line: a whole script per change would drown the caller.
                        var oldLines = (globals + "\n" + code).Split('\n');
                        var newLines = (newGlobals + "\n" + newCode).Split('\n');

                        for (var i = 0; i < Math.Min(oldLines.Length, newLines.Length); i++)
                        {
                            if (oldLines[i] != newLines[i])
                            {
                                result.Changes.Add(new XmlChange { Object = spot.Screen, Where = where, Old = oldLines[i].Trim(), New = newLines[i].Trim() });
                            }
                        }

                        if (dryRun)
                        {
                            continue;
                        }

                        if (newCode != code)
                        {
                            script.ScriptCode = newCode;
                        }

                        if (newGlobals != globals)
                        {
                            script.GlobalDefinitionAreaScriptCode = newGlobals;
                        }
                    }

                    result.Applied = !dryRun && result.Changes.Count > 0;

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("dryRun", dryRun));
        }

        private static IEngineeringObject FindItem(HmiSoftware hmi, string screenName, string itemName)
        {
            var screen = FindScreen(hmi, screenName);

            if (string.IsNullOrEmpty(itemName))
            {
                return screen;
            }

            var items = Children(screen, "ScreenItems").ToList();

            return items.FirstOrDefault(i => Text(i, "Name").Equals(itemName, StringComparison.OrdinalIgnoreCase))
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"Screen item '{itemName}' not found on '{screenName}'. Available: {string.Join(", ", items.Select(i => Text(i, "Name")))}.");
        }

        /// <summary>
        /// Sets one attribute of a screen item (or of the screen, with an empty item name): its
        /// name, a graphic, a text. A multilingual text is set for one language; any other value
        /// is converted to the type the attribute holds now.
        /// </summary>
        public HmiEditResult SetHmiItemAttribute(string softwarePath, string screenName, string itemName, string attribute, string value, string culture = "", bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(SetHmiItemAttribute), PortalErrorCode.RenameFailed,
                () =>
                {
                    var item = FindItem(GetHmiSoftwareOrThrow(softwarePath), screenName, itemName);
                    var current = item.GetAttribute(attribute);
                    var result = new HmiEditResult();
                    var where = $"{(string.IsNullOrEmpty(itemName) ? "screen" : itemName)}.{attribute}";

                    if (current is MultilingualText text)
                    {
                        var texts = text.Items.Where(i => string.IsNullOrEmpty(culture) || i.Language.Culture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase)).ToList();

                        if (texts.Count != 1)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                $"'{attribute}' exists in {text.Items.Count} language(s) ({string.Join(", ", text.Items.Select(i => i.Language.Culture.Name))}); pass one of them as 'culture'.");
                        }

                        result.Changes.Add(new XmlChange { Object = screenName, Where = $"{where} [{texts[0].Language.Culture.Name}]", Old = texts[0].Text ?? string.Empty, New = value });

                        if (!dryRun)
                        {
                            texts[0].Text = value;
                        }
                    }
                    else
                    {
                        var type = current?.GetType() ?? typeof(string);
                        var converted = type.IsEnum ? Enum.Parse(type, value, true) : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);

                        result.Changes.Add(new XmlChange { Object = screenName, Where = where, Old = current?.ToString() ?? string.Empty, New = value });

                        if (!dryRun)
                        {
                            item.SetAttribute(attribute, converted);
                        }
                    }

                    result.Applied = !dryRun;

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName), ("attribute", attribute), ("dryRun", dryRun));
        }

        /// <summary>Points the tag dynamization of one property of a screen item at another HMI tag.</summary>
        public HmiEditResult SetHmiDynamizationTag(string softwarePath, string screenName, string itemName, string property, string tag, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(SetHmiDynamizationTag), PortalErrorCode.RenameFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var item = FindItem(hmi, screenName, itemName);
                    var dynamizations = Children(item, "Dynamizations").OfType<TagDynamization>().ToList();
                    var dynamization = dynamizations.FirstOrDefault(d => d.PropertyName.Equals(property, StringComparison.OrdinalIgnoreCase))
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"'{itemName}' has no tag dynamization on '{property}'. It has: {string.Join(", ", dynamizations.Select(d => d.PropertyName))}.");
                    var result = new HmiEditResult();

                    if (hmi.Tags.Find(PlcRoot(tag)) == null)
                    {
                        result.Warnings.Add($"No HMI tag named '{PlcRoot(tag)}' exists.");
                    }

                    result.Changes.Add(new XmlChange { Object = screenName, Where = $"{itemName}, dynamization {dynamization.PropertyName}", Old = dynamization.Tag ?? string.Empty, New = tag });

                    if (!dryRun)
                    {
                        dynamization.Tag = tag;
                        result.Applied = true;
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName), ("property", property), ("dryRun", dryRun));
        }

        #endregion

        #region hmi compile and plc dependents

        /// <summary>
        /// Compiles the Unified device. The HMI software itself offers no services, so the
        /// compiler is looked for on the device item that holds it, then on its parents.
        /// </summary>
        public (CompilerResult Result, string CompiledObject) CompileHmi(string softwarePath)
        {
            return Operation.Run(_logger, nameof(CompileHmi), PortalErrorCode.InvalidState,
                () =>
                {
                    GetHmiSoftwareOrThrow(softwarePath);

                    for (IEngineeringObject? current = GetSoftwareContainer(softwarePath)?.Parent; current != null; current = current.Parent)
                    {
                        if (current is Project)
                        {
                            break;
                        }

                        var compiler = (current as IEngineeringServiceProvider)?.GetService<ICompilable>();

                        if (compiler != null)
                        {
                            return (compiler.Compile(), $"{current.GetType().Name} '{Text(current, "Name")}'");
                        }
                    }

                    throw new PortalException(PortalErrorCode.NotSupported,
                        $"Neither the device item of '{softwarePath}' nor its parents offer a compiler through Openness. Compile the HMI in TIA Portal.");
                },
                ("softwarePath", softwarePath));
        }

        /// <summary>
        /// The HMI tags whose PLC path starts at one of <paramref name="blocks"/> and passes a
        /// member that is being renamed, as ready-made "hmiTag=newPlcPath" assignments for
        /// SetHmiTagPlcTags. Seen live: after a member rename by import these tags are left with
        /// an empty PlcTag, so the paths have to be noted before the rename.
        /// </summary>
        public List<string> HmiDependents(string hmiSoftwarePath, ICollection<string> blocks, IDictionary<string, string> renames)
        {
            var leaves = renames.ToDictionary(r => r.Key.Split('.').Last().Trim().Trim('"'), r => r.Value, StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            foreach (var tag in GetHmiSoftwareOrThrow(hmiSoftwarePath).Tags.Where(t => !string.IsNullOrEmpty(t.PlcTag)))
            {
                if (!blocks.Contains(PlcRoot(tag.PlcTag)))
                {
                    continue;
                }

                var renamed = SimaticMl.RenamePlcPath(tag.PlcTag, leaves);

                if (renamed != tag.PlcTag)
                {
                    result.Add($"{tag.Name}={renamed}");
                }
            }

            return result;
        }

        #endregion
    }
}
