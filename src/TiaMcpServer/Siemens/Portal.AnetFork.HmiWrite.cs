using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;
using Siemens.Engineering.HmiUnified.HmiTags;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>One item of an HMI batch write, with how it went.</summary>
    public sealed class HmiWriteItem
    {
        public string Name { get; set; } = string.Empty;

        public bool Ok { get; set; } = true;

        /// <summary>Why the item failed, or what it needs; empty when it went through.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>What was set and what TIA Portal reads back, e.g. PlcTag, DataType, TriggerBitAddress.</summary>
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>();
    }

    public sealed class HmiWriteResult
    {
        public bool Applied { get; set; }

        public List<HmiWriteItem> Items { get; set; } = new List<HmiWriteItem>();

        public List<string> Warnings { get; set; } = new List<string>();
    }

    public partial class Portal
    {
        // ANet fork additions for WinCC Unified (branch 'anet'): tag tables, tags and alarms are
        // created, changed and deleted here; the rename, binding and dynamization tools are in
        // Portal.AnetFork.Hmi.cs. Modelled on the hmi_write of Czarnak/tia-portal-mcp (MIT).
        //
        // Callers: the tools in McpServer.AnetFork.HmiWrite.cs, registered only under
        // '--allow-write' and run inside one TIA Portal transaction each. Affected API: none
        // existing. No file I/O.
        //
        // Batches fail closed per item: an item whose table, connection, PLC source or trigger
        // tag does not exist is reported and skipped, the others go through, and a tag whose
        // PLC path TIA Portal does not accept is deleted again. When no item can go through the
        // call fails as a whole.

        #region hmi write plumbing

        private static IEnumerable<HmiTagTable> AllHmiTagTables(HmiTagTableComposition tables, HmiTagTableGroupComposition groups)
        {
            foreach (var table in tables)
            {
                yield return table;
            }

            foreach (var group in groups)
            {
                foreach (var table in AllHmiTagTables(group.TagTables, group.Groups))
                {
                    yield return table;
                }
            }
        }

        /// <summary>Exactly one tag table by name, in any group.</summary>
        private static HmiTagTable FindHmiTagTable(HmiSoftware hmi, string tableName)
        {
            var matches = AllHmiTagTables(hmi.TagTables, hmi.TagTableGroups)
                .Where(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            throw new PortalException(matches.Count == 0 ? PortalErrorCode.NotFound : PortalErrorCode.InvalidParams,
                matches.Count == 0
                    ? $"No HMI tag table named '{tableName}'. Tables: {string.Join(", ", AllHmiTagTables(hmi.TagTables, hmi.TagTableGroups).Select(t => t.Name))}."
                    : $"{matches.Count} HMI tag tables are named '{tableName}'; the name must select exactly one.");
        }

        /// <summary>The tag tables of a group path ('Line/Station'); empty names the root.</summary>
        private static HmiTagTableComposition HmiTagTablesAt(HmiSoftware hmi, string groupPath)
        {
            var tables = hmi.TagTables;
            var groups = hmi.TagTableGroups;

            foreach (var segment in (groupPath ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var group = groups.Find(segment)
                    ?? throw new PortalException(PortalErrorCode.NotFound, $"No HMI tag table group '{segment}' in '{groupPath}'.");

                tables = group.TagTables;
                groups = group.Groups;
            }

            return tables;
        }

        private static string RequireHmiName(string name)
        {
            var problem = HmiSpecs.NameProblem(name);

            if (problem != null)
            {
                throw new PortalException(PortalErrorCode.InvalidParams, $"'{name}' is not a valid HMI name: {problem}.");
            }

            return name;
        }

        /// <summary>Writes one text of a multilingual text; without a culture the text must exist in exactly one language.</summary>
        private static XmlChange SetText(MultilingualText text, string culture, string value, string what)
        {
            var items = text.Items.Where(i => string.IsNullOrEmpty(culture) || i.Language.Culture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase)).ToList();

            if (items.Count != 1)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"{what} exists in {items.Count} language(s) ({string.Join(", ", text.Items.Select(i => i.Language.Culture.Name))}); pass one of them as 'culture'.");
            }

            var change = new XmlChange { Where = $"{what} [{items[0].Language.Culture.Name}]", Old = items[0].Text ?? string.Empty, New = value };

            items[0].Text = value;

            return change;
        }

        /// <summary>The tag a trigger or PLC path starts at: everything before the first '.'.</summary>
        private static string HmiRoot(string path) => path.Split('.')[0].Trim();

        private static string? ConnectionOrDefault(HmiSoftware hmi, string connection)
        {
            var names = hmi.Connections.Select(c => c.Name).ToList();

            if (!string.IsNullOrWhiteSpace(connection))
            {
                return names.FirstOrDefault(n => n.Equals(connection.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new PortalException(PortalErrorCode.NotFound, $"No HMI connection named '{connection}'. Connections: {string.Join(", ", names)}.");
            }

            return names.Count == 1 ? names[0] : null;
        }

        private static void Fail(HmiWriteItem item, string message)
        {
            item.Ok = false;
            item.Message = message;
        }

        /// <summary>A batch where nothing could be done is a failed call, not a success with fine print.</summary>
        private static HmiWriteResult Finish(HmiWriteResult result, bool dryRun, string what)
        {
            if (result.Items.Count > 0 && result.Items.All(i => !i.Ok))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"No {what} can be written: " + string.Join(" | ", result.Items.Select(i => $"{i.Name}: {i.Message}")));
            }

            result.Applied = !dryRun;

            return result;
        }

        #endregion

        #region hmi tag tables

        public HmiEditResult CreateHmiTagTable(string softwarePath, string tableName, string groupPath, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(CreateHmiTagTable), PortalErrorCode.CreateFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var tables = HmiTagTablesAt(hmi, groupPath);

                    RequireHmiName(tableName);

                    if (AllHmiTagTables(hmi.TagTables, hmi.TagTableGroups).Any(t => t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"An HMI tag table named '{tableName}' already exists.");
                    }

                    var result = new HmiEditResult();
                    result.Changes.Add(new XmlChange { Object = tableName, Where = "Create", Old = string.Empty, New = string.IsNullOrEmpty(groupPath) ? "/" : groupPath });

                    if (!dryRun)
                    {
                        tables.Create(tableName);
                        result.Applied = true;
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("tableName", tableName), ("dryRun", dryRun));
        }

        public HmiEditResult RenameHmiTagTable(string softwarePath, string tableName, string newName, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(RenameHmiTagTable), PortalErrorCode.RenameFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var table = FindHmiTagTable(hmi, tableName);

                    RequireHmiName(newName);

                    if (!table.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)
                        && AllHmiTagTables(hmi.TagTables, hmi.TagTableGroups).Any(t => t.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"An HMI tag table named '{newName}' already exists.");
                    }

                    var result = new HmiEditResult();
                    result.Changes.Add(new XmlChange { Object = table.Name, Where = "Name", Old = table.Name, New = newName });

                    if (!dryRun)
                    {
                        table.Name = newName;
                        result.Applied = true;
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("tableName", tableName), ("newName", newName), ("dryRun", dryRun));
        }

        /// <summary>Deletes a tag table with every tag in it; the default table is refused by TIA Portal.</summary>
        public HmiEditResult DeleteHmiTagTable(string softwarePath, string tableName, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(DeleteHmiTagTable), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var table = FindHmiTagTable(hmi, tableName);
                    var tags = table.Tags.Select(t => t.Name).ToList();
                    var result = new HmiEditResult();

                    result.Changes.Add(new XmlChange { Object = table.Name, Where = "Delete", Old = $"{tags.Count} tag(s)", New = string.Empty });

                    if (tags.Count > 0)
                    {
                        result.Warnings.Add($"Deleting '{table.Name}' deletes its {tags.Count} tag(s) as well: {string.Join(", ", tags.Take(20))}{(tags.Count > 20 ? ", ..." : string.Empty)}.");
                    }

                    if (!dryRun)
                    {
                        table.Delete();
                        result.Applied = true;
                    }

                    return result;
                },
                ("softwarePath", softwarePath), ("tableName", tableName), ("dryRun", dryRun));
        }

        #endregion

        #region hmi tags

        /// <param name="connection">Empty takes the only connection of the device; with several it has to be named.</param>
        /// <param name="plcSoftwarePath">Required when a bound tag is in the batch: every PLC path must start at a tag or block of that PLC.</param>
        public HmiWriteResult CreateHmiTags(string softwarePath, string tableName, IList<HmiTagSpec> specs, string connection, string plcSoftwarePath, string acquisitionCycle, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(CreateHmiTags), PortalErrorCode.CreateFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var table = FindHmiTagTable(hmi, tableName);
                    var result = new HmiWriteResult();
                    var bound = specs.Any(s => s.Bound);
                    string? connectionName = null;
                    HashSet<string>? roots = null;

                    if (bound)
                    {
                        connectionName = ConnectionOrDefault(hmi, connection)
                            ?? throw new PortalException(PortalErrorCode.InvalidParams,
                                $"The device has {hmi.Connections.Count} connections ({string.Join(", ", hmi.Connections.Select(c => c.Name))}); name one as 'connection'.");

                        if (string.IsNullOrWhiteSpace(plcSoftwarePath))
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams,
                                "Tags bound to the PLC need plcSoftwarePath, so that every PLC path can be checked to start at an existing tag or block.");
                        }

                        roots = new HashSet<string>(
                            GetTags(plcSoftwarePath).Select(t => t.Name).Concat(GetBlocks(plcSoftwarePath).Select(b => b.Name)),
                            StringComparer.OrdinalIgnoreCase);
                        result.Warnings.Add("PLC paths are checked at their first segment only; members behind it are proved by the PlcTag TIA Portal reads back.");
                    }

                    foreach (var spec in specs)
                    {
                        var item = new HmiWriteItem { Name = spec.Name };
                        result.Items.Add(item);

                        item.Details["Table"] = table.Name;

                        if (spec.Bound)
                        {
                            item.Details["Connection"] = connectionName!;
                            item.Details["PlcTag"] = spec.PlcTag!;
                        }
                        else
                        {
                            item.Details["DataType"] = spec.DataType!;
                        }

                        if (hmi.Tags.Find(spec.Name) != null)
                        {
                            Fail(item, "an HMI tag of this name exists already");
                        }
                        else if (spec.Bound && !roots!.Contains(PlcRoot(spec.PlcTag!)))
                        {
                            Fail(item, $"'{spec.PlcTag}' does not start at a tag or block of '{plcSoftwarePath}'");
                        }
                    }

                    if (dryRun)
                    {
                        return Finish(result, true, "tag");
                    }

                    foreach (var (spec, item) in specs.Zip(result.Items, (s, i) => (s, i)).Where(p => p.i.Ok))
                    {
                        HmiTag? tag = null;

                        try
                        {
                            tag = hmi.Tags.Create(spec.Name, table.Name);

                            if (spec.Bound)
                            {
                                tag.Connection = connectionName;
                                tag.PlcTag = spec.PlcTag;

                                if (string.IsNullOrEmpty(tag.PlcTag))
                                {
                                    throw new PortalException(PortalErrorCode.InvalidParams, $"TIA Portal did not accept the PLC path '{spec.PlcTag}' (PlcTag reads back empty)");
                                }
                            }
                            else
                            {
                                tag.DataType = spec.DataType;
                            }

                            if (!string.IsNullOrWhiteSpace(acquisitionCycle))
                            {
                                tag.AcquisitionCycle = acquisitionCycle;
                            }

                            item.Details["PlcTag"] = tag.PlcTag ?? string.Empty;
                            item.Details["DataType"] = tag.DataType ?? string.Empty;
                            item.Details["Address"] = tag.Address ?? string.Empty;
                            item.Details["AcquisitionCycle"] = tag.AcquisitionCycle ?? string.Empty;
                        }
                        catch (Exception ex) when (ex is EngineeringException || ex is PortalException)
                        {
                            Fail(item, ex.Message);

                            // Fail closed: a half-made tag is removed again.
                            if (tag != null)
                            {
                                try
                                {
                                    tag.Delete();
                                    item.Message += " - the tag was removed again";
                                }
                                catch (EngineeringException cleanup)
                                {
                                    item.Message += $" - the tag could NOT be removed ({cleanup.Message}); delete it by hand";
                                }
                            }
                        }
                    }

                    return Finish(result, false, "tag");
                },
                ("softwarePath", softwarePath), ("tableName", tableName), ("count", specs.Count), ("dryRun", dryRun));
        }

        /// <summary>Changes the connection, PLC path, data type (internal tags) or acquisition cycle of one tag; empty keeps a value.</summary>
        public HmiEditResult UpdateHmiTag(string softwarePath, string tagName, string connection, string plcTag, string dataType, string acquisitionCycle, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(UpdateHmiTag), PortalErrorCode.RenameFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var tag = hmi.Tags.Find(tagName)
                        ?? throw new PortalException(PortalErrorCode.NotFound, $"No HMI tag named '{tagName}'. Use 'GetHmiTags' to list the available tags.");
                    var result = new HmiEditResult();
                    var sets = new List<(string Where, string Old, string New, Action Apply)>();

                    if (!string.IsNullOrWhiteSpace(connection))
                    {
                        var name = ConnectionOrDefault(hmi, connection)!;
                        sets.Add(("Connection", tag.Connection ?? string.Empty, name, () => tag.Connection = name));
                    }

                    if (!string.IsNullOrWhiteSpace(plcTag))
                    {
                        sets.Add(("PlcTag", tag.PlcTag ?? string.Empty, plcTag, () => tag.PlcTag = plcTag));
                    }

                    if (!string.IsNullOrWhiteSpace(dataType))
                    {
                        sets.Add(("DataType", tag.DataType ?? string.Empty, dataType, () => tag.DataType = dataType));
                    }

                    if (!string.IsNullOrWhiteSpace(acquisitionCycle))
                    {
                        sets.Add(("AcquisitionCycle", tag.AcquisitionCycle ?? string.Empty, acquisitionCycle, () => tag.AcquisitionCycle = acquisitionCycle));
                    }

                    if (sets.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No change was requested: give connection, plcTag, dataType or acquisitionCycle.");
                    }

                    foreach (var (where, oldValue, newValue, _) in sets)
                    {
                        result.Changes.Add(new XmlChange { Object = tag.Name, Where = where, Old = oldValue, New = newValue });
                    }

                    if (dryRun)
                    {
                        return result;
                    }

                    foreach (var (_, _, _, apply) in sets)
                    {
                        apply();
                    }

                    if (!string.IsNullOrWhiteSpace(plcTag) && string.IsNullOrEmpty(tag.PlcTag))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, $"TIA Portal did not accept the PLC path '{plcTag}' (PlcTag reads back empty); the transaction rolls the change back.");
                    }

                    result.Applied = true;

                    return result;
                },
                ("softwarePath", softwarePath), ("tagName", tagName), ("dryRun", dryRun));
        }

        /// <summary>Deletes tags by name; the dry run lists where they are used (dynamizations, scripts, alarms).</summary>
        public HmiWriteResult DeleteHmiTags(string softwarePath, IList<string> names, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(DeleteHmiTags), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var result = new HmiWriteResult();
                    var tags = new List<(HmiTag Tag, HmiWriteItem Item)>();
                    var spots = AllSpots(hmi).ToList();
                    var alarms = hmi.DiscreteAlarms.Select(a => (a.Name, Trigger: a.RaisedStateTag ?? string.Empty, Kind: "discrete"))
                        .Concat(hmi.AnalogAlarms.Select(a => (a.Name, Trigger: a.RaisedStateTag ?? string.Empty, Kind: "analog")))
                        .ToList();

                    foreach (var name in names)
                    {
                        var item = new HmiWriteItem { Name = name };
                        result.Items.Add(item);

                        var tag = hmi.Tags.Find(name);

                        if (tag == null)
                        {
                            Fail(item, "no HMI tag of this name");
                            continue;
                        }

                        var dynamizations = spots.Where(s => s.Tag != null && (s.Tag.Tag ?? string.Empty).Equals(name, StringComparison.OrdinalIgnoreCase)
                            || s.Tag != null && (s.Tag.Tag ?? string.Empty).StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)).ToList();
                        var scripts = ScriptUsages(spots, name).ToList();
                        var users = alarms.Where(a => HmiRoot(a.Trigger).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

                        if (dynamizations.Count > 0)
                        {
                            item.Details["Dynamizations"] = string.Join(", ", dynamizations.Select(d => $"{d.Screen}/{d.Item} {d.Where}"));
                        }

                        if (scripts.Count > 0)
                        {
                            item.Details["Scripts"] = string.Join(" | ", scripts.Select(s => $"{s.Screen}/{s.Item}: {s.Text}"));
                        }

                        if (users.Count > 0)
                        {
                            item.Details["Alarms"] = string.Join(", ", users.Select(a => $"{a.Name} ({a.Kind})"));
                        }

                        if (dynamizations.Count + scripts.Count + users.Count > 0)
                        {
                            result.Warnings.Add($"'{name}' is used by {dynamizations.Count} dynamization(s), {scripts.Count} script line(s) and {users.Count} alarm(s) (see Details); they are left dangling.");
                        }

                        tags.Add((tag, item));
                    }

                    if (dryRun)
                    {
                        return Finish(result, true, "tag");
                    }

                    foreach (var (tag, item) in tags)
                    {
                        try
                        {
                            tag.Delete();

                            if (hmi.Tags.Find(item.Name) != null)
                            {
                                Fail(item, "the tag still exists after the delete");
                            }
                        }
                        catch (EngineeringException ex)
                        {
                            Fail(item, ex.Message);
                        }
                    }

                    return Finish(result, false, "tag");
                },
                ("softwarePath", softwarePath), ("count", names.Count), ("dryRun", dryRun));
        }

        #endregion

        #region hmi alarms

        private static AlarmBase? FindHmiAlarm(HmiSoftware hmi, bool discrete, string name) =>
            discrete ? (AlarmBase?)hmi.DiscreteAlarms.Find(name) : hmi.AnalogAlarms.Find(name);

        private static void DeleteHmiAlarm(AlarmBase alarm)
        {
            if (alarm is HmiDiscreteAlarm discrete)
            {
                discrete.Delete();
            }
            else
            {
                ((HmiAnalogAlarm)alarm).Delete();
            }
        }

        private static string? AlarmClassOrDefault(HmiSoftware hmi, string alarmClass)
        {
            if (string.IsNullOrWhiteSpace(alarmClass))
            {
                return null;
            }

            return hmi.AlarmClasses.Select(c => c.Name).FirstOrDefault(n => n.Equals(alarmClass.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new PortalException(PortalErrorCode.NotFound,
                    $"No alarm class named '{alarmClass}'. Classes: {string.Join(", ", hmi.AlarmClasses.Select(c => c.Name))}.");
        }

        /// <summary>Reads what TIA Portal made of the alarm, for the item details.</summary>
        private static void ReadAlarm(AlarmBase alarm, HmiWriteItem item)
        {
            item.Details["RaisedStateTag"] = alarm.RaisedStateTag ?? string.Empty;
            item.Details["AlarmClass"] = alarm.AlarmClass ?? string.Empty;
            item.Details["Priority"] = alarm.Priority.ToString(CultureInfo.InvariantCulture);

            if (alarm is HmiDiscreteAlarm discrete)
            {
                item.Details["TriggerBitAddress"] = discrete.TriggerBitAddress ?? string.Empty;
                item.Details["BitNumber"] = discrete.RaisedStateTagBitNumber.ToString(CultureInfo.InvariantCulture);
            }
            else if (alarm is HmiAnalogAlarm analog)
            {
                item.Details["TriggerAddress"] = analog.TriggerAddress ?? string.Empty;
                item.Details["Condition"] = analog.Condition.ToString();
                item.Details["ConditionValue"] = Convert.ToString(analog.ConditionValue, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        /// <param name="alarmClass">Empty keeps TIA Portal's default class.</param>
        /// <param name="priority">-1 keeps TIA Portal's default.</param>
        public HmiWriteResult CreateHmiAlarms(string softwarePath, bool discrete, IList<HmiAlarmSpec> specs, string alarmClass, int priority, string culture, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(CreateHmiAlarms), PortalErrorCode.CreateFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var result = new HmiWriteResult();
                    var className = AlarmClassOrDefault(hmi, alarmClass);
                    var kind = discrete ? "discrete" : "analog";

                    if (priority < -1 || priority > 255)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "priority must be 0 to 255, or -1 for the default.");
                    }

                    foreach (var spec in specs)
                    {
                        var item = new HmiWriteItem { Name = spec.Name };
                        result.Items.Add(item);

                        item.Details["RaisedStateTag"] = spec.TriggerTag;

                        if (discrete)
                        {
                            item.Details["BitNumber"] = spec.BitNumber.ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            item.Details["Condition"] = spec.Condition!;
                            item.Details["ConditionValue"] = Convert.ToString(spec.ConditionValue, CultureInfo.InvariantCulture) ?? string.Empty;
                        }

                        if (FindHmiAlarm(hmi, discrete, spec.Name) != null)
                        {
                            Fail(item, $"a {kind} alarm of this name exists already");
                        }
                        else if (hmi.Tags.Find(HmiRoot(spec.TriggerTag)) == null)
                        {
                            Fail(item, $"no HMI tag named '{HmiRoot(spec.TriggerTag)}' for the trigger '{spec.TriggerTag}'");
                        }
                    }

                    if (dryRun)
                    {
                        return Finish(result, true, "alarm");
                    }

                    foreach (var (spec, item) in specs.Zip(result.Items, (s, i) => (s, i)).Where(p => p.i.Ok))
                    {
                        AlarmBase? alarm = null;

                        try
                        {
                            alarm = discrete ? (AlarmBase)hmi.DiscreteAlarms.Create(spec.Name) : hmi.AnalogAlarms.Create(spec.Name);
                            alarm.RaisedStateTag = spec.TriggerTag;

                            if (alarm is HmiDiscreteAlarm discreteAlarm)
                            {
                                discreteAlarm.RaisedStateTagBitNumber = spec.BitNumber;
                            }
                            else if (alarm is HmiAnalogAlarm analogAlarm)
                            {
                                analogAlarm.Condition = (HmiAlarmCondition)Enum.Parse(typeof(HmiAlarmCondition), spec.Condition!, true);
                                analogAlarm.ConditionValue = spec.ConditionValue;
                            }

                            if (className != null)
                            {
                                alarm.AlarmClass = className;
                            }

                            if (priority >= 0)
                            {
                                alarm.Priority = (byte)priority;
                            }

                            if (spec.EventText != null)
                            {
                                SetText(alarm.EventText, culture, HmiSpecs.EventTextMarkup(spec.EventText), "EventText");
                            }

                            ReadAlarm(alarm, item);

                            if (string.IsNullOrEmpty(alarm.RaisedStateTag))
                            {
                                throw new PortalException(PortalErrorCode.InvalidParams, $"TIA Portal did not accept the trigger '{spec.TriggerTag}' (RaisedStateTag reads back empty)");
                            }
                        }
                        catch (Exception ex) when (ex is EngineeringException || ex is PortalException)
                        {
                            Fail(item, ex.Message);

                            if (alarm != null)
                            {
                                try
                                {
                                    DeleteHmiAlarm(alarm);
                                    item.Message += " - the alarm was removed again";
                                }
                                catch (EngineeringException cleanup)
                                {
                                    item.Message += $" - the alarm could NOT be removed ({cleanup.Message}); delete it by hand";
                                }
                            }
                        }
                    }

                    return Finish(result, false, "alarm");
                },
                ("softwarePath", softwarePath), ("kind", discrete ? "discrete" : "analog"), ("count", specs.Count), ("dryRun", dryRun));
        }

        /// <summary>Changes one alarm; an empty string or -1 keeps a value.</summary>
        public HmiEditResult UpdateHmiAlarm(
            string softwarePath, bool discrete, string alarmName, string newName, string triggerTag, int bitNumber, string condition, string conditionValue,
            string alarmClass, int priority, string eventText, string infoText, string culture, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(UpdateHmiAlarm), PortalErrorCode.RenameFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var kind = discrete ? "discrete" : "analog";
                    var alarm = FindHmiAlarm(hmi, discrete, alarmName)
                        ?? throw new PortalException(PortalErrorCode.NotFound, $"No {kind} alarm named '{alarmName}'.");
                    var result = new HmiEditResult();
                    var sets = new List<(string Where, string Old, string New, Action Apply)>();

                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        RequireHmiName(newName);

                        if (!alarmName.Equals(newName, StringComparison.OrdinalIgnoreCase) && FindHmiAlarm(hmi, discrete, newName) != null)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams, $"A {kind} alarm named '{newName}' already exists.");
                        }

                        sets.Add(("Name", alarm.Name, newName, () => alarm.Name = newName));
                    }

                    if (!string.IsNullOrWhiteSpace(triggerTag))
                    {
                        if (hmi.Tags.Find(HmiRoot(triggerTag)) == null)
                        {
                            throw new PortalException(PortalErrorCode.NotFound, $"No HMI tag named '{HmiRoot(triggerTag)}' for the trigger '{triggerTag}'.");
                        }

                        sets.Add(("RaisedStateTag", alarm.RaisedStateTag ?? string.Empty, triggerTag, () => alarm.RaisedStateTag = triggerTag));
                    }

                    if (alarm is HmiDiscreteAlarm discreteAlarm)
                    {
                        if (bitNumber >= 0)
                        {
                            sets.Add(("BitNumber", discreteAlarm.RaisedStateTagBitNumber.ToString(CultureInfo.InvariantCulture), bitNumber.ToString(CultureInfo.InvariantCulture),
                                () => discreteAlarm.RaisedStateTagBitNumber = (uint)bitNumber));
                        }

                        if (!string.IsNullOrWhiteSpace(condition) || !string.IsNullOrWhiteSpace(conditionValue))
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams, "condition and conditionValue apply to analog alarms only.");
                        }
                    }
                    else if (alarm is HmiAnalogAlarm analogAlarm)
                    {
                        if (bitNumber >= 0)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams, "bitNumber applies to discrete alarms only.");
                        }

                        if (!string.IsNullOrWhiteSpace(condition))
                        {
                            var name = HmiSpecs.Conditions.FirstOrDefault(c => c.Equals(condition.Trim(), StringComparison.OrdinalIgnoreCase))
                                ?? throw new PortalException(PortalErrorCode.InvalidParams, $"'{condition}' is not a condition. Use one of {string.Join(", ", HmiSpecs.Conditions)}.");

                            sets.Add(("Condition", analogAlarm.Condition.ToString(), name, () => analogAlarm.Condition = (HmiAlarmCondition)Enum.Parse(typeof(HmiAlarmCondition), name)));
                        }

                        if (!string.IsNullOrWhiteSpace(conditionValue))
                        {
                            var value = HmiSpecs.ParseNumber(conditionValue.Trim(), alarmName);

                            sets.Add(("ConditionValue", Convert.ToString(analogAlarm.ConditionValue, CultureInfo.InvariantCulture) ?? string.Empty, conditionValue.Trim(),
                                () => analogAlarm.ConditionValue = value));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(alarmClass))
                    {
                        var name = AlarmClassOrDefault(hmi, alarmClass)!;
                        sets.Add(("AlarmClass", alarm.AlarmClass ?? string.Empty, name, () => alarm.AlarmClass = name));
                    }

                    if (priority >= 0)
                    {
                        if (priority > 255)
                        {
                            throw new PortalException(PortalErrorCode.InvalidParams, "priority must be 0 to 255.");
                        }

                        sets.Add(("Priority", alarm.Priority.ToString(CultureInfo.InvariantCulture), priority.ToString(CultureInfo.InvariantCulture), () => alarm.Priority = (byte)priority));
                    }

                    if (sets.Count == 0 && string.IsNullOrEmpty(eventText) && string.IsNullOrEmpty(infoText))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No change was requested.");
                    }

                    foreach (var (where, oldValue, newValue, _) in sets)
                    {
                        result.Changes.Add(new XmlChange { Object = alarm.Name, Where = where, Old = oldValue, New = newValue });
                    }

                    if (!string.IsNullOrEmpty(eventText))
                    {
                        result.Changes.Add(new XmlChange { Object = alarm.Name, Where = "EventText", Old = string.Join(" / ", alarm.EventText.Items.Select(i => i.Text)), New = HmiSpecs.EventTextMarkup(eventText) });
                    }

                    if (!string.IsNullOrEmpty(infoText))
                    {
                        result.Changes.Add(new XmlChange { Object = alarm.Name, Where = "InfoText", Old = string.Join(" / ", alarm.InfoText.Items.Select(i => i.Text)), New = infoText });
                    }

                    if (dryRun)
                    {
                        return result;
                    }

                    foreach (var (_, _, _, apply) in sets)
                    {
                        apply();
                    }

                    if (!string.IsNullOrEmpty(eventText))
                    {
                        SetText(alarm.EventText, culture, HmiSpecs.EventTextMarkup(eventText), "EventText");
                    }

                    if (!string.IsNullOrEmpty(infoText))
                    {
                        SetText(alarm.InfoText, culture, infoText, "InfoText");
                    }

                    result.Applied = true;

                    return result;
                },
                ("softwarePath", softwarePath), ("alarmName", alarmName), ("dryRun", dryRun));
        }

        public HmiWriteResult DeleteHmiAlarms(string softwarePath, bool discrete, IList<string> names, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(DeleteHmiAlarms), PortalErrorCode.DeleteFailed,
                () =>
                {
                    var hmi = GetHmiSoftwareOrThrow(softwarePath);
                    var result = new HmiWriteResult();
                    var alarms = new List<(AlarmBase Alarm, HmiWriteItem Item)>();
                    var kind = discrete ? "discrete" : "analog";

                    foreach (var name in names)
                    {
                        var item = new HmiWriteItem { Name = name };
                        result.Items.Add(item);

                        var alarm = FindHmiAlarm(hmi, discrete, name);

                        if (alarm == null)
                        {
                            Fail(item, $"no {kind} alarm of this name");
                            continue;
                        }

                        ReadAlarm(alarm, item);
                        alarms.Add((alarm, item));
                    }

                    if (dryRun)
                    {
                        return Finish(result, true, "alarm");
                    }

                    foreach (var (alarm, item) in alarms)
                    {
                        try
                        {
                            DeleteHmiAlarm(alarm);

                            if (FindHmiAlarm(hmi, discrete, item.Name) != null)
                            {
                                Fail(item, "the alarm still exists after the delete");
                            }
                        }
                        catch (EngineeringException ex)
                        {
                            Fail(item, ex.Message);
                        }
                    }

                    return Finish(result, false, "alarm");
                },
                ("softwarePath", softwarePath), ("kind", discrete ? "discrete" : "analog"), ("count", names.Count), ("dryRun", dryRun));
        }

        #endregion
    }
}
