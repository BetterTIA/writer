using ModelContextProtocol.Server;
using System.ComponentModel;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpPrompts
    {
        // ANet fork additions: one prompt per tool of McpServer.AnetFork.HmiScreens.cs, as Test_708
        // and Test_709 require.

        #region ANet fork HMI screen templates

        [McpServerPrompt(Name = "CreateHmiScreen"), Description("Create an empty HMI screen")]
        public static string CreateHmiScreen(string softwarePath, string screenName, string groupPath = "", string width = "0", string height = "0", string events = "", string dryRun = "true")
        {
            return $@"Create an empty screen of a WinCC Unified device (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: name of the new screen
- groupPath: screen group, created when it does not exist, empty for the root
- width, height: size in pixels, 0 keeps the device default
- events: JSON object of event name (Loaded, Unloaded, Tapped, ContextTapped) to JavaScript, empty for none
- dryRun: true only checks the name, false creates the screen

Use the CreateHmiScreen tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- groupPath: {groupPath}
- width: {width}
- height: {height}
- events: {events}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "DeleteHmiScreen"), Description("Delete an HMI screen with its items")]
        public static string DeleteHmiScreen(string softwarePath, string screenName, string dryRun = "true")
        {
            return $@"Delete a screen of a WinCC Unified device together with every item on it (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: name of the screen
- dryRun: true only reports the items and the script lines that name the screen, false deletes it

Use the DeleteHmiScreen tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "CreateHmiScreenItems"), Description("Create buttons, IO fields, labels, shapes, windows and controls on an HMI screen")]
        public static string CreateHmiScreenItems(string softwarePath, string screenName, string items, string culture = "", string dryRun = "true")
        {
            return $@"Create items on a screen of a WinCC Unified device, many in one call (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: existing screen that receives the items
- items: JSON array, each item with type (an item class without 'Hmi', e.g. Button, IOField, Label, Rectangle, ScreenWindow, AlarmControl, or Faceplate with faceplateType), name, left, top, width, height and optional text, properties (also dotted paths such as Font.Size), dynamizations (a tag name, or an object with tag or script), events
- culture: language of the texts, may stay empty when the project has one language
- dryRun: true only checks every item, false creates the items

Use the CreateHmiScreenItems tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- items: {items}
- culture: {culture}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "DeleteHmiScreenItems"), Description("Delete items of an HMI screen by name, in a batch")]
        public static string DeleteHmiScreenItems(string softwarePath, string screenName, string names, string dryRun = "true")
        {
            return $@"Delete items of one screen of a WinCC Unified device by name (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: name of the screen
- names: item names separated by ';'
- dryRun: true only reports the items, false deletes them

Use the DeleteHmiScreenItems tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- names: {names}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "SetHmiItemDynamization"), Description("Dynamize a property of an HMI screen item by a tag, a resource list or a script")]
        public static string SetHmiItemDynamization(string softwarePath, string screenName, string itemName, string property, string dynamization, string dryRun = "true")
        {
            return $@"Set the dynamization of one property of a screen item of a WinCC Unified device, replacing the one it had (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: name of the screen
- itemName: name of the screen item; empty addresses the screen itself
- property: the property to dynamize, e.g. Visible, BackColor, Font.Size
- dynamization: a tag name, or JSON with tag (plus indirect, resourceList, or formula and formulaSelected) or with script (plus trigger, e.g. T1s)
- dryRun: true only reports what would change; false sets the dynamization

Use the SetHmiItemDynamization tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}
- property: {property}
- dynamization: {dynamization}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "ExportHmiTextLists"), Description("Export the text lists of an HMI to a file")]
        public static string ExportHmiTextLists(string softwarePath, string folder, string fileName)
        {
            return $@"Export the text lists of a WinCC Unified device to a file; edit it and import it with ImportHmiTextLists to add lists (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- folder: absolute folder for the file
- fileName: name of the file

Use the ExportHmiTextLists tool with these parameters:
- softwarePath: {softwarePath}
- folder: {folder}
- fileName: {fileName}";
        }

        [McpServerPrompt(Name = "ImportHmiTextLists"), Description("Import HMI text lists from a file")]
        public static string ImportHmiTextLists(string softwarePath, string folder, string fileName, string dryRun = "true")
        {
            return $@"Import text lists into a WinCC Unified device from a file in the format of ExportHmiTextLists (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- folder: absolute folder that holds the file
- fileName: name of the file
- dryRun: true only checks the file; false imports it

Use the ImportHmiTextLists tool with these parameters:
- softwarePath: {softwarePath}
- folder: {folder}
- fileName: {fileName}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "SetHmiRuntimeSetting"), Description("Set a runtime setting of an HMI, e.g. the start screen")]
        public static string SetHmiRuntimeSetting(string softwarePath, string attribute, string value, string dryRun = "true")
        {
            return $@"Set one runtime setting of a WinCC Unified device (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- attribute: name of the runtime setting, e.g. StartScreen
- value: the new value
- dryRun: true only reports what would change; false sets the value

Use the SetHmiRuntimeSetting tool with these parameters:
- softwarePath: {softwarePath}
- attribute: {attribute}
- value: {value}
- dryRun: {dryRun}";
        }

        #endregion
    }
}
