using ModelContextProtocol.Server;
using System.ComponentModel;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpPrompts
    {
        // ANet fork additions: one prompt per tool of McpServer.AnetFork.HmiWrite.cs, as Test_708
        // and Test_709 require.

        #region ANet fork HMI write templates

        [McpServerPrompt(Name = "CreateHmiTagTable"), Description("Create an HMI tag table")]
        public static string CreateHmiTagTable(string softwarePath, string tableName, string groupPath = "", string dryRun = "true")
        {
            return $@"Create an HMI tag table of a WinCC Unified device (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- tableName: name of the new tag table
- groupPath: existing tag table group, empty for the root
- dryRun: true only checks the name, false creates the table

Use the CreateHmiTagTable tool with these parameters:
- softwarePath: {softwarePath}
- tableName: {tableName}
- groupPath: {groupPath}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "RenameHmiTagTable"), Description("Rename an HMI tag table")]
        public static string RenameHmiTagTable(string softwarePath, string tableName, string newName, string dryRun = "true")
        {
            return $@"Rename an HMI tag table of a WinCC Unified device; the tags keep their names (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- tableName: current name of the tag table
- newName: the new table name
- dryRun: true only reports, false renames

Use the RenameHmiTagTable tool with these parameters:
- softwarePath: {softwarePath}
- tableName: {tableName}
- newName: {newName}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "DeleteHmiTagTable"), Description("Delete an HMI tag table with its tags")]
        public static string DeleteHmiTagTable(string softwarePath, string tableName, string dryRun = "true")
        {
            return $@"Delete an HMI tag table of a WinCC Unified device together with its tags. Run it with dryRun true first to see the tags that would go (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- tableName: name of the tag table
- dryRun: true only reports, false deletes

Use the DeleteHmiTagTable tool with these parameters:
- softwarePath: {softwarePath}
- tableName: {tableName}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "CreateHmiTags"), Description("Create HMI tags bound to the PLC or internal, in a batch")]
        public static string CreateHmiTags(string softwarePath, string tableName, string tags, string plcSoftwarePath = "", string connection = "", string acquisitionCycle = "", string dryRun = "true")
        {
            return $@"Create HMI tags of a WinCC Unified device in one tag table, many in one call. 'name=plcPath' binds a tag to a PLC tag or data block member, 'name:dataType' creates an internal tag. Run it with dryRun true first and show the items, then repeat with dryRun false (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- tableName: existing tag table that receives the tags
- tags: items separated by ';', e.g. Motor1_Speed=""DB_Motors"".Motor1.Speed; Page:Int
- plcSoftwarePath: path of the PLC the bound tags point at, required for bound tags
- connection: HMI connection of the bound tags, empty takes the only one
- acquisitionCycle: e.g. T1s, empty keeps the default
- dryRun: true only reports, false creates

Use the CreateHmiTags tool with these parameters:
- softwarePath: {softwarePath}
- tableName: {tableName}
- tags: {tags}
- plcSoftwarePath: {plcSoftwarePath}
- connection: {connection}
- acquisitionCycle: {acquisitionCycle}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "UpdateHmiTag"), Description("Change connection, PLC path, data type or cycle of one HMI tag")]
        public static string UpdateHmiTag(string softwarePath, string tagName, string connection = "", string plcTag = "", string dataType = "", string acquisitionCycle = "", string dryRun = "true")
        {
            return $@"Change one HMI tag of a WinCC Unified device; an empty value keeps the current one (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- tagName: name of the HMI tag
- connection: new HMI connection, empty keeps it
- plcTag: new PLC path, empty keeps it
- dataType: new data type of an internal tag, empty keeps it
- acquisitionCycle: new acquisition cycle, empty keeps it
- dryRun: true only reports, false writes

Use the UpdateHmiTag tool with these parameters:
- softwarePath: {softwarePath}
- tagName: {tagName}
- connection: {connection}
- plcTag: {plcTag}
- dataType: {dataType}
- acquisitionCycle: {acquisitionCycle}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "DeleteHmiTags"), Description("Delete HMI tags by name, in a batch")]
        public static string DeleteHmiTags(string softwarePath, string names, string dryRun = "true")
        {
            return $@"Delete HMI tags of a WinCC Unified device. Run it with dryRun true first: it lists the dynamizations, scripts and alarms that use each tag (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- names: tag names separated by ';'
- dryRun: true only reports, false deletes

Use the DeleteHmiTags tool with these parameters:
- softwarePath: {softwarePath}
- names: {names}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "CreateHmiAlarms"), Description("Create discrete or analog HMI alarms, in a batch")]
        public static string CreateHmiAlarms(string softwarePath, string kind, string alarms, string alarmClass = "", string priority = "-1", string culture = "", string dryRun = "true")
        {
            return $@"Create discrete or analog alarms of a WinCC Unified device, many in one call. Discrete: 'name|triggerTag|bitNumber|eventText'; analog: 'name|triggerTag|condition|value|eventText'. Run it with dryRun true first and show the items (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- kind: discrete or analog
- alarms: items separated by ';', fields by '|'
- alarmClass: alarm class for every alarm, empty keeps the default
- priority: 0 to 255, -1 keeps the default
- culture: language of the event texts, e.g. en-US
- dryRun: true only reports, false creates

Use the CreateHmiAlarms tool with these parameters:
- softwarePath: {softwarePath}
- kind: {kind}
- alarms: {alarms}
- alarmClass: {alarmClass}
- priority: {priority}
- culture: {culture}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "UpdateHmiAlarm"), Description("Change one discrete or analog HMI alarm")]
        public static string UpdateHmiAlarm(string softwarePath, string kind, string alarmName, string newName = "", string triggerTag = "", string bitNumber = "-1", string condition = "", string conditionValue = "", string alarmClass = "", string priority = "-1", string eventText = "", string infoText = "", string culture = "", string dryRun = "true")
        {
            return $@"Change one alarm of a WinCC Unified device; an empty value or -1 keeps the current one (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- kind: discrete or analog
- alarmName: current name of the alarm
- newName, triggerTag, bitNumber, condition, conditionValue, alarmClass, priority, eventText, infoText: the new values
- culture: language of the texts
- dryRun: true only reports, false writes

Use the UpdateHmiAlarm tool with these parameters:
- softwarePath: {softwarePath}
- kind: {kind}
- alarmName: {alarmName}
- newName: {newName}
- triggerTag: {triggerTag}
- bitNumber: {bitNumber}
- condition: {condition}
- conditionValue: {conditionValue}
- alarmClass: {alarmClass}
- priority: {priority}
- eventText: {eventText}
- infoText: {infoText}
- culture: {culture}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "DeleteHmiAlarms"), Description("Delete discrete or analog HMI alarms, in a batch")]
        public static string DeleteHmiAlarms(string softwarePath, string kind, string names, string dryRun = "true")
        {
            return $@"Delete alarms of a WinCC Unified device by name. Run it with dryRun true first (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- kind: discrete or analog
- names: alarm names separated by ';'
- dryRun: true only reports, false deletes

Use the DeleteHmiAlarms tool with these parameters:
- softwarePath: {softwarePath}
- kind: {kind}
- names: {names}
- dryRun: {dryRun}";
        }

        #endregion
    }
}
