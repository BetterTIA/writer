using ModelContextProtocol.Server;
using System.ComponentModel;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpPrompts
    {
        // ANet fork additions: one prompt per tool of McpServer.AnetFork.Hmi.cs, as Test_708 and
        // Test_709 require.

        #region ANet fork WinCC Unified templates

        [McpServerPrompt(Name = "GetHmiTags"), Description("List the HMI tags of a WinCC Unified device")]
        public static string GetHmiTags(string softwarePath, string regexName = "", string unboundOnly = "false")
        {
            return $@"List the HMI tags of a WinCC Unified device with connection, PLC tag and address. 'unbound' marks tags that have a connection but lost their PLC tag.

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- regexName: optional regular expression on the HMI tag name
- unboundOnly: true lists only the tags whose binding is lost

Use the GetHmiTags tool with these parameters:
- softwarePath: {softwarePath}
- regexName: {regexName}
- unboundOnly: {unboundOnly}";
        }

        [McpServerPrompt(Name = "SetHmiTagPlcTags"), Description("Point HMI tags at PLC tags or data block members")]
        public static string SetHmiTagPlcTags(string softwarePath, string assignments, string plcSoftwarePath = "", string dryRun = "true")
        {
            return $@"Point HMI tags of a WinCC Unified device at PLC tags or data block members. Run it with dryRun true first, show the old and new PLC paths, and only then repeat with dryRun false (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- assignments: 'hmiTag=plcPath' pairs separated by ';', e.g. 'Pump1_Run=DB_Pumps.Pump1.Run'
- plcSoftwarePath: path to the PLC software the tags belong to, for checking the paths; empty skips the check
- dryRun: true only reports what would change; false sets the PLC tags

Use the SetHmiTagPlcTags tool with these parameters:
- softwarePath: {softwarePath}
- assignments: {assignments}
- plcSoftwarePath: {plcSoftwarePath}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "RenameHmiTags"), Description("Rename many HMI tags at once, swaps included")]
        public static string RenameHmiTags(string softwarePath, string renames, string dryRun = "true")
        {
            return $@"Rename many HMI tags of a WinCC Unified device in one transaction. Run it with dryRun true first and show where the old names are used; script code that names a tag as a string is only listed and has to be changed with ReplaceInHmiScripts (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- renames: 'currentName=newName' pairs separated by ';', e.g. 'Pump1_Run=Pump2_Run; Pump2_Run=Pump1_Run'
- dryRun: true only reports what would change; false renames the tags

Use the RenameHmiTags tool with these parameters:
- softwarePath: {softwarePath}
- renames: {renames}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "RenameHmiScreen"), Description("Rename a screen of a WinCC Unified device")]
        public static string RenameHmiScreen(string softwarePath, string screenName, string newName, string dryRun = "true")
        {
            return $@"Rename a screen of a WinCC Unified device. Run it with dryRun true first and show the script lines that hold the screen name as a string; TIA Portal does not rewrite those (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: current name of the screen
- newName: the new screen name
- dryRun: true only reports what would change; false renames the screen

Use the RenameHmiScreen tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- newName: {newName}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "RenameHmiScreens"), Description("Rename many screens at once, swaps included")]
        public static string RenameHmiScreens(string softwarePath, string renames, string dryRun = "true", string rewriteScripts = "false")
        {
            return $@"Rename many screens of a WinCC Unified device with one walk over the screens. Run it with dryRun true first and show the script lines that hold an old screen name as a string; TIA Portal does not rewrite those (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- renames: 'currentName=newName' pairs separated by ';', e.g. 'Station1=Station2; Station2=Station1'
- dryRun: true only reports what would change; false renames the screens
- rewriteScripts: true also writes the new names into the script lines; false only lists them

Use the RenameHmiScreens tool with these parameters:
- softwarePath: {softwarePath}
- renames: {renames}
- dryRun: {dryRun}
- rewriteScripts: {rewriteScripts}";
        }

        [McpServerPrompt(Name = "ReplaceInHmiScripts"), Description("Replace text in the scripts of a WinCC Unified device")]
        public static string ReplaceInHmiScripts(string softwarePath, string replacements, string screenName = "", string dryRun = "true")
        {
            return $@"Replace text in the script code of event handlers and script dynamizations of a WinCC Unified device (literal, case-sensitive, all pairs in one pass). Run it with dryRun true first, show the changed lines, and only then repeat with dryRun false (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- replacements: 'find=replace' pairs separated by ';'; include the quotes to hit only whole names
- screenName: restrict the replacement to one screen; empty covers every screen
- dryRun: true only reports what would change; false writes the scripts

Use the ReplaceInHmiScripts tool with these parameters:
- softwarePath: {softwarePath}
- replacements: {replacements}
- screenName: {screenName}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "SetHmiItemAttribute"), Description("Set an attribute of a screen item of a WinCC Unified device")]
        public static string SetHmiItemAttribute(string softwarePath, string screenName, string itemName, string attribute, string value, string culture = "", string dryRun = "true")
        {
            return $@"Set one attribute of a screen item of a WinCC Unified device, or of the screen itself: its name, a text, a graphic or another simple property. Run it with dryRun true first (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: name of the screen
- itemName: name of the screen item; empty addresses the screen itself
- attribute: name of the attribute as Openness calls it, e.g. Name, Text, Graphic, Visible
- value: the new value
- culture: language of a text attribute, e.g. en-US; may stay empty when the project has one language
- dryRun: true only reports what would change; false sets the attribute

Use the SetHmiItemAttribute tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}
- attribute: {attribute}
- value: {value}
- culture: {culture}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "SetHmiDynamizationTag"), Description("Point a tag dynamization of a screen item at another HMI tag")]
        public static string SetHmiDynamizationTag(string softwarePath, string screenName, string itemName, string property, string tag, string dryRun = "true")
        {
            return $@"Point the tag dynamization of one property of a screen item of a WinCC Unified device at another HMI tag. Run it with dryRun true first (requires the server started with '--allow-write').

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software
- screenName: name of the screen
- itemName: name of the screen item
- property: the dynamized property, e.g. ProcessValue, Visible, BackColor
- tag: name of the HMI tag to use
- dryRun: true only reports what would change; false sets the tag

Use the SetHmiDynamizationTag tool with these parameters:
- softwarePath: {softwarePath}
- screenName: {screenName}
- itemName: {itemName}
- property: {property}
- tag: {tag}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "CompileHmi"), Description("Compile a WinCC Unified device and list the compiler messages")]
        public static string CompileHmi(string softwarePath)
        {
            return $@"Compile a WinCC Unified device and report every compiler message.

Common parameter values:
- softwarePath: defines the path in the project structure to the HMI software

Use the CompileHmi tool with these parameters:
- softwarePath: {softwarePath}";
        }

        #endregion
    }
}
