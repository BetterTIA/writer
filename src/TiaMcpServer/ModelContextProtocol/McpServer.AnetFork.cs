using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>Result of an export -> edit -> import tool, for a dry run as well as for the real run.</summary>
    public class ResponseXmlEdit : ResponseMessage
    {
        public bool DryRun { get; set; }

        /// <summary>True when the edited objects were imported into the project.</summary>
        public bool Applied { get; set; }

        public int ChangeCount { get; set; }

        /// <summary>The objects that are (or would be) imported, in import order.</summary>
        public IEnumerable<XmlEditObject>? Objects { get; set; }

        /// <summary>The first changes, old text against new text; ChangeCount has the total.</summary>
        public IEnumerable<XmlChange>? Changes { get; set; }

        public IEnumerable<string>? Warnings { get; set; }

        /// <summary>Objects TIA Portal would not export, so references inside them were not seen.</summary>
        public IEnumerable<string>? NotScanned { get; set; }
    }

    public static partial class McpServer
    {
        // ANet fork additions (branch 'anet'): the tools for Portal.AnetFork.cs, kept in this one
        // file so merges from upstream stay trivial.
        //
        // Callers: registered by Program.BuildTools() only when '--allow-write' was passed.
        // Affected API: additive only. Data: ResponseRenamed for the setter renames,
        // ResponseXmlEdit for the export -> edit -> import tools. Those default to dryRun = true:
        // the first call reports the affected objects and every change and imports nothing.

        #region renames with a setter

        [WriteTool]
        [McpServerTool(Name = "RenameBlockGroup", Title = "Rename block group", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename a user group below 'Program blocks'. The blocks inside keep their names and numbers; a sibling group of the new name is rejected")]
        public static ResponseRenamed RenameBlockGroup(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("groupPath: root-relative path of the group, e.g. Station1/Conveyor")] string groupPath,
            [Description("newName: the new group name, without a slash")] string newName)
        {
            return Guarded(nameof(RenameBlockGroup), () =>
            {
                Portal.RenameBlockGroup(softwarePath, groupPath, newName);
                return Renamed("Block group", groupPath, newName, ReplaceLeaf(groupPath, newName));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "RenameTypeGroup", Title = "Rename type group", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename a user group below 'PLC data types'. The types inside keep their names; a sibling group of the new name is rejected")]
        public static ResponseRenamed RenameTypeGroup(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("groupPath: root-relative path of the group, e.g. Station1/Types")] string groupPath,
            [Description("newName: the new group name, without a slash")] string newName)
        {
            return Guarded(nameof(RenameTypeGroup), () =>
            {
                Portal.RenameTypeGroup(softwarePath, groupPath, newName);
                return Renamed("Type group", groupPath, newName, ReplaceLeaf(groupPath, newName));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "RenameTagTableGroup", Title = "Rename tag table group", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename a user group below 'PLC tags'. The tag tables inside keep their names; a sibling group of the new name is rejected")]
        public static ResponseRenamed RenameTagTableGroup(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("groupPath: root-relative path of the group, e.g. Station1/IO")] string groupPath,
            [Description("newName: the new group name, without a slash")] string newName)
        {
            return Guarded(nameof(RenameTagTableGroup), () =>
            {
                Portal.RenameTagTableGroup(softwarePath, groupPath, newName);
                return Renamed("Tag table group", groupPath, newName, ReplaceLeaf(groupPath, newName));
            });
        }

        [WriteTool]
        [McpServerTool(Name = "RenameTechnologicalObject", Title = "Rename technology object", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename a technology object (PID_Temp, axis, ...). A block or technology object of the new name is rejected. HMI tags and watch tables that name the object are not updated")]
        public static ResponseRenamed RenameTechnologicalObject(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("name: current name of the technology object")] string name,
            [Description("newName: the new name, without a slash")] string newName)
        {
            return Guarded(nameof(RenameTechnologicalObject), () =>
            {
                Portal.RenameTechnologicalObject(softwarePath, name, newName);
                return Renamed("Technology object", name, newName, newName);
            });
        }

        #endregion

        #region export -> edit -> import

        /// <summary>Changes listed in a response; a rename across a large PLC can produce thousands.</summary>
        private const int MaxReportedChanges = 200;

        private static ResponseXmlEdit XmlEdited(XmlEditResult result, bool dryRun)
        {
            var summary = $"{result.Changes.Count} change(s) in {result.Objects.Count} object(s)";

            return new ResponseXmlEdit
            {
                DryRun = dryRun,
                Applied = result.Applied,
                ChangeCount = result.Changes.Count,
                Objects = result.Objects,
                Changes = result.Changes.Take(MaxReportedChanges),
                Warnings = result.Warnings,
                NotScanned = result.NotScanned,
                Message = result.Changes.Count == 0
                    ? "Nothing to change."
                    : result.Applied
                        ? $"{summary} imported. Compile the software to verify. {SaveHint}"
                        : $"Dry run: {summary} would be imported. Nothing was changed; repeat with dryRun=false to apply.",
                Meta = result.Applied ? OkMeta() : Ok()
            };
        }

        [WriteTool]
        [McpServerTool(Name = "SetNetworkText", Title = "Set network title and comment", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Set the title and/or comment of one network. Openness has no setter for them, so the block is exported, edited and imported over itself. Defaults to a dry run that reports the change and imports nothing. Know-how protected blocks are rejected; fail-safe blocks need allowFailsafe")]
        public static ResponseXmlEdit SetNetworkText(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("blockPath: root-relative path of the block, e.g. Station1/FB_Conveyor")] string blockPath,
            [Description("network: number of the network, starting at 1")] int network,
            [Description("title: the new network title; leave out to keep the current one")] string? title = null,
            [Description("comment: the new network comment; leave out to keep the current one")] string? comment = null,
            [Description("culture: language of the text, e.g. en-US; may stay empty when the project has one language")] string culture = "",
            [Description("dryRun: true (default) only reports what would change; false imports the edited block")] bool dryRun = true,
            [Description("allowFailsafe: true allows importing a fail-safe block, which changes its F-signature")] bool allowFailsafe = false)
        {
            return Guarded(nameof(SetNetworkText), () => XmlEdited(
                Portal.EditNetworkTexts(softwarePath, blockPath,
                    doc => SimaticMl.SetNetworkText(doc, network, title, comment, culture), dryRun, allowFailsafe),
                dryRun));
        }

        [WriteTool]
        [McpServerTool(Name = "ReplaceNetworkText", Title = "Replace in network titles and comments", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Replace a text in the titles and comments of every network of a block (literal, case-sensitive). The block is exported, edited and imported over itself. Defaults to a dry run that reports each change and imports nothing. Know-how protected blocks are rejected; fail-safe blocks need allowFailsafe")]
        public static ResponseXmlEdit ReplaceNetworkText(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("blockPath: root-relative path of the block, e.g. Station1/FB_Conveyor")] string blockPath,
            [Description("find: the text to look for")] string find,
            [Description("replace: the text to put in its place")] string replace,
            [Description("culture: restrict the replacement to one language, e.g. en-US; empty replaces in every language")] string culture = "",
            [Description("dryRun: true (default) only reports what would change; false imports the edited block")] bool dryRun = true,
            [Description("allowFailsafe: true allows importing a fail-safe block, which changes its F-signature")] bool allowFailsafe = false)
        {
            return Guarded(nameof(ReplaceNetworkText), () => XmlEdited(
                Portal.EditNetworkTexts(softwarePath, blockPath,
                    doc => SimaticMl.ReplaceNetworkText(doc, find, replace, culture), dryRun, allowFailsafe),
                dryRun));
        }

        [WriteTool]
        [McpServerTool(Name = "RenameMembers", Title = "Rename members of a block or type", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Rename members of a block interface, a data block or a PLC data type, together with the references to them in all blocks. Openness has no setter for member names, so every block and type is exported, the affected ones are edited and imported back. Defaults to a dry run that lists the affected objects, every change and what could not be resolved, and imports nothing. Several renames in one call are applied together, so a swap is possible. Not updated: HMI tags, watch tables, technology objects. Start values and current values of a renamed member may be reset - back them up first")]
        public static ResponseXmlEdit RenameMembers(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("ownerPath: root-relative path of the block or PLC data type that declares the members")] string ownerPath,
            [Description("kind: 'block' when ownerPath is a block (FB, FC, OB, DB), 'type' when it is a PLC data type")] string kind,
            [Description("renames: 'memberPath=newName' pairs separated by ';', e.g. 'Speed=Velocity; Config.Limit=MaxLimit'. The path is relative to the owner, nested struct members separated by '.'")] string renames,
            [Description("dryRun: true (default) only reports what would change; false imports the edited objects")] bool dryRun = true,
            [Description("allowFailsafe: true allows importing fail-safe blocks, which changes their F-signature")] bool allowFailsafe = false)
        {
            return Guarded(nameof(RenameMembers), () =>
            {
                var isType = kind.Equals("type", StringComparison.OrdinalIgnoreCase);

                if (!isType && !kind.Equals("block", StringComparison.OrdinalIgnoreCase))
                {
                    throw new McpException($"Unknown kind '{kind}'. Use 'type' or 'block'.");
                }

                return XmlEdited(
                    Portal.RenameMembers(softwarePath, ownerPath, isType, ParseRenames(renames), dryRun, allowFailsafe),
                    dryRun);
            });
        }

        /// <summary>"A=B; C.D=E" to { "A": "B", "C.D": "E" }.</summary>
        public static IDictionary<string, string> ParseRenames(string renames)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in (renames ?? string.Empty).Split([';'], StringSplitOptions.RemoveEmptyEntries).Where(p => p.Trim().Length > 0))
            {
                var parts = pair.Split('=');

                if (parts.Length != 2 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
                {
                    throw new McpException($"'{pair.Trim()}' is not a rename. Write 'memberPath=newName', several separated by ';'.");
                }

                result[parts[0].Trim()] = parts[1].Trim();
            }

            return result;
        }

        #endregion
    }
}
