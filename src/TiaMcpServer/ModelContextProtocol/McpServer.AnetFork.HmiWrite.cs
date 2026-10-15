using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>Result of an HMI batch write (tags, alarms), item by item.</summary>
    public class ResponseHmiBatch : ResponseMessage
    {
        public bool DryRun { get; set; }

        public bool Applied { get; set; }

        public int OkCount { get; set; }

        public int FailedCount { get; set; }

        public IEnumerable<HmiWriteItem>? Items { get; set; }

        public IEnumerable<string>? Warnings { get; set; }
    }

    public static partial class McpServer
    {
        // ANet fork additions for WinCC Unified (branch 'anet'): the tools for
        // Portal.AnetFork.HmiWrite.cs. softwarePath names the HMI software, not a PLC.
        //
        // Callers: Program.BuildTools(), only under '--allow-write'. Every tool defaults to
        // dryRun = true and runs in one transaction; a batch reports every item, the ones that
        // failed their check are skipped, and a call where no item can go through fails.

        #region hmi write

        private static ResponseHmiBatch HmiBatch(HmiWriteResult result, bool dryRun, string what)
        {
            var ok = result.Items.Count(i => i.Ok);
            var failed = result.Items.Count - ok;
            var failures = failed == 0 ? string.Empty : $" {failed} failed (see Items).";

            return new ResponseHmiBatch
            {
                DryRun = dryRun,
                Applied = result.Applied,
                OkCount = ok,
                FailedCount = failed,
                Items = result.Items,
                Warnings = result.Warnings,
                Message = result.Applied
                    ? $"{ok} {what} written.{failures} {SaveHint}"
                    : $"Dry run: {ok} {what} would be written.{failures} Nothing was changed; repeat with dryRun=false to apply.",
                Meta = result.Applied ? OkMeta() : Ok()
            };
        }

        private static bool DiscreteKind(string kind)
        {
            if (kind.Equals("discrete", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (kind.Equals("analog", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new McpException($"Unknown alarm kind '{kind}'. Use 'discrete' or 'analog'.");
        }

        private static List<string> ParseNames(string names) =>
            (names ?? string.Empty).Split(';').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();

        [WriteTool]
        [McpServerTool(Name = "CreateHmiTagTable", Title = "Create HMI tag table", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create an HMI tag table of a WinCC Unified device, at the root or in an existing tag table group. A table name already in use is rejected. Defaults to a dry run that checks the name and creates nothing")]
        public static ResponseHmiEdit CreateHmiTagTable(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("tableName: name of the new tag table")] string tableName,
            [Description("groupPath: existing tag table group, e.g. Line1/Station2; empty creates the table at the root")] string groupPath = "",
            [Description("dryRun: true (default) only checks the name; false creates the table")] bool dryRun = true)
        {
            return Guarded(nameof(CreateHmiTagTable), () => HmiEdited(Portal.CreateHmiTagTable(softwarePath, tableName, groupPath, dryRun), dryRun, "table(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "RenameHmiTagTable", Title = "Rename HMI tag table", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename an HMI tag table of a WinCC Unified device. The tags keep their names; a table name already in use is rejected. Defaults to a dry run")]
        public static ResponseHmiEdit RenameHmiTagTable(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("tableName: current name of the tag table")] string tableName,
            [Description("newName: the new table name")] string newName,
            [Description("dryRun: true (default) only reports the change; false renames the table")] bool dryRun = true)
        {
            return Guarded(nameof(RenameHmiTagTable), () => HmiEdited(Portal.RenameHmiTagTable(softwarePath, tableName, newName, dryRun), dryRun, "table(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiTagTable", Title = "Delete HMI tag table", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete an HMI tag table of a WinCC Unified device together with every tag in it. TIA Portal refuses the default table. Defaults to a dry run that names the tags that would go")]
        public static ResponseHmiEdit DeleteHmiTagTable(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("tableName: name of the tag table")] string tableName,
            [Description("dryRun: true (default) only reports the tags that would go; false deletes the table")] bool dryRun = true)
        {
            return Guarded(nameof(DeleteHmiTagTable), () => HmiEdited(Portal.DeleteHmiTagTable(softwarePath, tableName, dryRun), dryRun, "table(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "CreateHmiTags", Title = "Create HMI tags in a batch", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create HMI tags of a WinCC Unified device in one tag table, many in one call: 'name=plcPath' creates a tag bound to a PLC tag or data block member (data type derived by TIA Portal), 'name:dataType' an internal tag. Bound tags need the connection (the only one of the device, or named) and plcSoftwarePath, so that every PLC path is checked to start at an existing tag or block. Defaults to a dry run that reports every item. Items whose name is taken or whose PLC source is missing are reported and skipped; a tag whose PLC path TIA Portal does not accept is removed again. Indirect addressing is not available through Openness")]
        public static ResponseHmiBatch CreateHmiTags(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("tableName: existing tag table that receives the tags")] string tableName,
            [Description("tags: items separated by ';' - 'name=plcPath' for a bound tag (e.g. 'Motor1_Speed=\"DB_Motors\".Motor1.Speed'), 'name:dataType' for an internal tag (e.g. 'Page:Int')")] string tags,
            [Description("plcSoftwarePath: path of the PLC the bound tags point at; required when a bound tag is in the batch")] string plcSoftwarePath = "",
            [Description("connection: HMI connection of the bound tags; empty takes the only connection of the device")] string connection = "",
            [Description("acquisitionCycle: acquisition cycle for every tag, as TIA Portal names it (e.g. 'T1s'); empty keeps the default")] string acquisitionCycle = "",
            [Description("dryRun: true (default) only reports every item; false creates the tags")] bool dryRun = true)
        {
            return Guarded(nameof(CreateHmiTags), () =>
                HmiBatch(Portal.CreateHmiTags(softwarePath, tableName, HmiSpecs.ParseTagSpecs(tags), connection, plcSoftwarePath, acquisitionCycle, dryRun), dryRun, "tag(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "UpdateHmiTag", Title = "Update one HMI tag", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Change the connection, PLC path, data type (internal tags) or acquisition cycle of one HMI tag of a WinCC Unified device; an empty value keeps the current one. For many PLC paths at once use SetHmiTagPlcTags, for names RenameHmiTags. Defaults to a dry run that reports old against new. A PLC path TIA Portal does not accept fails the call")]
        public static ResponseHmiEdit UpdateHmiTag(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("tagName: name of the HMI tag")] string tagName,
            [Description("connection: new HMI connection; empty keeps it")] string connection = "",
            [Description("plcTag: new PLC path, e.g. '\"DB_Motors\".Motor1.Speed'; empty keeps it")] string plcTag = "",
            [Description("dataType: new data type of an internal tag; empty keeps it")] string dataType = "",
            [Description("acquisitionCycle: new acquisition cycle, e.g. 'T1s'; empty keeps it")] string acquisitionCycle = "",
            [Description("dryRun: true (default) only reports the change; false writes it")] bool dryRun = true)
        {
            return Guarded(nameof(UpdateHmiTag), () => HmiEdited(Portal.UpdateHmiTag(softwarePath, tagName, connection, plcTag, dataType, acquisitionCycle, dryRun), dryRun, "value(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiTags", Title = "Delete HMI tags in a batch", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete HMI tags of a WinCC Unified device by name, many in one call. Defaults to a dry run that reports, per tag, the tag dynamizations, script lines and alarms that use it - they are left dangling by the delete. A name that is no tag is reported and skipped")]
        public static ResponseHmiBatch DeleteHmiTags(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("names: tag names separated by ';'")] string names,
            [Description("dryRun: true (default) only reports the tags and their users; false deletes them")] bool dryRun = true)
        {
            return Guarded(nameof(DeleteHmiTags), () => HmiBatch(Portal.DeleteHmiTags(softwarePath, ParseNames(names), dryRun), dryRun, "tag(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "CreateHmiAlarms", Title = "Create HMI alarms in a batch", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create discrete or analog alarms of a WinCC Unified device, many in one call. A discrete alarm is 'name|triggerTag|bitNumber|eventText', an analog alarm 'name|triggerTag|condition|value|eventText' (condition LowerLimit, UpperLimit, Equal, NotEqual, LowerLimitOrEqual or UpperLimitOrEqual); the texts are optional, the trigger may be a tag or 'Tag.Member'. Defaults to a dry run that reports every item. Items whose name is taken or whose trigger tag does not exist are reported and skipped; an alarm TIA Portal does not accept is removed again")]
        public static ResponseHmiBatch CreateHmiAlarms(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("kind: 'discrete' or 'analog'")] string kind,
            [Description("alarms: items separated by ';', fields by '|' - discrete 'name|triggerTag|bitNumber|eventText', analog 'name|triggerTag|condition|value|eventText'")] string alarms,
            [Description("alarmClass: alarm class for every alarm, e.g. 'Alarm'; empty keeps TIA Portal's default")] string alarmClass = "",
            [Description("priority: priority 0 to 255 for every alarm; -1 keeps the default")] int priority = -1,
            [Description("culture: language of the event texts, e.g. en-US; may stay empty when the project has one language")] string culture = "",
            [Description("dryRun: true (default) only reports every item; false creates the alarms")] bool dryRun = true)
        {
            return Guarded(nameof(CreateHmiAlarms), () =>
            {
                var discrete = DiscreteKind(kind);

                return HmiBatch(Portal.CreateHmiAlarms(softwarePath, discrete, HmiSpecs.ParseAlarmSpecs(alarms, discrete), alarmClass, priority, culture, dryRun), dryRun, "alarm(s)");
            });
        }

        [WriteTool]
        [McpServerTool(Name = "UpdateHmiAlarm", Title = "Update one HMI alarm", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Change one discrete or analog alarm of a WinCC Unified device: name, trigger tag, bit number (discrete), condition and value (analog), alarm class, priority, event text, info text. An empty value or -1 keeps the current one. Defaults to a dry run that reports old against new")]
        public static ResponseHmiEdit UpdateHmiAlarm(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("kind: 'discrete' or 'analog'")] string kind,
            [Description("alarmName: current name of the alarm")] string alarmName,
            [Description("newName: new name; empty keeps it")] string newName = "",
            [Description("triggerTag: new trigger tag or 'Tag.Member'; empty keeps it")] string triggerTag = "",
            [Description("bitNumber: new bit of the trigger tag (discrete); -1 keeps it")] int bitNumber = -1,
            [Description("condition: new condition (analog), e.g. UpperLimit; empty keeps it")] string condition = "",
            [Description("conditionValue: new limit (analog), e.g. 80 or 12.5; empty keeps it")] string conditionValue = "",
            [Description("alarmClass: new alarm class; empty keeps it")] string alarmClass = "",
            [Description("priority: new priority 0 to 255; -1 keeps it")] int priority = -1,
            [Description("eventText: new event text (plain text); empty keeps it")] string eventText = "",
            [Description("infoText: new info text; empty keeps it")] string infoText = "",
            [Description("culture: language of the texts, e.g. en-US; may stay empty when the project has one language")] string culture = "",
            [Description("dryRun: true (default) only reports the change; false writes it")] bool dryRun = true)
        {
            return Guarded(nameof(UpdateHmiAlarm), () =>
                HmiEdited(Portal.UpdateHmiAlarm(softwarePath, DiscreteKind(kind), alarmName, newName, triggerTag, bitNumber, condition, conditionValue, alarmClass, priority, eventText, infoText, culture, dryRun), dryRun, "value(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiAlarms", Title = "Delete HMI alarms in a batch", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete discrete or analog alarms of a WinCC Unified device by name, many in one call. Defaults to a dry run that reports each alarm with its trigger. A name that is no alarm of that kind is reported and skipped")]
        public static ResponseHmiBatch DeleteHmiAlarms(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("kind: 'discrete' or 'analog'")] string kind,
            [Description("names: alarm names separated by ';'")] string names,
            [Description("dryRun: true (default) only reports the alarms; false deletes them")] bool dryRun = true)
        {
            return Guarded(nameof(DeleteHmiAlarms), () => HmiBatch(Portal.DeleteHmiAlarms(softwarePath, DiscreteKind(kind), ParseNames(names), dryRun), dryRun, "alarm(s)"));
        }

        #endregion
    }
}
