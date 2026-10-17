using ModelContextProtocol.Server;
using System.ComponentModel;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpServer
    {
        // ANet fork additions for WinCC Unified (branch 'anet'): the tools for
        // Portal.AnetFork.HmiScreens.cs. softwarePath names the HMI software, not a PLC.
        //
        // Callers: Program.BuildTools(), only under '--allow-write'. Every tool defaults to
        // dryRun = true and runs in one transaction; a batch reports every item, the ones that
        // failed their check are skipped, and a call where no item can go through fails.

        #region hmi screens

        [WriteTool]
        [McpServerTool(Name = "CreateHmiScreen", Title = "Create an HMI screen", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create an empty screen of a WinCC Unified device, at the root or in an existing screen group, with an optional size. A screen name already in use is rejected. Openness offers no copy of a screen and no template, so the items are added with CreateHmiScreenItems. Defaults to a dry run that checks the name and creates nothing")]
        public static ResponseHmiEdit CreateHmiScreen(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: name of the new screen")] string screenName,
            [Description("groupPath: existing screen group, e.g. Line1/Station2; empty creates the screen at the root")] string groupPath = "",
            [Description("width: screen width in pixels; 0 keeps the device default")] uint width = 0,
            [Description("height: screen height in pixels; 0 keeps the device default")] uint height = 0,
            [Description("dryRun: true (default) only checks the name; false creates the screen")] bool dryRun = true)
        {
            return Guarded(nameof(CreateHmiScreen), () => HmiEdited(Portal.CreateHmiScreen(softwarePath, screenName, groupPath, width, height, dryRun), dryRun, "screen(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiScreen", Title = "Delete an HMI screen", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete a screen of a WinCC Unified device together with every item on it. Defaults to a dry run that reports the item count and the script lines of other screens that name the screen as a string - they are left dangling by the delete")]
        public static ResponseHmiEdit DeleteHmiScreen(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("dryRun: true (default) only reports the items and the script lines; false deletes the screen")] bool dryRun = true)
        {
            return Guarded(nameof(DeleteHmiScreen), () => HmiEdited(Portal.DeleteHmiScreen(softwarePath, screenName, dryRun), dryRun, "screen(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "CreateHmiScreenItems", Title = "Create HMI screen items in a batch", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Create items on a screen of a WinCC Unified device, many in one call: buttons, IO fields, texts, rectangles and circles with position, size, text, further properties by their Openness name, tag dynamizations and event scripts. items is a JSON array, e.g. [{\"type\":\"Button\",\"name\":\"btnStart\",\"left\":10,\"top\":10,\"width\":120,\"height\":40,\"text\":\"Start\",\"properties\":{\"BackColor\":\"#00AA00\"},\"dynamizations\":{\"Visible\":\"Ready\"},\"events\":{\"Tapped\":\"Tags('Start').Write(1);\"}}]. A circle takes its diameter as width and is inscribed at left/top. Defaults to a dry run that checks every item against the Openness type. Items whose name is taken, whose tag does not exist or whose property, dynamization or event the type does not have are reported and skipped; an item TIA Portal refuses is removed again")]
        public static ResponseHmiBatch CreateHmiScreenItems(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: existing screen that receives the items")] string screenName,
            [Description("items: JSON array of items with type (Button, IOField, Text, Rectangle, Circle), name, left, top, width, height and optional text, properties, dynamizations, events")] string items,
            [Description("culture: language of the texts, e.g. en-US; may stay empty when the project has one language")] string culture = "",
            [Description("dryRun: true (default) only reports every item; false creates the items")] bool dryRun = true)
        {
            return Guarded(nameof(CreateHmiScreenItems), () =>
                HmiBatch(Portal.CreateHmiScreenItems(softwarePath, screenName, HmiScreenSpecs.ParseItems(items), culture, dryRun), dryRun, "screen item(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "DeleteHmiScreenItems", Title = "Delete HMI screen items in a batch", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Delete items of one screen of a WinCC Unified device by name, many in one call. Defaults to a dry run that reports each item with its type and the number of its dynamizations and scripts. A name that is no item of the screen is reported and skipped")]
        public static ResponseHmiBatch DeleteHmiScreenItems(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("names: item names separated by ';'")] string names,
            [Description("dryRun: true (default) only reports the items; false deletes them")] bool dryRun = true)
        {
            return Guarded(nameof(DeleteHmiScreenItems), () => HmiBatch(Portal.DeleteHmiScreenItems(softwarePath, screenName, ParseNames(names), dryRun), dryRun, "screen item(s)"));
        }

        #endregion
    }
}
