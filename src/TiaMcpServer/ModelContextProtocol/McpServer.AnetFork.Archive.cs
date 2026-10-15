using ModelContextProtocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using TiaMcpServer.Siemens;

namespace TiaMcpServer.ModelContextProtocol
{
    public class ResponseArchive : ResponseMessage
    {
        public bool DryRun { get; set; }

        public bool Applied { get; set; }

        public string? ProjectName { get; set; }

        /// <summary>'V1.2.3', as confirmed by the operator.</summary>
        public string? Version { get; set; }

        /// <summary>'full' or 'interim'.</summary>
        public string? Kind { get; set; }

        /// <summary>The archive file, written or planned.</summary>
        public string? Path { get; set; }

        public long? SizeBytes { get; set; }

        public bool SavedFirst { get; set; }

        public IEnumerable<string>? Warnings { get; set; }
    }

    public class ResponsePrune : ResponseMessage
    {
        public bool DryRun { get; set; }

        public bool Applied { get; set; }

        public string? Directory { get; set; }

        public string? ProjectName { get; set; }

        public IEnumerable<string>? Kept { get; set; }

        /// <summary>Deleted, or with a dry run the files that would be deleted.</summary>
        public IEnumerable<string>? Deleted { get; set; }

        public IEnumerable<string>? Warnings { get; set; }
    }

    public static partial class McpServer
    {
        // ANet fork additions (branch 'anet'): project archives, for Portal.AnetFork.Archive.cs.
        // Not run inside a TIA Portal transaction: an archive is not a project edit, and
        // exclusive access could block Project.Archive. Registered only under '--allow-write'.

        #region archives

        private static T Audited<T>(string toolName, Func<T> body)
        {
            WritePolicy.EnsureEnabled(toolName);

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
                throw new McpException($"Unexpected error in '{toolName}': {ex.Message}", ex);
            }
        }

        [WriteTool]
        [McpServerTool(Name = "ArchiveProject", Title = "Archive project to a zap file", Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true),
         Description("Archive the open project as a compressed .zapXX file outside the project folder, named '<project>_V<major>.<minor>.<patch>.zapXX' for a full (last fully working) version or with the date and time appended for an interim one, e.g. '<project>_V1.2.0_2026-10-15_1430.zap21'. The version number is the operator's decision: ask before calling, never invent it. Defaults to a dry run that returns the target path. A project with unsaved changes is refused unless saveFirst is true. Archiving a large project takes minutes. Afterwards PruneArchives removes interim archives beyond the newest three")]
        public static ResponseArchive ArchiveProject(
            [Description("archiveDirectory: absolute path of the archive folder, outside the project folder; created when missing")] string archiveDirectory,
            [Description("version: V<major>.<minor>.<patch>, e.g. V1.2.0 - confirmed by the operator")] string version,
            [Description("kind: 'interim' (default, a checkpoint with date and time in the name) or 'full' (a fully working version)")] string kind = "interim",
            [Description("saveFirst: true saves unsaved changes before the archive; false (default) refuses a modified project")] bool saveFirst = false,
            [Description("dryRun: true (default) only returns the target path; false writes the archive")] bool dryRun = true)
        {
            return Audited(nameof(ArchiveProject), () =>
            {
                var result = Portal.ArchiveProject(archiveDirectory, version, kind, saveFirst, dryRun);

                return new ResponseArchive
                {
                    DryRun = dryRun,
                    Applied = result.Applied,
                    ProjectName = result.ProjectName,
                    Version = result.Version,
                    Kind = result.Kind,
                    Path = result.Path,
                    SizeBytes = result.SizeBytes,
                    SavedFirst = result.SavedFirst,
                    Warnings = result.Warnings,
                    Message = result.Applied
                        ? $"Project '{result.ProjectName}' archived to '{result.Path}' ({(result.SizeBytes ?? 0) / 1048576.0:F1} MB)" +
                          (result.SavedFirst ? ", saved first" : string.Empty) + ". Call PruneArchives to drop interim archives beyond the newest three."
                        : $"Dry run: '{result.ProjectName}' would be archived to '{result.Path}'" +
                          (result.SavedFirst ? " after saving its unsaved changes" : string.Empty) + ". Nothing was written; repeat with dryRun=false to archive.",
                    Meta = Ok()
                };
            });
        }

        [WriteTool]
        [McpServerTool(Name = "PruneArchives", Title = "Delete old interim archives", Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
         Description("Keep every full archive and the newest three interim archives of a project in the archive folder and delete the older interim ones. Only files whose name matches the pattern of ArchiveProject exactly are considered; a full archive is never deleted. Defaults to a dry run that lists what would go; a real deletion needs dryRun=false and delete=true, and is recorded in the audit file. Call it only after ArchiveProject confirmed the newest archive")]
        public static ResponsePrune PruneArchives(
            [Description("archiveDirectory: absolute path of the archive folder")] string archiveDirectory,
            [Description("projectName: the project whose archives are pruned; empty takes the open project")] string projectName = "",
            [Description("dryRun: true (default) only lists the files that would be deleted")] bool dryRun = true,
            [Description("delete: must be true, together with dryRun=false, to delete anything")] bool delete = false)
        {
            return Audited(nameof(PruneArchives), () =>
            {
                var result = Portal.PruneArchives(archiveDirectory, projectName, !dryRun && delete);
                var files = string.Join(", ", result.Deleted);

                return new ResponsePrune
                {
                    DryRun = dryRun,
                    Applied = result.Applied,
                    Directory = result.Directory,
                    ProjectName = result.ProjectName,
                    Kept = result.Kept,
                    Deleted = result.Deleted,
                    Warnings = result.Warnings,
                    Message = result.Deleted.Count == 0
                        ? $"Nothing to delete for '{result.ProjectName}' in '{result.Directory}'; {result.Kept.Count} archive(s) kept."
                        : result.Applied
                            ? $"Deleted {result.Deleted.Count} interim archive(s) of '{result.ProjectName}': {files}. {result.Kept.Count} archive(s) kept."
                            : dryRun
                                ? $"Dry run: {result.Deleted.Count} interim archive(s) of '{result.ProjectName}' would be deleted: {files}. Nothing was deleted; repeat with dryRun=false and delete=true."
                                : $"Not deleted: {result.Deleted.Count} interim archive(s) of '{result.ProjectName}' would go ({files}), but delete=true was not given.",
                    Meta = Ok()
                };
            });
        }

        #endregion
    }
}
