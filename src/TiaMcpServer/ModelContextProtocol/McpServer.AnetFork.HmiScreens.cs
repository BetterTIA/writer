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
         Description("Create an empty screen of a WinCC Unified device, at the root or in a screen group (missing groups are created), with an optional size and event scripts (Loaded, Unloaded, Tapped, ContextTapped). A screen name already in use is rejected. Openness offers no copy of a screen and no template, so the items are added with CreateHmiScreenItems. Defaults to a dry run that checks the name and creates nothing")]
        public static ResponseHmiEdit CreateHmiScreen(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: name of the new screen")] string screenName,
            [Description("groupPath: screen group, e.g. Line1/Station2, created when it does not exist; empty creates the screen at the root")] string groupPath = "",
            [Description("width: screen width in pixels; 0 keeps the device default")] uint width = 0,
            [Description("height: screen height in pixels; 0 keeps the device default")] uint height = 0,
            [Description("events: JSON object of event name to JavaScript, e.g. {\"Loaded\":\"Tags('Page').Write(1);\"}; empty adds none")] string events = "",
            [Description("dryRun: true (default) only checks the name; false creates the screen")] bool dryRun = true)
        {
            return Guarded(nameof(CreateHmiScreen), () =>
                HmiEdited(Portal.CreateHmiScreen(softwarePath, screenName, groupPath, width, height, HmiScreenSpecs.ParseEvents(events), dryRun), dryRun, "screen(s)"));
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
         Description("Create items on a screen of a WinCC Unified device, many in one call: any item class of WinCC Unified by its name without the 'Hmi' prefix (Button, IOField, Text, Label, Rectangle, Circle, Ellipse, Line, Bar, Slider, ToggleSwitch, GraphicView, ScreenWindow, AlarmControl, WebControl, Clock...) and Faceplate with faceplateType, with position, size, text, further properties by their Openness name or dotted path (Font.Size, Font.Weight, Interface.Speed.Value), dynamizations and event scripts. items is a JSON array, e.g. [{\"type\":\"Label\",\"name\":\"lblState\",\"left\":10,\"top\":10,\"width\":120,\"height\":30,\"text\":\"State\",\"properties\":{\"BackColor\":\"#2E7D32\",\"Font.Size\":\"16\",\"Font.Weight\":\"Bold\"},\"dynamizations\":{\"Visible\":\"Ready\",\"BackColor\":{\"script\":\"return Tags('State').Read() == 1 ? 0xFF2E7D32 : 0xFF9E9E9E;\",\"trigger\":\"T1s\"}}}]. A dynamization is a tag name, or an object with tag (plus indirect or resourceList) or script (plus trigger). A circle takes its diameter as width and is inscribed at left/top. Defaults to a dry run that checks every item against the Openness type. Items whose name is taken, whose tag does not exist or whose property, dynamization or event the type does not have are reported and skipped; an item TIA Portal refuses is removed again")]
        public static ResponseHmiBatch CreateHmiScreenItems(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: existing screen that receives the items")] string screenName,
            [Description("items: JSON array of items with type, name, left, top, width, height and optional faceplateType, text, properties, dynamizations, events")] string items,
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

        [WriteTool]
        [McpServerTool(Name = "SetHmiItemDynamization", Title = "Set dynamization of HMI screen item", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Set the dynamization of one property of a screen item of a WinCC Unified device, or of the screen itself, replacing the one it had: a tag, a tag through a resource list (text or graphic list), or a script whose return value the property takes, e.g. a colour or the visibility by the state of a tag. A dotted path reaches into a part, e.g. Font.Size. The script is syntax checked and the result reported. Defaults to a dry run that reports the old and the new dynamization")]
        public static ResponseHmiEdit SetHmiItemDynamization(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("screenName: name of the screen")] string screenName,
            [Description("itemName: name of the screen item; empty addresses the screen itself")] string itemName,
            [Description("property: the property to dynamize, e.g. Visible, BackColor, Font.Size")] string property,
            [Description("dynamization: a tag name, or JSON {\"tag\":\"X\",\"indirect\":true} or {\"tag\":\"X\",\"resourceList\":\"L\"} or {\"script\":\"return ...;\",\"trigger\":\"T1s\"}; trigger is a TriggerType name such as T100ms, T1s, AutomaticTags; a tag dynamization also takes \"formula\":\"x * 0.1\" (turns the value converter formula on) and \"formulaSelected\":false (turns it off)")] string dynamization,
            [Description("dryRun: true (default) only reports what would change; false sets the dynamization")] bool dryRun = true)
        {
            return Guarded(nameof(SetHmiItemDynamization), () =>
                HmiEdited(Portal.SetHmiItemDynamization(softwarePath, screenName, itemName, property, HmiScreenSpecs.ParseDynamization(dynamization, $"dynamization of '{property}'"), dryRun), dryRun, "dynamization(s)"));
        }

        #endregion

        #region hmi text lists and runtime settings

        [WriteTool]
        [McpServerTool(Name = "ExportHmiTextLists", Title = "Export HMI text lists to a file", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Export the text lists of a WinCC Unified device to a file in a folder, as Openness writes it. Openness cannot create a text list or its entries, so a new list is made by editing such a file and importing it with ImportHmiTextLists")]
        public static ResponseHmiEdit ExportHmiTextLists(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("folder: absolute folder for the file (a relative one is refused); created when it does not exist")] string folder,
            [Description("fileName: name of the file, e.g. TextLists")] string fileName,
            [Description("dryRun: false (default) writes the file; true only reports the target and the existing files it would overwrite, and creates nothing")] bool dryRun = false)
        {
            return Guarded(nameof(ExportHmiTextLists), () => HmiEdited(Portal.ExportHmiTextLists(softwarePath, folder, fileName, dryRun), dryRun, "file(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "ImportHmiTextLists", Title = "Import HMI text lists from a file", Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Import text lists into a WinCC Unified device from a file in the format of ExportHmiTextLists, e.g. lists that map the value of a tag to a text for a resource list dynamization. Defaults to a dry run that checks the file and counts the text lists the device has now")]
        public static ResponseHmiEdit ImportHmiTextLists(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("folder: absolute folder that holds the file")] string folder,
            [Description("fileName: name of the file as ExportHmiTextLists wrote it")] string fileName,
            [Description("dryRun: true (default) only checks the file; false imports it")] bool dryRun = true)
        {
            return Guarded(nameof(ImportHmiTextLists), () => HmiEdited(Portal.ImportHmiTextLists(softwarePath, folder, fileName, dryRun), dryRun, "file(s)"));
        }

        [WriteTool]
        [McpServerTool(Name = "SetHmiRuntimeSetting", Title = "Set an HMI runtime setting", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Set one runtime setting of a WinCC Unified device, e.g. StartScreen (the screen must exist). An attribute that is no writable runtime setting is rejected with the list of the settable ones. Defaults to a dry run that reports the old and the new value")]
        public static ResponseHmiEdit SetHmiRuntimeSetting(
            [Description("softwarePath: defines the path in the project structure to the HMI software")] string softwarePath,
            [Description("attribute: name of the runtime setting as Openness calls it, e.g. StartScreen")] string attribute,
            [Description("value: the new value, e.g. the name of the start screen")] string value,
            [Description("dryRun: true (default) only reports what would change; false sets the value")] bool dryRun = true)
        {
            return Guarded(nameof(SetHmiRuntimeSetting), () => HmiEdited(Portal.SetHmiRuntimeSetting(softwarePath, attribute, value, dryRun), dryRun, "setting(s)"));
        }

        #endregion
    }
}
