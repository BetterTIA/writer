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
        public static string CreateHmiScreen(string softwarePath, string screenName, string groupPath = "", string width = "0", string height = "0", string dryRun = "true")
        {
            return $@"Create an empty screen of a WinCC Unified device (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: name of the new screen
- groupPath: existing screen group, empty for the root
- width, height: size in pixels, 0 keeps the device default
- dryRun: true only checks the name, false creates the screen

Use the CreateHmiScreen tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- groupPath: {groupPath}
- width: {width}
- height: {height}
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

        [McpServerPrompt(Name = "CreateHmiScreenItems"), Description("Create buttons, IO fields, texts and shapes on an HMI screen")]
        public static string CreateHmiScreenItems(string softwarePath, string screenName, string items, string culture = "", string dryRun = "true")
        {
            return $@"Create items on a screen of a WinCC Unified device, many in one call (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: existing screen that receives the items
- items: JSON array, each item with type (Button, IOField, Text, Rectangle, Circle), name, left, top, width, height and optional text, properties, dynamizations, events
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

        #endregion
    }
}
