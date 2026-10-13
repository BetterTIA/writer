using ModelContextProtocol;
using ModelContextProtocol.Server;
using Siemens.Engineering.Compiler;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseHmiTags : ResponseMessage
    {
        public IEnumerable<HmiTagInfo>? Items { get; set; }

        /// <summary>Number of tags with a connection but neither a PLC tag nor an address.</summary>
        public int UnboundCount { get; set; }
    }

    /// <summary>Result of a WinCC Unified edit, for a dry run as well as for the real run.</summary>
    public class ResponseHmiEdit : ResponseMessage
    {
        public bool DryRun { get; set; }

        public bool Applied { get; set; }

        public int ChangeCount { get; set; }

        /// <summary>The first changes, old value against new value; ChangeCount has the total.</summary>
        public IEnumerable<XmlChange>? Changes { get; set; }

        /// <summary>Where the renamed names are used: tag dynamizations and script lines.</summary>
        public IEnumerable<HmiUsage>? Usages { get; set; }

        public IEnumerable<string>? Warnings { get; set; }
    }

    public static partial class McpServer
    {
        // ANet fork additions for WinCC Unified (branch 'anet'): the tools for
        // Portal.AnetFork.Hmi.cs. softwarePath names the HMI software here, not a PLC.
        //
        // Callers: Program.BuildTools(); the write tools only under '--allow-write'. Affected API:
        // additive only. Every write tool defaults to dryRun = true and reports old against new.

        #region hmi

        private static ResponseHmiEdit HmiEdited(HmiEditResult result, bool dryRun, string what)
        {
            return new ResponseHmiEdit
            {
                DryRun = dryRun,
                Applied = result.Applied,
                ChangeCount = result.Changes.Count,
                Changes = result.Changes.Take(MaxReportedChanges),
                Usages = result.Usages.Take(MaxReportedChanges),
                Warnings = result.Warnings,
                Message = result.Changes.Count == 0
                    ? "Nothing to change."
                    : result.Applied
                        ? $"{result.Changes.Count} {what} changed. {SaveHint}"
                        : $"Dry run: {result.Changes.Count} {what} would change. Nothing was changed; repeat with dryRun=false to apply.",
                Meta = result.Applied ? OkMeta() : Ok()
            };
        }

        private static T Read<T>(string what, Func<T> body)
        {
            try
            {
                return body();
            }
            catch (PortalException pex)
            {
                throw new McpException(pex.Message, pex);
            }
            catch (Exception ex) when (ex is not McpException)
            {
                throw new McpException($"Unexpected error {what}: {ex.Message}", ex);
            }
        }

        [McpServerTool(Name = "GetHmiTags", Title = "Get HMI tags", ReadOnly = true, OpenWorld = false, UseStructuredContent = true),
         Description("List the HMI tags of a WinCC Unified device with table, connection, PLC tag, address and data type. 'unbound' marks a tag that has a connection but neither a PLC tag nor an address - the state a tag is left in when the PLC member it pointed at was renamed")]
        public static ResponseHmiTags GetHmiTags(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("regexName: optional regular expression on the HMI tag name")] string regexName = "",
            [Description("unboundOnly: true lists only the tags whose binding is lost")] bool unboundOnly = false)
        {
            return Read("listing the HMI tags", () =>
            {
                var tags = Portal.GetHmiTags(softwarePath, regexName);
                var unbound = tags.Count(t => t.Unbound);
                var items = unboundOnly ? tags.Where(t => t.Unbound).ToList() : tags;

                return new ResponseHmiTags
                {
                    Message = $"{tags.Count} HMI tag(s) in '{softwarePath}', {unbound} with a connection but no PLC tag or address",
                    Items = items,
                    UnboundCount = unbound,
                    Meta = Ok(new JsonObject { ["count"] = tags.Count, ["unbound"] = unbound })
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "SetHmiTagPlcTags", Title = "Set PLC tag of HMI tags", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Point HMI tags of a WinCC Unified device at PLC tags or data block members, many in one call. Defaults to a dry run that reports the old and the new PLC path of each tag and changes nothing. With plcSoftwarePath every path is checked to start at a tag or block of that PLC; the members after it are not checked. A path TIA Portal does not accept shows as a warning after the real run")]
        public static ResponseHmiEdit SetHmiTagPlcTags(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("assignments: 'hmiTag=plcPath' pairs separated by ';', e.g. 'Pump1_Run=DB_Pumps.Pump1.Run; Level=LevelSensor'")] string assignments,
            [Description("plcSoftwarePath: path to the PLC software the tags belong to, for checking the paths; empty skips the check")] string plcSoftwarePath = "",
            [Description("dryRun: true (default) only reports what would change; false sets the PLC tags")] bool dryRun = true)
        {
            return Guarded(nameof(SetHmiTagPlcTags), () =>
                HmiEdited(Portal.SetHmiTagPlcTags(softwarePath, ParseRenames(assignments), plcSoftwarePath, dryRun), dryRun, "HMI tag binding(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "RenameHmiTags", Title = "Rename HMI tags in a batch", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Rename many HMI tags of a WinCC Unified device in one transaction; a new name may be the current name of another tag of the batch, so a swap needs no temporary names. Defaults to a dry run that also lists where the old names are used: tag dynamizations of screen items, and script lines that hold the name as a string (SetBitInTag, InvertBitInTag, Tags(...)). Tag dynamizations are pointed at the new name; script code is only listed - change it with ReplaceInHmiScripts")]
        public static ResponseHmiEdit RenameHmiTags(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("renames: 'currentName=newName' pairs separated by ';', e.g. 'Pump1_Run=Pump2_Run; Pump2_Run=Pump1_Run'")] string renames,
            [Description("dryRun: true (default) only reports what would change; false renames the tags")] bool dryRun = true,
            [Description("updateDynamizations: true (default) points tag dynamizations that still carry an old name at the new one")] bool updateDynamizations = true)
        {
            return Guarded(nameof(RenameHmiTags), () =>
                HmiEdited(Portal.RenameHmiTags(softwarePath, ParseRenames(renames), dryRun, updateDynamizations), dryRun, "HMI tag name(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "RenameHmiScreen", Title = "Rename HMI screen", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Rename a screen of a WinCC Unified device. Defaults to a dry run that also lists the script lines holding the screen name as a string (screen changes in screen windows), which TIA Portal does not rewrite - change them with ReplaceInHmiScripts")]
        public static ResponseHmiEdit RenameHmiScreen(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: current name of the screen")] string screenName,
            [Description("newName: the new screen name")] string newName,
            [Description("dryRun: true (default) only reports what would change; false renames the screen")] bool dryRun = true)
        {
            return Guarded(nameof(RenameHmiScreen), () =>
                HmiEdited(Portal.RenameHmiScreens(softwarePath, new Dictionary<string, string> { [screenName] = newName }, dryRun), dryRun, "screen name(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "RenameHmiScreens", Title = "Rename HMI screens in a batch", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Rename many screens of a WinCC Unified device with one walk over the screens (a walk takes minutes, so prefer this over repeated RenameHmiScreen); a new name may be the current name of another screen of the batch, so a swap needs no temporary names. Defaults to a dry run that also lists the script lines holding an old screen name as a string, which TIA Portal does not rewrite; rewriteScripts writes the new names into those lines in the same call")]
        public static ResponseHmiEdit RenameHmiScreens(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("renames: 'currentName=newName' pairs separated by ';', e.g. 'Station1=Station2; Station2=Station1'")] string renames,
            [Description("dryRun: true (default) only reports what would change; false renames the screens")] bool dryRun = true,
            [Description("rewriteScripts: true also replaces the quoted old screen names in script code; false (default) only lists those lines")] bool rewriteScripts = false)
        {
            return Guarded(nameof(RenameHmiScreens), () =>
                HmiEdited(Portal.RenameHmiScreens(softwarePath, ParseRenames(renames), dryRun, rewriteScripts), dryRun, "screen name(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "ReplaceInHmiScripts", Title = "Replace in HMI scripts", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Replace text in the script code of a WinCC Unified device: event handlers and property event handlers of screens and screen items, and script dynamizations (literal, case-sensitive). All pairs are applied in one pass, so a swap is possible. Defaults to a dry run that reports every changed line with screen, item and event, and changes nothing. Script modules are not covered")]
        public static ResponseHmiEdit ReplaceInHmiScripts(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("replacements: 'find=replace' pairs separated by ';', e.g. '\"Pump1_Run\"=\"Pump2_Run\"'. Include the quotes to hit only whole names")] string replacements,
            [Description("screenName: restrict the replacement to one screen; empty covers every screen")] string screenName = "",
            [Description("dryRun: true (default) only reports what would change; false writes the scripts")] bool dryRun = true)
        {
            return Guarded(nameof(ReplaceInHmiScripts), () =>
                HmiEdited(Portal.ReplaceInHmiScripts(softwarePath, ParseRenames(replacements, StringComparer.Ordinal), screenName, dryRun), dryRun, "script line(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "SetHmiItemAttribute", Title = "Set attribute of HMI screen item", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Set one attribute of a screen item of a WinCC Unified device, or of the screen itself: rename it (Name), change a text (Text, ToolTipText - one language), a graphic (Graphic) or any other simple property. Defaults to a dry run that reports the old and the new value")]
        public static ResponseHmiEdit SetHmiItemAttribute(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("itemName: name of the screen item; empty addresses the screen itself")] string itemName,
            [Description("attribute: name of the attribute as Openness calls it, e.g. Name, Text, Graphic, Visible")] string attribute,
            [Description("value: the new value")] string value,
            [Description("culture: language of a text attribute, e.g. en-US; may stay empty when the project has one language")] string culture = "",
            [Description("dryRun: true (default) only reports what would change; false sets the attribute")] bool dryRun = true)
        {
            return Guarded(nameof(SetHmiItemAttribute), () =>
                HmiEdited(Portal.SetHmiItemAttribute(softwarePath, screenName, itemName, attribute, value, culture, dryRun), dryRun, "attribute(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "SetHmiDynamizationTag", Title = "Set tag of HMI dynamization", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Point the tag dynamization of one property of a screen item of a WinCC Unified device at another HMI tag. Defaults to a dry run that reports the old and the new tag")]
        public static ResponseHmiEdit SetHmiDynamizationTag(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("itemName: name of the screen item")] string itemName,
            [Description("property: the dynamized property, e.g. ProcessValue, Visible, BackColor")] string property,
            [Description("tag: name of the HMI tag to use")] string tag,
            [Description("dryRun: true (default) only reports what would change; false sets the tag")] bool dryRun = true)
        {
            return Guarded(nameof(SetHmiDynamizationTag), () =>
                HmiEdited(Portal.SetHmiDynamizationTag(softwarePath, screenName, itemName, property, tag, dryRun), dryRun, "dynamization(s)"));
        }

        [McpServerTool(Name = "CompileHmi", Title = "Compile HMI", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Compile a WinCC Unified device and report every compiler message. The message says which object was compiled: the HMI software offers no compiler itself, so the device item that holds it or one of its parents is used. Warnings are reported as a successful compile; only errors fail the call")]
        public static ResponseCompileSoftware CompileHmi(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath)
        {
            return Read("compiling the HMI", () =>
            {
                var (result, compiled) = Portal.CompileHmi(softwarePath);
                var messages = FlattenCompilerMessages(result.Messages, 0).ToList();
                var state = result.State.ToString();

                if (result.State == CompilerResultState.Error)
                {
                    throw new McpException(
                        $"Compiling {compiled} failed with {result.ErrorCount} error(s). " +
                        string.Join(" | ", messages
                            .Where(m => string.Equals(m.State, nameof(CompilerResultState.Error), StringComparison.OrdinalIgnoreCase))
                            .Take(5)
                            .Select(m => $"{m.Path}: {m.Description}")));
                }

                return new ResponseCompileSoftware
                {
                    Message = $"Compiling {compiled} finished with state '{state}': {result.ErrorCount} error(s), {result.WarningCount} warning(s)",
                    State = state,
                    ErrorCount = result.ErrorCount,
                    WarningCount = result.WarningCount,
                    Messages = messages,
                    Meta = Ok(new JsonObject { ["state"] = state, ["errorCount"] = result.ErrorCount, ["warningCount"] = result.WarningCount })
                };
            });
        }

        #endregion
    }
}
