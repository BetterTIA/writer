using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.Compiler;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        /// <summary>Objects that are not edited but read the changed interface, so they turn inconsistent.</summary>
        public IEnumerable<string>? Dependents { get; set; }

        /// <summary>Fail-safe blocks among the dependents: the safety program has to be compiled again.</summary>
        public IEnumerable<string>? FailsafeDependents { get; set; }

        /// <summary>After a real run: blocks that are inconsistent now and were not before the import.</summary>
        public IEnumerable<string>? InconsistentAfter { get; set; }

        /// <summary>With summaryOnly: one line per object with its change count per network, instead of Changes.</summary>
        public IEnumerable<string>? ChangeSummary { get; set; }

        /// <summary>With summaryOnly: the file on the server machine that holds the complete result as JSON.</summary>
        public string? ReportPath { get; set; }
    }

    /// <summary>One technology object, as GetTechnologicalObjects lists it.</summary>
    public class TechnologicalObjectInfo
    {
        public string? Name { get; set; }

        /// <summary>Number of the data block behind the technology object.</summary>
        public int? Number { get; set; }

        /// <summary>Object type, e.g. PID_Temp (attribute OfSystemLibElement).</summary>
        public string? Type { get; set; }

        /// <summary>Version of the object type (attribute OfSystemLibVersion).</summary>
        public string? Version { get; set; }

        /// <summary>Every attribute Openness reports for it; only with attributes=true.</summary>
        public IEnumerable<Attribute>? Attributes { get; set; }
    }

    public class ResponseTechnologicalObjects : ResponseMessage
    {
        public IEnumerable<TechnologicalObjectInfo>? Items { get; set; }
    }

    /// <summary>Result of a batch rename (RenameTags, RenameObjects), for a dry run as well as for the real run.</summary>
    public class ResponseBatchRename : ResponseMessage
    {
        public bool DryRun { get; set; }

        public bool Applied { get; set; }

        /// <summary>Number of objects in the batch; Items may hold fewer with summaryOnly.</summary>
        public int ItemCount { get; set; }

        /// <summary>All objects of the batch, or with summaryOnly only those that touch the safety program.</summary>
        public IEnumerable<BatchRenameItem>? Items { get; set; }

        public IEnumerable<string>? Warnings { get; set; }

        /// <summary>With summaryOnly: the file on the server machine that holds the complete result as JSON.</summary>
        public string? ReportPath { get; set; }
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

        /// <summary>
        /// Writes the complete result of a tool call into the temp directory, for the calls that
        /// return a summary only: a large batch is tens of kilobytes the caller rarely reads.
        /// </summary>
        public static string WriteReport(string toolName, object result)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), $"tiamcp-report-{toolName}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
                var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

                File.WriteAllText(path, JsonSerializer.Serialize(result, options), Encoding.UTF8);

                return path;
            }
            catch (Exception ex)
            {
                // The report is a convenience: failing here would roll back a rename that worked.
                return $"<report not written: {ex.Message}>";
            }
        }

        private static ResponseXmlEdit XmlEdited(XmlEditResult result, bool dryRun, bool summaryOnly = false, string toolName = "")
        {
            var summary = $"{result.Changes.Count} change(s) in {result.Objects.Count} object(s)";

            // References inside a block that could not be exported are invisible to the edit,
            // so this must not hide in a list at the end of the response.
            var safety = result.FailsafeDependents.Count == 0
                ? string.Empty
                : $" WARNING: {result.FailsafeDependents.Count} fail-safe block(s) depend on what is imported (see FailsafeDependents) - " +
                  "the safety program may have to be compiled again; show this to the operator before applying.";
            var after = result.InconsistentAfter.Count == 0
                ? string.Empty
                : $" {result.InconsistentAfter.Count} block(s) are inconsistent now (see InconsistentAfter).";
            var blind = result.NotScanned.Count == 0
                ? string.Empty
                : $" WARNING: {result.NotScanned.Count} object(s) could not be exported and were NOT checked for references " +
                  "(inconsistent or know-how protected, see NotScanned) - compile the software first and repeat the dry run.";

            return new ResponseXmlEdit
            {
                DryRun = dryRun,
                Applied = result.Applied,
                ChangeCount = result.Changes.Count,
                Objects = result.Objects,
                Changes = summaryOnly ? null : result.Changes.Take(MaxReportedChanges),
                ChangeSummary = summaryOnly
                    ? result.Changes.GroupBy(c => c.Object).Select(o => $"{o.Key}: {o.Count()} change(s) - " +
                        string.Join(", ", o.GroupBy(c => c.Where).Select(w => w.Count() == 1 ? w.Key : $"{w.Key} x{w.Count()}"))).ToList()
                    : null,
                ReportPath = summaryOnly ? WriteReport(toolName, result) : null,
                Warnings = result.Warnings,
                NotScanned = result.NotScanned,
                Dependents = result.Dependents,
                FailsafeDependents = result.FailsafeDependents,
                InconsistentAfter = result.InconsistentAfter,
                Message = (result.Changes.Count == 0
                    ? "Nothing to change."
                    : result.Applied
                        ? $"{summary} imported.{after} Compile the software to verify. {SaveHint}"
                        : $"Dry run: {summary} would be imported. Nothing was changed; repeat with dryRun=false to apply.") + safety + blind,
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
        [McpServerTool(Name = "ReplaceNetworkText", Title = "Replace in network titles and comments", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Replace texts in the titles and comments of every network of a block (literal, case-sensitive). All pairs are applied in one pass, so '1' to '2' together with '2' to '1' swaps. With scope 'all' also the title and comment of the block and the texts of GRAPH sequences (step titles, transition and sequence comments; not step names, which are interface members). The block is exported, edited and imported over itself. Defaults to a dry run that reports each change and imports nothing. Know-how protected blocks are rejected; fail-safe blocks need allowFailsafe")]
        public static ResponseXmlEdit ReplaceNetworkText(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("blockPath: root-relative path of the block, e.g. Station1/FB_Conveyor")] string blockPath,
            [Description("replacements: 'find=replace' pairs separated by ';', e.g. 'Pump 1=Pump 2; Pump 2=Pump 1'")] string replacements,
            [Description("scope: 'networks' (default) for network titles and comments only, 'all' to include the block title and comment and GRAPH texts")] string scope = "networks",
            [Description("culture: restrict the replacement to one language, e.g. en-US; empty replaces in every language")] string culture = "",
            [Description("dryRun: true (default) only reports what would change; false imports the edited block")] bool dryRun = true,
            [Description("allowFailsafe: true allows importing a fail-safe block, which changes its F-signature")] bool allowFailsafe = false)
        {
            return Guarded(nameof(ReplaceNetworkText), () =>
            {
                var all = scope.Equals("all", StringComparison.OrdinalIgnoreCase);

                if (!all && !scope.Equals("networks", StringComparison.OrdinalIgnoreCase))
                {
                    throw new McpException($"Unknown scope '{scope}'. Use 'networks' or 'all'.");
                }

                var pairs = ParseRenames(replacements, StringComparer.Ordinal);

                return XmlEdited(
                    Portal.EditNetworkTexts(softwarePath, blockPath,
                        doc => SimaticMl.ReplaceNetworkText(doc, pairs, culture, all), dryRun, allowFailsafe),
                    dryRun);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "RenameMembers", Title = "Rename members of a block or type", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Rename members of a block interface, a data block or a PLC data type, together with the references to them in all blocks. Openness has no setter for member names, so every block and type is exported, the affected ones are edited and imported back. Defaults to a dry run that lists the affected objects, every change and what could not be resolved, and imports nothing. Several renames in one call are applied together, so a swap is possible. Every block that reads a data block built from the owner turns inconsistent, also one that reads another member; they are listed as Dependents, and fail-safe blocks among them (FailsafeDependents) need allowFailsafe because the safety program must then be compiled again. Not updated: HMI tags, watch tables, technology objects. Start values and current values of a renamed member may be reset - back them up first")]
        public static ResponseXmlEdit RenameMembers(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("ownerPath: root-relative path of the block or PLC data type that declares the members")] string ownerPath,
            [Description("kind: 'block' when ownerPath is a block (FB, FC, OB, DB), 'type' when it is a PLC data type")] string kind,
            [Description("renames: 'memberPath=newName' pairs separated by ';', e.g. 'Speed=Velocity; Config.Limit=MaxLimit'. The path is relative to the owner, nested struct members separated by '.'")] string renames,
            [Description("dryRun: true (default) only reports what would change; false imports the edited objects")] bool dryRun = true,
            [Description("allowFailsafe: true allows importing fail-safe blocks, which changes their F-signature")] bool allowFailsafe = false,
            [Description("summaryOnly: true returns change counts per object and network instead of every change, and writes the complete result to a JSON file whose path is returned")] bool summaryOnly = false)
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
                    dryRun, summaryOnly, nameof(RenameMembers));
            });
        }

        /// <summary>"A=B; C.D=E" to { "A": "B", "C.D": "E" }.</summary>
        /// <param name="comparer">Names compare without case (the default); texts to replace are passed with Ordinal.</param>
        public static IDictionary<string, string> ParseRenames(string renames, StringComparer? comparer = null)
        {
            // shortcut: pairs are split at ';' and '=', so neither side can contain those two characters; a quoting rule if that is ever needed.
            var result = new Dictionary<string, string>(comparer ?? StringComparer.OrdinalIgnoreCase);

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

        #region batch rename, tag comments, compile

        [McpServerTool(Name = "GetTechnologicalObjects", Title = "Get technology objects", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the technology objects (PID_Temp, axes, ...) of the PLC software with name, data block number, type and version - enough to check which name belongs to which number after a rename")]
        public static ResponseTechnologicalObjects GetTechnologicalObjects(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("attributes: true adds every attribute Openness reports for each object (about thirty); default false")] bool attributes = false)
        {
            try
            {
                var items = Portal.GetTechnologicalObjects(softwarePath)
                    .Select(t =>
                    {
                        var all = Helper.GetAttributeList(t);

                        string? Read(string name) =>
                            all.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))?.Value?.ToString();

                        return new TechnologicalObjectInfo
                        {
                            Name = t.Name,
                            Number = t.Number,
                            Type = Read("OfSystemLibElement"),
                            Version = Read("OfSystemLibVersion"),
                            Attributes = attributes ? all : null
                        };
                    })
                    .OrderBy(t => t.Number)
                    .ToList();

                return new ResponseTechnologicalObjects
                {
                    Message = $"{items.Count} technology object(s) in '{softwarePath}'",
                    Items = items,
                    Meta = Ok(new JsonObject { ["count"] = items.Count })
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error listing the technology objects of '{softwarePath}': {ex.Message}", ex);
            }
        }

        private static ResponseBatchRename BatchRenamed(BatchRenameResult result, bool dryRun, string kind, bool summaryOnly, string toolName)
        {
            return new ResponseBatchRename
            {
                DryRun = dryRun,
                Applied = result.Applied,
                ItemCount = result.Items.Count,
                Items = summaryOnly ? result.Items.Where(i => i.Failsafe || i.UsedByFailsafe.Count > 0).ToList() : result.Items,
                ReportPath = summaryOnly ? WriteReport(toolName, result) : null,
                Warnings = result.Warnings,
                Message = result.Applied
                    ? $"{result.Items.Count} {kind} renamed. Blocks that refer to them are inconsistent until compiled. {SaveHint}"
                    : $"Dry run: {result.Items.Count} {kind} would be renamed. Nothing was changed; repeat with dryRun=false to apply.",
                Meta = result.Applied ? OkMeta() : Ok()
            };
        }

        [WriteTool]
        [McpServerTool(Name = "RenameTags", Title = "Rename tags in a batch", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Rename many PLC tags in one transaction, by name across all tag tables. A new name may be the current name of another tag of the batch, so a swap or rotation needs no temporary names. Defaults to a dry run that lists each tag with its table, how many objects use it and which fail-safe blocks use it, and renames nothing. Tags used by fail-safe blocks need allowFailsafe; a name taken by a tag outside the batch is rejected. HMI tags that name a PLC tag are not updated")]
        public static ResponseBatchRename RenameTags(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("renames: 'currentName=newName' pairs separated by ';', e.g. 'iPump1_Run=iPump2_Run; iPump2_Run=iPump1_Run'")] string renames,
            [Description("dryRun: true (default) only reports what would change; false renames the tags")] bool dryRun = true,
            [Description("allowFailsafe: true allows renaming tags that fail-safe blocks use, which changes the safety program")] bool allowFailsafe = false,
            [Description("summaryOnly: true returns only the tags that touch the safety program plus counts and warnings, and writes the complete result to a JSON file whose path is returned")] bool summaryOnly = false)
        {
            return Guarded(nameof(RenameTags), () =>
                BatchRenamed(Portal.RenameTags(softwarePath, ParseRenames(renames), dryRun, allowFailsafe), dryRun, "tag(s)", summaryOnly, nameof(RenameTags)));
        }

        [WriteTool]
        [McpServerTool(Name = "RenameObjects", Title = "Rename blocks, TOs and types in a batch", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Rename many blocks, technology objects and PLC data types in one transaction, by name. A new name may be the current name of another object of the batch, so a swap needs no temporary names. Defaults to a dry run that lists each object with its kind, path and the fail-safe blocks that use it, and renames nothing. Fail-safe blocks and objects used by them need allowFailsafe; know-how protected objects and names taken outside the batch are rejected. Callers of a renamed block are inconsistent until compiled")]
        public static ResponseBatchRename RenameObjects(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("renames: 'currentName=newName' pairs separated by ';', e.g. 'PID_1=PID_2; PID_2=PID_1'")] string renames,
            [Description("dryRun: true (default) only reports what would change; false renames the objects")] bool dryRun = true,
            [Description("allowFailsafe: true allows renaming fail-safe blocks and objects they use, which changes the safety program")] bool allowFailsafe = false,
            [Description("summaryOnly: true returns only the objects that touch the safety program plus counts and warnings, and writes the complete result to a JSON file whose path is returned")] bool summaryOnly = false)
        {
            return Guarded(nameof(RenameObjects), () =>
                BatchRenamed(Portal.RenameObjects(softwarePath, ParseRenames(renames), dryRun, allowFailsafe), dryRun, "object(s)", summaryOnly, nameof(RenameObjects)));
        }

        private static ResponseXmlEdit CommentsEdited(List<XmlChange> changes, bool dryRun)
        {
            return new ResponseXmlEdit
            {
                DryRun = dryRun,
                Applied = !dryRun && changes.Count > 0,
                ChangeCount = changes.Count,
                Changes = changes.Take(MaxReportedChanges),
                Message = changes.Count == 0
                    ? "Nothing to change."
                    : dryRun
                        ? $"Dry run: {changes.Count} tag comment(s) would change. Nothing was changed; repeat with dryRun=false to apply."
                        : $"{changes.Count} tag comment(s) changed. {SaveHint}",
                Meta = dryRun || changes.Count == 0 ? Ok() : OkMeta()
            };
        }

        [WriteTool]
        [McpServerTool(Name = "SetTagComment", Title = "Set tag comment", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Set the comment of one PLC tag, which 'UpdateTag' cannot do. A comment has one text per project language")]
        public static ResponseXmlEdit SetTagComment(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("tagPath: path of the tag including its table, e.g. TagGroup1/Table1/Tag_1")] string tagPath,
            [Description("comment: the new comment text")] string comment,
            [Description("culture: language of the text, e.g. en-US; may stay empty when the project has one language")] string culture = "")
        {
            return Guarded(nameof(SetTagComment), () => CommentsEdited(Portal.SetTagComment(softwarePath, tagPath, comment, culture), false));
        }

        [WriteTool]
        [McpServerTool(Name = "ReplaceTagComments", Title = "Replace in tag comments", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Replace texts in the comments of the PLC tags of one tag table, or of all tables (literal, case-sensitive). All pairs are applied in one pass, so '1' to '2' together with '2' to '1' swaps. Defaults to a dry run that reports each change and writes nothing")]
        public static ResponseXmlEdit ReplaceTagComments(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("replacements: 'find=replace' pairs separated by ';', e.g. 'Pump 1=Pump 2; Pump 2=Pump 1'")] string replacements,
            [Description("tagTablePath: root-relative path of the tag table; empty covers every tag table of the PLC")] string tagTablePath = "",
            [Description("culture: restrict the replacement to one language, e.g. en-US; empty replaces in every language")] string culture = "",
            [Description("dryRun: true (default) only reports what would change; false writes the comments")] bool dryRun = true)
        {
            return Guarded(nameof(ReplaceTagComments), () =>
            {
                var pairs = ParseRenames(replacements, StringComparer.Ordinal);

                return CommentsEdited(
                    Portal.EditTagComments(softwarePath, tagTablePath, text => SimaticMl.ReplaceAll(text, pairs), culture, dryRun), dryRun);
            });
        }

        [WriteTool]
        [McpServerTool(Name = "RenameWatchTableGroup", Title = "Rename watch table group", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename a user group below 'Watch and force tables'. The tables inside keep their names; a sibling group of the new name is rejected")]
        public static ResponseRenamed RenameWatchTableGroup(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("groupPath: root-relative path of the group, e.g. Station1/Commissioning")] string groupPath,
            [Description("newName: the new group name, without a slash")] string newName)
        {
            return Guarded(nameof(RenameWatchTableGroup), () =>
            {
                Portal.RenameWatchTableGroup(softwarePath, groupPath, newName);
                return Renamed("Watch table group", groupPath, newName, ReplaceLeaf(groupPath, newName));
            });
        }

        [McpServerTool(Name = "CompileBlock", Title = "Compile block or block group", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Compile one block, or every block of one block group, instead of the whole PLC software - enough to make the callers of a renamed block consistent again. A fail-safe block, or a group that contains one, is refused: compiling the safety program stays with the operator. Warnings are reported as a successful compile; only errors fail the call. The message names the blocks that are still inconsistent afterwards, which then need a compile of the whole software")]
        public static ResponseCompileSoftware CompileBlock(
            [Description("softwarePath: defines the path in the project structure to the PLC software")] string softwarePath,
            [Description("path: root-relative path of a block or of a block group, e.g. Station1/FB_Conveyor or Station1")] string path)
        {
            try
            {
                var (result, inconsistent, others) = Portal.CompileBlocks(softwarePath, path);
                var messages = FlattenCompilerMessages(result.Messages, 0).ToList();
                var state = result.State.ToString();

                if (result.State == CompilerResultState.Error)
                {
                    throw new McpException(
                        $"Compiling '{path}' failed with {result.ErrorCount} error(s). " +
                        string.Join(" | ", messages
                            .Where(m => string.Equals(m.State, nameof(CompilerResultState.Error), StringComparison.OrdinalIgnoreCase))
                            .Take(5)
                            .Select(m => $"{m.Path}: {m.Description}")));
                }

                return new ResponseCompileSoftware
                {
                    Message = $"Compiling '{path}' finished with state '{state}': {result.ErrorCount} error(s), {result.WarningCount} warning(s)" +
                              (inconsistent.Count == 0
                                  ? string.Empty
                                  : $". WARNING: still inconsistent after the compile: {string.Join(", ", inconsistent)} - " +
                                    "something they depend on needs compiling too; compile the whole software.") +
                              (others.Count == 0
                                  ? string.Empty
                                  : $" Other inconsistent blocks in the PLC ({others.Count}): {string.Join(", ", others.Take(15))}" +
                                    (others.Count > 15 ? ", ..." : string.Empty) + "."),
                    State = state,
                    ErrorCount = result.ErrorCount,
                    WarningCount = result.WarningCount,
                    Messages = messages,
                    Meta = Ok(new JsonObject
                    {
                        ["state"] = state,
                        ["errorCount"] = result.ErrorCount,
                        ["warningCount"] = result.WarningCount,
                        ["stillInconsistent"] = inconsistent.Count,
                        ["otherInconsistent"] = others.Count
                    })
                };
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error compiling '{path}': {ex.Message}", ex);
            }
        }

        #endregion
    }
}
