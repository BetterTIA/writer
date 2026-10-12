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

        [McpServerPrompt(Name = "ReplaceNetworkText"), Description("Replace texts in the network titles and comments of a block")]
        public static string ReplaceNetworkText(string softwarePath, string blockPath, string replacements, string scope = "networks", string culture = "", string dryRun = "true")
        {
            return $@"Replace texts in the titles and comments of every network of a block (literal, case-sensitive). All pairs are applied in one pass, so a swap is possible. The block is exported, edited and imported over itself. Run it with dryRun true first, show the reported changes, and only then repeat with dryRun false (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- blockPath: root-relative path of the block, e.g. Station1/FB_Conveyor
- replacements: 'find=replace' pairs separated by ';', e.g. 'Pump 1=Pump 2; Pump 2=Pump 1'
- scope: 'networks' for network titles and comments only, 'all' to include the block title and comment and GRAPH texts
- culture: restrict the replacement to one language, e.g. en-US; empty replaces in every language
- dryRun: true only reports what would change; false imports the edited block

Use the ReplaceNetworkText tool with these parameters:
- softwarePath: {softwarePath}
- blockPath: {blockPath}
- replacements: {replacements}
- scope: {scope}
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

        [McpServerPrompt(Name = "RenameTags"), Description("Rename many PLC tags at once, swaps included")]
        public static string RenameTags(string softwarePath, string renames, string dryRun = "true")
        {
            return $@"Rename many PLC tags in one transaction, by name across all tag tables. A new name may be the current name of another tag of the batch, so a swap needs no temporary names. Run it with dryRun true first and show the tags, their users and the fail-safe blocks that use them; tags used by fail-safe blocks must be confirmed by the operator by name before they are renamed (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- renames: 'currentName=newName' pairs separated by ';', e.g. 'iPump1_Run=iPump2_Run; iPump2_Run=iPump1_Run'
- dryRun: true only reports what would change; false renames the tags

Use the RenameTags tool with these parameters:
- softwarePath: {softwarePath}
- renames: {renames}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "RenameObjects"), Description("Rename many blocks, technology objects and types at once")]
        public static string RenameObjects(string softwarePath, string renames, string dryRun = "true")
        {
            return $@"Rename many blocks, technology objects and PLC data types in one transaction, by name. A new name may be the current name of another object of the batch, so a swap needs no temporary names. Run it with dryRun true first and show the objects and the fail-safe blocks involved; those must be confirmed by the operator by name. Callers of a renamed block are inconsistent until compiled (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- renames: 'currentName=newName' pairs separated by ';', e.g. 'PID_1=PID_2; PID_2=PID_1'
- dryRun: true only reports what would change; false renames the objects

Use the RenameObjects tool with these parameters:
- softwarePath: {softwarePath}
- renames: {renames}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "SetTagComment"), Description("Set the comment of a PLC tag")]
        public static string SetTagComment(string softwarePath, string tagPath, string comment, string culture = "")
        {
            return $@"Set the comment of one PLC tag (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- tagPath: path of the tag including its table, e.g. TagGroup1/Table1/Tag_1
- comment: the new comment text
- culture: language of the text, e.g. en-US; may stay empty when the project has one language

Use the SetTagComment tool with these parameters:
- softwarePath: {softwarePath}
- tagPath: {tagPath}
- comment: {comment}
- culture: {culture}";
        }

        [McpServerPrompt(Name = "ReplaceTagComments"), Description("Replace texts in the comments of PLC tags")]
        public static string ReplaceTagComments(string softwarePath, string replacements, string tagTablePath = "", string culture = "", string dryRun = "true")
        {
            return $@"Replace texts in the comments of the PLC tags of one tag table, or of all tables (literal, case-sensitive). All pairs are applied in one pass, so a swap is possible. Run it with dryRun true first, show the reported changes, and only then repeat with dryRun false (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- replacements: 'find=replace' pairs separated by ';', e.g. 'Pump 1=Pump 2; Pump 2=Pump 1'
- tagTablePath: root-relative path of the tag table; empty covers every tag table of the PLC
- culture: restrict the replacement to one language, e.g. en-US; empty replaces in every language
- dryRun: true only reports what would change; false writes the comments

Use the ReplaceTagComments tool with these parameters:
- softwarePath: {softwarePath}
- replacements: {replacements}
- tagTablePath: {tagTablePath}
- culture: {culture}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "RenameWatchTableGroup"), Description("Rename a watch table group")]
        public static string RenameWatchTableGroup(string softwarePath, string groupPath, string newName)
        {
            return $@"Rename a user group below 'Watch and force tables'. The tables inside keep their names; a sibling group of the new name is rejected (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- groupPath: root-relative path of the group, e.g. Station1/Commissioning
- newName: the new group name, without a slash

Use the RenameWatchTableGroup tool with these parameters:
- softwarePath: {softwarePath}
- groupPath: {groupPath}
- newName: {newName}";
        }

        [McpServerPrompt(Name = "CompileBlock"), Description("Compile one block or one block group")]
        public static string CompileBlock(string softwarePath, string path)
        {
            return $@"Compile one block, or every block of one block group, instead of the whole PLC software. A fail-safe block, or a group that contains one, is refused: compiling the safety program stays with the operator.

Common parameter values:
- softwarePath: defines the path in the project structure to the PLC software
- path: root-relative path of a block or of a block group, e.g. Station1/FB_Conveyor or Station1

Use the CompileBlock tool with these parameters:
- softwarePath: {softwarePath}
- path: {path}";
        }

        #endregion
    }
}
