using ModelContextProtocol.Server;
using System.ComponentModel;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpPrompts
    {
        // ANet fork additions (branch 'anet'): one prompt per tool of McpServer.AnetFork.cs, as
        // Test_708 and Test_709 require. Kept in this one file so merges from upstream stay trivial.

        #region ANet fork templates

        [McpServerPrompt(Name = "RenameBlockGroup"), Description("Rename a block group")]
        public static string RenameBlockGroup(string softwarePath, string groupPath, string newName)
        {
            return $@"Rename a user group below 'Program blocks'. The blocks inside keep their names and numbers; a sibling group of the new name is rejected (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the group, e.g. Station1/Conveyor
- newName: the new group name, without a slash

Use the RenameBlockGroup tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- newName: {newName}";
        }

        [McpServerPrompt(Name = "RenameTypeGroup"), Description("Rename a PLC data type group")]
        public static string RenameTypeGroup(string softwarePath, string groupPath, string newName)
        {
            return $@"Rename a user group below 'PLC data types'. The types inside keep their names; a sibling group of the new name is rejected (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the group, e.g. Station1/Types
- newName: the new group name, without a slash

Use the RenameTypeGroup tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- newName: {newName}";
        }

        [McpServerPrompt(Name = "RenameTagTableGroup"), Description("Rename a tag table group")]
        public static string RenameTagTableGroup(string softwarePath, string groupPath, string newName)
        {
            return $@"Rename a user group below 'PLC tags'. The tag tables inside keep their names; a sibling group of the new name is rejected (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the group, e.g. Station1/IO
- newName: the new group name, without a slash

Use the RenameTagTableGroup tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- newName: {newName}";
        }

        [McpServerPrompt(Name = "RenameTechnologicalObject"), Description("Rename a technology object")]
        public static string RenameTechnologicalObject(string softwarePath, string name, string newName)
        {
            return $@"Rename a technology object (PID_Temp, axis, ...). A block or technology object of the new name is rejected. HMI tags and watch tables that name the object are not updated (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- name: current name of the technology object
- newName: the new name, without a slash

Use the RenameTechnologicalObject tool with these parameters:
- softwarePath: {softwarePath}
- name: {name}
- newName: {newName}";
        }

        [McpServerPrompt(Name = "SetNetworkText"), Description("Set the title and comment of one network")]
        public static string SetNetworkText(string softwarePath, string blockPath, string network, string title = "", string comment = "", string culture = "", string dryRun = "true")
        {
            return $@"Set the title and/or comment of one network. The block is exported, edited and imported over itself. Run it with dryRun true first, show the reported change, and only then repeat with dryRun false (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- blockPath: root-relative path of the block, e.g. Station1/FB_Conveyor
- network: number of the network, starting at 1
- title: the new network title; leave out to keep the current one
- comment: the new network comment; leave out to keep the current one
- culture: language of the text, e.g. en-US; may stay empty when the project has one language
- dryRun: true only reports what would change; false imports the edited block

Use the SetNetworkText tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- network: {network}
- title: {title}
- comment: {comment}
- culture: {culture}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "ReplaceNetworkText"), Description("Replace a text in the network titles and comments of a block")]
        public static string ReplaceNetworkText(string softwarePath, string blockPath, string find, string replace, string culture = "", string dryRun = "true")
        {
            return $@"Replace a text in the titles and comments of every network of a block (literal, case-sensitive). The block is exported, edited and imported over itself. Run it with dryRun true first, show the reported changes, and only then repeat with dryRun false (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- blockPath: root-relative path of the block, e.g. Station1/FB_Conveyor
- find: the text to look for
- replace: the text to put in its place
- culture: restrict the replacement to one language, e.g. en-US; empty replaces in every language
- dryRun: true only reports what would change; false imports the edited block

Use the ReplaceNetworkText tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- find: {find}
- replace: {replace}
- culture: {culture}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "RenameMembers"), Description("Rename members of a block or PLC data type and their references")]
        public static string RenameMembers(string softwarePath, string ownerPath, string kind, string renames, string dryRun = "true")
        {
            return $@"Rename members of a block interface, a data block or a PLC data type, together with the references to them in all blocks. Every block and type is exported, the affected ones are edited and imported back. Run it with dryRun true first and show the affected objects, the changes, the warnings and the objects that were not scanned; only then repeat with dryRun false. HMI tags, watch tables and technology objects are not updated, and start values of a renamed member may be reset (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- ownerPath: root-relative path of the block or PLC data type that declares the members
- kind: 'block' when ownerPath is a block (FB, FC, OB, DB), 'type' when it is a PLC data type
- renames: 'memberPath=newName' pairs separated by ';', e.g. 'Speed=Velocity; Config.Limit=MaxLimit'
- dryRun: true only reports what would change; false imports the edited objects

Use the RenameMembers tool with these parameters:
- softwarePath: {softwarePath}
- ownerPath: {ownerPath}
- kind: {kind}
- renames: {renames}
- dryRun: {dryRun}";
        }

        #endregion
    }
}
