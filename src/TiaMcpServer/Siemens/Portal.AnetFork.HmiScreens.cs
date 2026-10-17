using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Script;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // ANet fork additions for WinCC Unified (branch 'anet'): screens and screen items are
        // created and deleted here. Openness V21 creates a screen with HmiScreenComposition.Create
        // and an item with HmiScreenItemBaseComposition.Create<T>; it offers no copy of a screen
        // and no screen template, so a new screen is empty.
        //
        // Callers: the tools in McpServer.AnetFork.HmiScreens.cs, registered only under
        // '--allow-write' and run inside one TIA Portal transaction each. Affected API: none
        // existing. No file I/O.
        //
        // A batch of items fails closed per item: an item whose name is taken, whose tag does not
        // exist or whose property, dynamization or event the type does not have is reported and
        // skipped; an item TIA Portal refuses while it is set up is deleted again. The checks run
        // in the dry run as well, because they need no object to exist.

        #region hmi screens

        /// <summary>The screens of a group path ('Line/Station'); empty names the root.</summary>
        private static HmiScreenComposition HmiScreensAt(HmiSoftware hmi, string groupPath)
        {
            var screens = hmi.Screens;
            var groups = hmi.ScreenGroups;

            foreach (var segment in (groupPath ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var group = groups.Find(segment)
                    ?? throw new PortalException(PortalErrorCode.NotFound, $"No screen group '{segment}' in '{groupPath}'.");

                screens = group.Screens;
                groups = group.Groups;
            }

            return screens;
        }

        private static HmiScreen FindUnifiedScreen(HmiSoftware hmi, string screenName)
        {
            return FindScreen(hmi, screenName) as HmiScreen
                ?? throw new PortalException(PortalErrorCode.NotSupported, $"'{screenName}' is not a WinCC Unified screen.");
        }

        /// <summary>Creates an empty screen at the root or in a screen group; width and height 0 keep the device default.</summary>
        public HmiEditResult CreateHmiScreen(string softwarePath, string screenName, string groupPath, uint width, uint height, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(CreateHmiScreen), PortalErrorCode.CreateFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var screens = HmiScreensAt(hmi, groupPath);

                    RequireHmiName(screenName);

                    if (HmiScreens(hmi).Any(s => Text(s, "Name").Equals(screenName, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"A screen named '{screenName}' already exists.");
                    }

                    var result = new HmiEditResult();
                    var size = width > 0 || height > 0 ? $"{width}x{height}" : "device default";

                    result.Changes.Add(new XmlChange { Object = screenName, Where = "Create", Old = string.Empty, New = $"{(string.IsNullOrEmpty(groupPath) ? "/" : groupPath)}, {size}" });

                    if (!dryRun)
                    {
                        var screen = screens.Create(screenName);

                        if (width > 0)
                        {
                            screen.Width = width;
                        }

                        if (height > 0)
                        {
                            screen.Height = height;
                        }

                        result.Changes[0].New = $"{(string.IsNullOrEmpty(groupPath) ? "/" : groupPath)}, {screen.Width}x{screen.Height}";
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

        private static readonly Dictionary<string, Type> _itemTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["Button"] = typeof(HmiButton),
            ["IOField"] = typeof(HmiIOField),
            ["Text"] = typeof(HmiText),
            ["Rectangle"] = typeof(HmiRectangle),
            ["Circle"] = typeof(HmiCircle)
        };

        private static HmiScreenItemBase CreateItem(HmiScreen screen, HmiItemSpec spec)
        {
            switch (spec.Type)
            {
                case "Button": return screen.ScreenItems.Create<HmiButton>(spec.Name);
                case "IOField": return screen.ScreenItems.Create<HmiIOField>(spec.Name);
                case "Text": return screen.ScreenItems.Create<HmiText>(spec.Name);
                case "Rectangle": return screen.ScreenItems.Create<HmiRectangle>(spec.Name);
                case "Circle": return screen.ScreenItems.Create<HmiCircle>(spec.Name);
                default: throw new PortalException(PortalErrorCode.InvalidParams, $"'{spec.Type}' is not an item type.");
            }
        }

        /// <summary>Geometry as the item's own properties hold it: left/top/width/height, or centre and radius for a circle.</summary>
        private static Dictionary<string, string> Geometry(HmiItemSpec spec)
        {
            if (spec.Type == "Circle")
            {
                var radius = spec.Width / 2;

                return new Dictionary<string, string>
                {
                    ["CenterX"] = (spec.Left + (int)radius).ToString(),
                    ["CenterY"] = (spec.Top + (int)radius).ToString(),
                    ["Radius"] = radius.ToString()
                };
            }

            return new Dictionary<string, string>
            {
                ["Left"] = spec.Left.ToString(),
                ["Top"] = spec.Top.ToString(),
                ["Width"] = spec.Width.ToString(),
                ["Height"] = spec.Height.ToString()
            };
        }

        private static void SetProperty(HmiScreenItemBase item, Type type, string name, string value)
        {
            var property = HmiScreenSpecs.Property(type, name)!;

            item.SetAttribute(property.Name, HmiScreenSpecs.Convert(property.PropertyType, value));
        }

        private static void AddEvent(HmiScreenItemBase item, Type type, string eventName, string script)
        {
            var handlersProperty = HmiScreenSpecs.Property(type, "EventHandlers")!;
            var handlers = handlersProperty.GetValue(item);
            var create = handlersProperty.PropertyType.GetMethod("Create")!;
            var eventType = create.GetParameters()[0].ParameterType;
            var handler = create.Invoke(handlers, new[] { Enum.Parse(eventType, eventName, true) })!;
            var code = handler.GetType().GetProperty("Script")?.GetValue(handler) as IHmiScript
                ?? throw new PortalException(PortalErrorCode.NotSupported, $"the handler of '{eventName}' offers no script");

            code.ScriptCode = script;
        }

        /// <summary>
        /// Creates screen items in a batch: type, name and geometry, then the text, the properties,
        /// the tag dynamizations and the event scripts of each. Every tag must exist as an HMI tag.
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

                        item.Details["Type"] = spec.Type;

                        foreach (var geometry in Geometry(spec))
                        {
                            item.Details[geometry.Key] = geometry.Value;
                        }

                        var missingTag = (spec.Dynamizations ?? new Dictionary<string, string>()).Values
                            .Select(HmiRoot)
                            .FirstOrDefault(t => hmi.Tags.Find(t) == null);
                        var problem = HmiScreenSpecs.Problem(spec, _itemTypes[spec.Type]);

                        if (existing.Contains(spec.Name))
                        {
                            Fail(item, $"an item named '{spec.Name}' is on '{screen.Name}' already");
                        }
                        else if (problem != null)
                        {
                            Fail(item, problem);
                        }
                        else if (missingTag != null)
                        {
                            Fail(item, $"no HMI tag named '{missingTag}' exists");
                        }
                    }

                    if (dryRun)
                    {
                        return Finish(result, true, "screen item");
                    }

                    foreach (var (spec, item) in specs.Zip(result.Items, (s, i) => (s, i)).Where(p => p.i.Ok))
                    {
                        HmiScreenItemBase? created = null;
                        var type = _itemTypes[spec.Type];

                        try
                        {
                            created = CreateItem(screen, spec);

                            foreach (var geometry in Geometry(spec).Concat(spec.PropertyValues))
                            {
                                SetProperty(created, type, geometry.Key, geometry.Value);
                            }

                            if (spec.Text != null)
                            {
                                SetText((MultilingualText)created.GetAttribute("Text"), culture, spec.Text, $"{spec.Name}.Text");
                            }

                            foreach (var dynamization in spec.Dynamizations ?? new Dictionary<string, string>())
                            {
                                created.Dynamizations.Create<TagDynamization>(HmiScreenSpecs.Property(type, dynamization.Key)!.Name).Tag = dynamization.Value;
                            }

                            foreach (var handler in spec.Events ?? new Dictionary<string, string>())
                            {
                                AddEvent(created, type, handler.Key, handler.Value);
                            }

                            foreach (var geometry in Geometry(spec).Keys)
                            {
                                item.Details[geometry] = created.GetAttribute(geometry)?.ToString() ?? string.Empty;
                            }

                            item.Details["Dynamizations"] = (spec.Dynamizations?.Count ?? 0).ToString();
                            item.Details["Events"] = (spec.Events?.Count ?? 0).ToString();
                        }
                        catch (Exception ex) when (ex is EngineeringException || ex is PortalException || ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is System.Reflection.TargetInvocationException)
                        {
                            Fail(item, (ex as System.Reflection.TargetInvocationException)?.InnerException?.Message ?? ex.Message);

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

        #endregion
    }
}
