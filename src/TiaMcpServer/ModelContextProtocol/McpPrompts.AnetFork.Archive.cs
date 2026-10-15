using ModelContextProtocol.Server;
using System.ComponentModel;

namespace TiaMcpServer.ModelContextProtocol
{
    public static partial class McpPrompts
    {
        // ANet fork additions: one prompt per tool of McpServer.AnetFork.Archive.cs, as Test_708
        // and Test_709 require.

        #region ANet fork archive templates

        [McpServerPrompt(Name = "ArchiveProject"), Description("Archive the project as a versioned zap file")]
        public static string ArchiveProject(string archiveDirectory, string version, string kind = "interim", string saveFirst = "false", string dryRun = "true")
        {
            return $@"Archive the open project as a compressed zap file outside the project folder. Ask the operator for the version number first; never invent it. Run it with dryRun true to see the target path, then repeat with dryRun false (requires the server started with '--allow-write').

Common parameter values:
- archiveDirectory: absolute path of the archive folder, outside the project folder
- version: V<major>.<minor>.<patch>, e.g. V1.2.0, confirmed by the operator
- kind: interim (checkpoint with date and time in the name) or full (fully working version)
- saveFirst: true saves unsaved changes before the archive
- dryRun: true only returns the target path, false writes the archive

Use the ArchiveProject tool with these parameters:
- archiveDirectory: {archiveDirectory}
- version: {version}
- kind: {kind}
- saveFirst: {saveFirst}
- dryRun: {dryRun}";
        }

        [McpServerPrompt(Name = "PruneArchives"), Description("Delete interim archives beyond the newest three")]
        public static string PruneArchives(string archiveDirectory, string projectName = "", string dryRun = "true", string delete = "false")
        {
            return $@"Keep every full archive and the newest three interim archives of the project, delete the older interim ones. Run it with dryRun true first and show the list; a real deletion needs dryRun false and delete true (requires the server started with '--allow-write').

Common parameter values:
- archiveDirectory: absolute path of the archive folder
- projectName: empty takes the open project
- dryRun: true only lists what would be deleted
- delete: true, with dryRun false, deletes

Use the PruneArchives tool with these parameters:
- archiveDirectory: {archiveDirectory}
- projectName: {projectName}
- dryRun: {dryRun}
- delete: {delete}";
        }

        #endregion
    }
}
