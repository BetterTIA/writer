using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.Common;
using Siemens.Engineering.HmiUnified.UI;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // ANet fork additions for WinCC Unified (branch 'anet'): screens and screen items are
        // created and deleted here, items are dynamized, text lists go in and out as files and
        // runtime settings are set. Openness V21 creates a screen with HmiScreenComposition.Create
        // and an item with HmiScreenItemBaseComposition.Create<T>; it offers no copy of a screen
        // and no screen template, so a new screen is empty. Text lists have no Create either, only
        // Export and Import of a file.
        //
        // Callers: the tools in McpServer.AnetFork.HmiScreens.cs, registered only under
        // '--allow-write' and run inside one TIA Portal transaction each. Affected API: none
        // existing. File I/O: ExportHmiTextLists and ImportHmiTextLists, in the folder given.
        //
        // A batch of items fails closed per item: an item whose name is taken, whose tag does not
        // exist or whose property, dynamization or event the type does not have is reported and
        // skipped; an item TIA Portal refuses while it is set up is deleted again. The checks run
        // in the dry run as well, because they need no object to exist.

        #region hmi screens

        /// <summary>
        /// The screens of a group path ('Line/Station'); empty names the root. Groups that do not
        /// exist are added to <paramref name="missing"/> and created when <paramref name="create"/>
        /// is set; without it the result is null as soon as one is missing.
        /// </summary>
        private static HmiScreenComposition? HmiScreensAt(HmiSoftware hmi, string groupPath, bool create, List<string> missing)
        {
            var screens = hmi.Screens;
            var groups = hmi.ScreenGroups;
            var path = string.Empty;
            var found = true;

            foreach (var segment in (groupPath ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                path = path.Length == 0 ? segment : $"{path}/{segment}";

                var group = found ? groups.Find(segment) : null;

                if (group == null)
                {
                    RequireHmiName(segment);
                    missing.Add(path);
                    found = false;

                    if (!create)
                    {
                        continue;
                    }

                    group = groups.Create(segment);
                }

                screens = group.Screens;
                groups = group.Groups;
            }

            return found || create ? screens : null;
        }

        private static HmiScreen FindUnifiedScreen(HmiSoftware hmi, string screenName)
        {
            return FindScreen(hmi, screenName) as HmiScreen
                ?? throw new PortalException(PortalErrorCode.NotSupported, $"'{screenName}' is not a WinCC Unified screen.");
        }

        /// <summary>
        /// Creates an empty screen at the root or in a screen group, which is created when it does
        /// not exist; width and height 0 keep the device default. Events get their scripts.
        /// </summary>
        public HmiEditResult CreateHmiScreen(string softwarePath, string screenName, string groupPath, uint width, uint height, IDictionary<string, string>? events = null, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(CreateHmiScreen), PortalErrorCode.CreateFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var missing = new List<string>();

                    HmiScreensAt(hmi, groupPath, false, missing);
                    RequireHmiName(screenName);

                    if (HmiScreens(hmi).Any(s => Text(s, "Name").Equals(screenName, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"A screen named '{screenName}' already exists.");
                    }

                    var problem = HmiScreenSpecs.EventsProblem("screen", typeof(HmiScreen), events);

                    if (problem != null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"'{screenName}': {problem}.");
                    }

                    var result = new HmiEditResult();
                    var where = string.IsNullOrEmpty(groupPath) ? "/" : groupPath;
                    var size = width > 0 || height > 0 ? $"{width}x{height}" : "device default";

                    foreach (var group in missing)
                    {
                        result.Changes.Add(new XmlChange { Object = group, Where = "Create screen group", Old = string.Empty, New = group });
                    }

                    var change = new XmlChange { Object = screenName, Where = "Create", Old = string.Empty, New = $"{where}, {size}, {events?.Count ?? 0} event(s)" };
                    result.Changes.Add(change);

                    if (!dryRun)
                    {
                        var screen = HmiScreensAt(hmi, groupPath, true, new List<string>())!.Create(screenName);

                        if (width > 0)
                        {
                            screen.Width = width;
                        }

                        if (height > 0)
                        {
                            screen.Height = height;
                        }

                        foreach (var handler in events ?? new Dictionary<string, string>())
                        {
                            var errors = AddEvent(screen, typeof(HmiScreen), handler.Key, handler.Value);

                            if (errors != null)
                            {
                                result.Warnings.Add($"Syntax check of the {handler.Key} script: {errors}");
                            }
                        }

                        change.New = $"{where}, {screen.Width}x{screen.Height}, {events?.Count ?? 0} event(s)";
                        result.Applied = true;
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("groupPath", groupPath), ("dryRun", dryRun));
        }

        /// <summary>
        /// Deletes a screen with every item on it. The dry run lists the script lines of the other
        /// screens that hold the name as a string - they are left dangling by the delete.
        /// </summary>
        public HmiEditResult DeleteHmiScreen(string softwarePath, string screenName, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(DeleteHmiScreen), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var screen = FindUnifiedScreen(hmi, screenName);
                    var result = new HmiEditResult();
                    var items = screen.ScreenItems.Count;

                    result.Changes.Add(new XmlChange { Object = screen.Name, Where = "Delete", Old = $"{items} item(s)", New = string.Empty });
                    result.Usages.AddRange(ScriptUsages(AllSpots(hmi).Where(s => !s.Screen.Equals(screen.Name, StringComparison.OrdinalIgnoreCase)), screen.Name));

                    if (result.Usages.Count > 0)
                    {
                        result.Warnings.Add($"Script code of other screens names '{screen.Name}' as a string (see Usages); those lines are not changed.");
                    }

                    if (!dryRun)
                    {
                        screen.Delete();
                        result.Applied = true;
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("dryRun", dryRun));
        }

        #endregion

        #region hmi screen items

        /// <summary>
        /// Every item class of WinCC Unified by its name without the 'Hmi' prefix (Button, Label,
        /// ScreenWindow, AlarmControl...), read from the Openness assembly, so a new item class of
        /// a later version needs no change here; HmiFaceplateContainer is also known as Faceplate.
        /// </summary>
        private static readonly Lazy<Dictionary<string, Type>> _itemTypes = new Lazy<Dictionary<string, Type>>(() =>
        {
            Type[] types;

            try
            {
                types = typeof(HmiScreenItemBase).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            var map = types
                .Where(t => t.IsPublic && !t.IsAbstract && t.Name.StartsWith("Hmi") && !t.Name.EndsWith("Base") && typeof(HmiScreenItemBase).IsAssignableFrom(t))
                .ToDictionary(t => t.Name.Substring(3), StringComparer.OrdinalIgnoreCase);

            map[HmiScreenSpecs.Faceplate] = typeof(HmiFaceplateContainer);

            return map;
        });

        private static HmiScreenItemBase CreateItem(HmiScreen screen, HmiItemSpec spec, Type type)
        {
            var faceplate = spec.FaceplateType != null;
            var create = typeof(HmiScreenItemBaseComposition).GetMethods()
                .Single(m => m.Name == "Create" && m.GetParameters().Length == (faceplate ? 2 : 1))
                .MakeGenericMethod(type);

            return (HmiScreenItemBase)create.Invoke(screen.ScreenItems, faceplate ? new object[] { spec.Name, spec.FaceplateType! } : new object[] { spec.Name });
        }

        /// <summary>
        /// The object a dotted path ends in and the canonical name of its last segment, as
        /// HmiScreenSpecs.PathProperty walks it: 'Font.Size' is (the Font part, "Size"),
        /// 'Interface.Speed.Value' is (the interface entry Speed, "Value"). A path without a dot
        /// stays on <paramref name="item"/>.
        /// </summary>
        private static (IEngineeringObject Owner, string Name) ResolvePath(IEngineeringObject item, string path)
        {
            var segments = path.Split('.');
            object owner = item;

            for (var i = 0; i < segments.Length - 1; i++)
            {
                var property = HmiScreenSpecs.Property(owner.GetType(), segments[i])
                    ?? throw new PortalException(PortalErrorCode.InvalidParams, $"'{path}': {owner.GetType().Name} has no '{segments[i]}'.");
                var value = property.GetValue(owner)
                    ?? throw new PortalException(PortalErrorCode.NotFound, $"'{path}': '{segments[i]}' is empty.");
                var find = HmiScreenSpecs.Find(value.GetType());

                if (find != null && i + 1 < segments.Length - 1)
                {
                    i++;
                    value = find.Invoke(value, new object[] { segments[i] })
                        ?? throw new PortalException(PortalErrorCode.NotFound, $"'{path}': '{segments[i - 1]}' has no entry '{segments[i]}'.");
                }

                owner = value;
            }

            var name = HmiScreenSpecs.Property(owner.GetType(), segments[segments.Length - 1])?.Name ?? segments[segments.Length - 1];

            return (owner as IEngineeringObject
                ?? throw new PortalException(PortalErrorCode.NotSupported, $"'{path}' does not end in an object Openness can set."), name);
        }

        private static void SetProperty(IEngineeringObject item, string path, string value)
        {
            var (owner, name) = ResolvePath(item, path);

            owner.SetAttribute(name, HmiScreenSpecs.Convert(HmiScreenSpecs.Property(owner.GetType(), name)!.PropertyType, value));
        }

        /// <summary>The errors of a script syntax check as one line, or null when there are none.</summary>
        private static string? SyntaxErrors(HmiValidationResult? check)
        {
            var errors = check?.Errors?.ToList() ?? new List<string>();

            return errors.Count > 0 ? string.Join("; ", errors) : null;
        }

        /// <summary>Attaches an event script to an item or a screen; returns the errors of its syntax check, or null.</summary>
        private static string? AddEvent(object target, Type type, string eventName, string script)
        {
            var handlersProperty = HmiScreenSpecs.Property(type, "EventHandlers")!;
            var handlers = handlersProperty.GetValue(target);
            var create = handlersProperty.PropertyType.GetMethod("Create")!;
            var eventType = create.GetParameters()[0].ParameterType;
            var handler = create.Invoke(handlers, new[] { Enum.Parse(eventType, eventName, true) })!;
            var code = HmiScreenSpecs.Property(handler.GetType(), "Script")?.GetValue(handler) as IHmiScript
                ?? throw new PortalException(PortalErrorCode.NotSupported, $"the handler of '{eventName}' offers no script");

            code.ScriptCode = script;

            return SyntaxErrors(code.SyntaxCheck());
        }

        private static DynamizationBaseComposition Dynamizations(IEngineeringObject owner) =>
            HmiScreenSpecs.Property(owner.GetType(), "Dynamizations")?.GetValue(owner) as DynamizationBaseComposition
                ?? throw new PortalException(PortalErrorCode.NotSupported, $"{owner.GetType().Name} cannot be dynamized.");

        /// <summary>What an existing dynamization does, for the report: 'Tag X', 'Script', 'ResourceList X via L'.</summary>
        private static string Describe(DynamizationBase dynamization) =>
            dynamization is TagDynamization tag ? $"Tag {tag.Tag}"
            : dynamization is ResourceListDynamization list ? $"ResourceList {list.Tag} via {list.ResourceList}"
            : dynamization.DynamizationType.ToString();

        /// <summary>Why the dynamization cannot be made in this HMI: its tag does not exist or its trigger is unknown; null when it can.</summary>
        private static string? DynamizationProblem(HmiSoftware hmi, HmiDynamizationSpec spec)
        {
            if (spec.Tag != null && hmi.Tags.Find(HmiRoot(spec.Tag)) == null)
            {
                return $"no HMI tag named '{HmiRoot(spec.Tag)}' exists";
            }

            if (spec.Trigger != null && !Enum.GetNames(typeof(TriggerType)).Any(n => n.Equals(spec.Trigger, StringComparison.OrdinalIgnoreCase)))
            {
                return $"'{spec.Trigger}' is no trigger; use one of {string.Join(", ", Enum.GetNames(typeof(TriggerType)))}";
            }

            return null;
        }

        /// <summary>
        /// Sets the dynamization of one property (a dotted path reaches a part, e.g. Font.Size),
        /// replacing the one it had. Returns the errors of the script syntax check, or null.
        /// </summary>
        private static string? AddDynamization(IEngineeringObject item, string path, HmiDynamizationSpec spec)
        {
            var (owner, name) = ResolvePath(item, path);
            var dynamizations = Dynamizations(owner);

            dynamizations.FirstOrDefault(d => d.PropertyName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Delete();

            if (spec.Script != null)
            {
                var script = dynamizations.Create<ScriptDynamization>(name);

                script.ScriptCode = spec.Script;

                if (spec.Trigger != null)
                {
                    script.Trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), spec.Trigger, true);
                }

                return SyntaxErrors(script.SyntaxCheck());
            }

            if (spec.ResourceList != null)
            {
                var list = dynamizations.Create<ResourceListDynamization>(name);

                list.Tag = spec.Tag;
                list.ResourceList = spec.ResourceList;

                return null;
            }

            var tag = dynamizations.Create<TagDynamization>(name);

            tag.Tag = spec.Tag;

            if (spec.Indirect)
            {
                tag.UseIndirectAddressing = true;
            }

            return null;
        }

        /// <summary>
        /// Creates screen items in a batch: type, name and geometry, then the text, the properties,
        /// the dynamizations and the event scripts of each. Every tag must exist as an HMI tag.
        /// </summary>
        /// <param name="culture">Language of the texts; may stay empty when the project has one language.</param>
        public HmiWriteResult CreateHmiScreenItems(string softwarePath, string screenName, IList<HmiItemSpec> specs, string culture, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(CreateHmiScreenItems), PortalErrorCode.CreateFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var screen = FindUnifiedScreen(hmi, screenName);
                    var existing = new HashSet<string>(screen.ScreenItems.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
                    var result = new HmiWriteResult();

                    foreach (var spec in specs)
                    {
                        var item = new HmiWriteItem { Name = spec.Name };
                        result.Items.Add(item);

                        if (!_itemTypes.Value.TryGetValue(spec.Type, out var type))
                        {
                            item.Details["Type"] = spec.Type;
                            Fail(item, $"'{spec.Type}' is not an item type; use one of {string.Join(", ", _itemTypes.Value.Keys.OrderBy(k => k))}");
                            continue;
                        }

                        item.Details["Type"] = spec.Type = spec.Type.Equals(HmiScreenSpecs.Faceplate, StringComparison.OrdinalIgnoreCase) ? HmiScreenSpecs.Faceplate : type.Name.Substring(3);

                        foreach (var geometry in HmiScreenSpecs.Geometry(spec, type))
                        {
                            item.Details[geometry.Key] = geometry.Value;
                        }

                        var problem = HmiScreenSpecs.Problem(spec, type)
                            ?? spec.DynamizationSpecs.Values.Select(d => DynamizationProblem(hmi, d)).FirstOrDefault(p => p != null);

                        if (existing.Contains(spec.Name))
                        {
                            Fail(item, $"an item named '{spec.Name}' is on '{screen.Name}' already");
                        }
                        else if (problem != null)
                        {
                            Fail(item, problem);
                        }
                    }

                    if (dryRun)
                    {
                        return Finish(result, true, "screen item");
                    }

                    foreach (var (spec, item) in specs.Zip(result.Items, (s, i) => (s, i)).Where(p => p.i.Ok))
                    {
                        HmiScreenItemBase? created = null;
                        var type = _itemTypes.Value[spec.Type];
                        var scriptErrors = new List<string>();

                        try
                        {
                            created = CreateItem(screen, spec, type);

                            foreach (var property in HmiScreenSpecs.Geometry(spec, type).Concat(spec.PropertyValues))
                            {
                                SetProperty(created, property.Key, property.Value);
                            }

                            if (spec.Text != null)
                            {
                                var text = created.GetAttribute("Text") as MultilingualText
                                    ?? throw new PortalException(PortalErrorCode.NotSupported, $"the Text of a {spec.Type} is no multilingual text");

                                SetText(text, culture, spec.Text, $"{spec.Name}.Text");
                            }

                            foreach (var dynamization in spec.DynamizationSpecs)
                            {
                                var errors = AddDynamization(created, dynamization.Key, dynamization.Value);

                                if (errors != null)
                                {
                                    scriptErrors.Add($"{dynamization.Key}: {errors}");
                                }
                            }

                            foreach (var handler in spec.Events ?? new Dictionary<string, string>())
                            {
                                var errors = AddEvent(created, type, handler.Key, handler.Value);

                                if (errors != null)
                                {
                                    scriptErrors.Add($"{handler.Key}: {errors}");
                                }
                            }

                            foreach (var geometry in HmiScreenSpecs.Geometry(spec, type).Keys)
                            {
                                item.Details[geometry] = created.GetAttribute(geometry)?.ToString() ?? string.Empty;
                            }

                            item.Details["Dynamizations"] = spec.DynamizationSpecs.Count.ToString();
                            item.Details["Events"] = (spec.Events?.Count ?? 0).ToString();

                            // The script is kept: the check may not know tags or functions the runtime has.
                            if (scriptErrors.Count > 0)
                            {
                                item.Details["ScriptCheck"] = string.Join(" | ", scriptErrors);
                            }
                        }
                        catch (Exception ex) when (ex is EngineeringException || ex is PortalException || ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is InvalidCastException || ex is TargetInvocationException)
                        {
                            Fail(item, (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message);

                            // Fail closed: a half-made item is removed again.
                            if (created != null)
                            {
                                try
                                {
                                    created.Delete();
                                    item.Message += " - the item was removed again";
                                }
                                catch (EngineeringException cleanup)
                                {
                                    item.Message += $" - the item could NOT be removed ({cleanup.Message}); delete it by hand";
                                }
                            }
                        }
                    }

                    return Finish(result, false, "screen item");
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("count", specs.Count), ("dryRun", dryRun));
        }

        /// <summary>Deletes items of one screen by name; the dry run reports each with its type, dynamizations and scripts.</summary>
        public HmiWriteResult DeleteHmiScreenItems(string softwarePath, string screenName, IList<string> names, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(DeleteHmiScreenItems), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var screen = FindUnifiedScreen(hmi, screenName);
                    var result = new HmiWriteResult();

                    if (names.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No item name was given.");
                    }

                    foreach (var name in names)
                    {
                        var item = new HmiWriteItem { Name = name };
                        result.Items.Add(item);

                        var found = screen.ScreenItems.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                        if (found == null)
                        {
                            Fail(item, $"no item named '{name}' is on '{screen.Name}'");
                            continue;
                        }

                        var spots = Spots(screen.Name, found.Name, found).ToList();

                        item.Name = found.Name;
                        item.Details["Type"] = found.GetType().Name;
                        item.Details["Dynamizations"] = spots.Count(s => s.Tag != null).ToString();
                        item.Details["Scripts"] = spots.Count(s => s.Script != null).ToString();

                        if (!dryRun)
                        {
                            try
                            {
                                found.Delete();
                            }
                            catch (EngineeringException ex)
                            {
                                Fail(item, ex.Message);
                            }
                        }
                    }

                    return Finish(result, dryRun, "screen item");
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("count", names.Count), ("dryRun", dryRun));
        }

        /// <summary>
        /// Sets the dynamization of one property of a screen item (or of the screen, with an empty
        /// item name): a tag, a tag with a resource list or a script. A dotted path reaches a part,
        /// e.g. Font.Size. The dynamization the property had is replaced.
        /// </summary>
        public HmiEditResult SetHmiItemDynamization(string softwarePath, string screenName, string itemName, string property, HmiDynamizationSpec spec, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(SetHmiItemDynamization), PortalErrorCode.CreateFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var item = FindItem(hmi, screenName, itemName);
                    var problem = HmiScreenSpecs.PathProperty(item.GetType(), property) == null
                        ? $"{item.GetType().Name} has no property '{property}' to dynamize"
                        : DynamizationProblem(hmi, spec);

                    if (problem != null)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"'{(string.IsNullOrEmpty(itemName) ? screenName : itemName)}': {problem}.");
                    }

                    var (owner, name) = ResolvePath(item, property);
                    var old = Dynamizations(owner).FirstOrDefault(d => d.PropertyName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    var result = new HmiEditResult();

                    result.Changes.Add(new XmlChange
                    {
                        Object = screenName,
                        Where = $"{(string.IsNullOrEmpty(itemName) ? "screen" : itemName)}, dynamization {property}",
                        Old = old == null ? string.Empty : Describe(old),
                        New = spec.ToString()
                    });

                    if (!dryRun)
                    {
                        var errors = AddDynamization(item, property, spec);

                        if (errors != null)
                        {
                            result.Warnings.Add($"Syntax check of the script: {errors}");
                        }

                        result.Applied = true;
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("screenName", screenName), ("itemName", itemName), ("property", property), ("dryRun", dryRun));
        }

        #endregion

        #region hmi text lists and runtime settings

        /// <summary>A folder named in full (drive or UNC root), not 'x', a rooted 'x' or 'C:x', which would land next to the server's own folder.</summary>
        public static void RequireAbsoluteFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder) || (Path.GetPathRoot(folder) ?? string.Empty).Length < 3)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"folder '{folder}' is not an absolute path. Give the full path, e.g. 'C:\\Temp\\TextLists'.");
            }
        }

        /// <summary>
        /// Exports the text lists of the HMI to a file in <paramref name="folder"/>; returns the files written.
        /// The dry run creates nothing and lists the files of that name that already exist and would be overwritten.
        /// </summary>
        public HmiEditResult ExportHmiTextLists(string softwarePath, string folder, string fileName, bool dryRun = false)
        {
            return Operation.Run(_logger, nameof(ExportHmiTextLists), PortalErrorCode.ExportFailed,
                () =>
                {
                    RequireAbsoluteFolder(folder);

                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var result = new HmiEditResult();

                    if (dryRun)
                    {
                        var existing = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, fileName + "*").ToList() : new List<string>();

                        result.Changes.Add(new XmlChange
                        {
                            Object = Path.Combine(folder, fileName),
                            Where = "Export",
                            Old = existing.Count > 0 ? $"exists, would be overwritten: {string.Join(", ", existing.Select(Path.GetFileName))}" : "does not exist",
                            New = $"{hmi.HmiTextLists.Count} text list(s)"
                        });

                        return result;
                    }

                    var directory = Directory.CreateDirectory(folder);

                    foreach (var file in hmi.HmiTextLists.Export(directory, fileName))
                    {
                        result.Changes.Add(new XmlChange { Object = file.FullName, Where = "Export", Old = string.Empty, New = $"{hmi.HmiTextLists.Count} text list(s)" });
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("folder", folder), ("fileName", fileName));
        }

        /// <summary>
        /// Imports text lists from a file of ExportHmiTextLists (edited or from another project).
        /// The dry run checks the file and lists the text lists the HMI has now.
        /// </summary>
        public HmiEditResult ImportHmiTextLists(string softwarePath, string folder, string fileName, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(ImportHmiTextLists), PortalErrorCode.ImportFailed,
                () =>
                {
                    RequireAbsoluteFolder(folder);

                    var hmi = GetHmiSoftwareOrThrow(softwarePath);

                    if (!Directory.Exists(folder) || !Directory.EnumerateFiles(folder, fileName + "*").Any())
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"No file '{fileName}' in '{folder}'. Export one with ExportHmiTextLists to see the format.");
                    }

                    var before = hmi.HmiTextLists.Select(l => l.Name).ToList();
                    var result = new HmiEditResult();

                    result.Changes.Add(new XmlChange { Object = Path.Combine(folder, fileName), Where = "Import", Old = $"{before.Count} text list(s) now: {(before.Count > 0 ? string.Join(", ", before) : "none")}", New = dryRun ? "the import replaces text lists of the same name (which ones the file holds is not read here); lists of other names stay" : string.Empty });

                    if (!dryRun)
                    {
                        if (!hmi.HmiTextLists.Import(new DirectoryInfo(folder), fileName))
                        {
                            throw new PortalException(PortalErrorCode.ImportFailed, $"TIA Portal did not import '{fileName}' from '{folder}'.");
                        }

                        var added = hmi.HmiTextLists.Select(l => l.Name).Except(before).ToList();

                        result.Changes[0].New = $"{hmi.HmiTextLists.Count} text list(s); new: {(added.Count > 0 ? string.Join(", ", added) : "none")}";
                        result.Applied = true;
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("folder", folder), ("fileName", fileName), ("dryRun", dryRun));
        }

        /// <summary>Sets one runtime setting of the HMI, e.g. StartScreen; a start screen must exist.</summary>
        public HmiEditResult SetHmiRuntimeSetting(string softwarePath, string attribute, string value, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(SetHmiRuntimeSetting), PortalErrorCode.RenameFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var settings = hmi.RuntimeSettings;
                    var property = HmiScreenSpecs.Property(settings.GetType(), attribute);

                    if (property == null || !property.CanWrite)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'{attribute}' is no writable runtime setting; settable: {string.Join(", ", settings.GetType().GetProperties().Where(p => p.CanWrite).Select(p => p.Name))}.");
                    }

                    if (property.Name == "StartScreen")
                    {
                        value = FindUnifiedScreen(hmi, value).Name;
                    }

                    var result = new HmiEditResult();

                    result.Changes.Add(new XmlChange { Object = "RuntimeSettings", Where = property.Name, Old = property.GetValue(settings)?.ToString() ?? string.Empty, New = value });

                    if (!dryRun)
                    {
                        property.SetValue(settings, HmiScreenSpecs.Convert(property.PropertyType, value));
                        result.Applied = true;
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("attribute", attribute), ("dryRun", dryRun));
        }

        #endregion
    }
}
