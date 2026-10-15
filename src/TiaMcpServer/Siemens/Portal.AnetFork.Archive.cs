using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    public sealed class ArchiveResult
    {
        public bool Applied { get; set; }

        public string ProjectName { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        /// <summary>The archive file, written or planned.</summary>
        public string Path { get; set; } = string.Empty;

        public long? SizeBytes { get; set; }

        /// <summary>True when the project had unsaved changes and was saved before the archive.</summary>
        public bool SavedFirst { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class PruneResult
    {
        public bool Applied { get; set; }

        public string Directory { get; set; } = string.Empty;

        public string ProjectName { get; set; } = string.Empty;

        public List<string> Kept { get; set; } = new List<string>();

        /// <summary>Deleted, or with a dry run the files that would be deleted.</summary>
        public List<string> Deleted { get; set; } = new List<string>();

        public List<string> Warnings { get; set; } = new List<string>();
    }

    public partial class Portal
    {
        // ANet fork additions (branch 'anet'): project archives. Openness offers Project.Archive;
        // the naming and the retention rule live in ArchiveRules. The tools never schedule
        // themselves - the agent or the BetterTIA Editor calls them (before the first change of a
        // session, after a series of accepted changes, at the end of the day) and the operator
        // confirms the version number.
        //
        // Callers: McpServer.AnetFork.Archive.cs. Affected API: none existing. File I/O: writes
        // one archive file into the given directory; PruneArchives deletes files there whose
        // name matches ArchiveRules exactly, and nothing else.

        #region archive

        /// <param name="saveFirst">Save unsaved changes before archiving; without it a modified project is refused.</param>
        public ArchiveResult ArchiveProject(string archiveDirectory, string version, string kind, bool saveFirst, bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(ArchiveProject), PortalErrorCode.ExportFailed,
                () =>
                {
                    var project = _project as Project
                        ?? throw new PortalException(PortalErrorCode.InvalidState,
                            "No local project is open in TIA Portal (a multiuser session cannot be archived by this tool).");

                    var normalizedVersion = ArchiveRules.NormalizeVersion(version);
                    var normalizedKind = ArchiveRules.NormalizeKind(kind);

                    if (string.IsNullOrWhiteSpace(archiveDirectory) || !Path.IsPathRooted(archiveDirectory))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "archiveDirectory must be an absolute path.");
                    }

                    var directory = Path.GetFullPath(archiveDirectory);
                    var projectDirectory = project.Path?.Directory?.FullName;

                    // Seen upstream: archiving into the project's own folder fails or makes TIA Portal delete the folder again.
                    if (projectDirectory != null
                        && (directory.TrimEnd('\\') + "\\").StartsWith(projectDirectory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"The archive directory must lie outside the project folder '{projectDirectory}'.");
                    }

                    var projectName = project.Name;
                    var fileName = ArchiveRules.FileName(projectName, normalizedVersion, normalizedKind, DateTime.Now, Engineering.TiaMajorVersion);
                    var target = Path.Combine(directory, fileName);
                    var modified = Safe(() => project.IsModified);

                    var result = new ArchiveResult
                    {
                        ProjectName = projectName,
                        Version = normalizedVersion,
                        Kind = normalizedKind,
                        Path = target,
                        SavedFirst = modified && saveFirst
                    };

                    if (File.Exists(target))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"'{target}' already exists. A version is archived once; choose the next version number" +
                            (normalizedKind == ArchiveRules.Interim ? " or wait a minute for a new time stamp." : "."));
                    }

                    if (modified && !saveFirst)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            "The project has unsaved changes and TIA Portal archives the saved state. Call SaveProject first, or repeat with saveFirst=true.");
                    }

                    result.Warnings.Add("Archiving a large project takes minutes; the call returns when TIA Portal has written the file.");

                    if (dryRun)
                    {
                        return result;
                    }

                    Directory.CreateDirectory(directory);

                    if (modified)
                    {
                        project.Save();
                    }

                    project.Archive(new DirectoryInfo(directory), fileName, ProjectArchivationMode.Compressed);

                    var info = new FileInfo(target);

                    if (!info.Exists || info.Length == 0)
                    {
                        var written = Directory.GetFiles(directory).Where(f => File.GetLastWriteTime(f) >= DateTime.Now.AddMinutes(-10)).Select(Path.GetFileName).ToList();

                        throw new PortalException(PortalErrorCode.ExportFailed,
                            $"TIA Portal returned from Archive, but '{target}' is missing or empty. Files written in the last minutes: {string.Join(", ", written)}. Inspect the directory before relying on this archive.");
                    }

                    result.Applied = true;
                    result.SizeBytes = info.Length;

                    return result;
                },
                ("archiveDirectory", archiveDirectory), ("version", version), ("kind", kind), ("dryRun", dryRun));
        }

        /// <param name="projectName">Empty takes the name of the open project.</param>
        /// <param name="delete">False lists the files that would go; only true deletes.</param>
        public PruneResult PruneArchives(string archiveDirectory, string projectName, bool delete)
        {
            return Operation.Run(_logger, nameof(PruneArchives), PortalErrorCode.DeleteFailed,
                () =>
                {
                    if (string.IsNullOrWhiteSpace(archiveDirectory) || !Path.IsPathRooted(archiveDirectory))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "archiveDirectory must be an absolute path.");
                    }

                    if (!Directory.Exists(archiveDirectory))
                    {
                        throw new PortalException(PortalErrorCode.NotFound, $"The archive directory '{archiveDirectory}' does not exist.");
                    }

                    var name = string.IsNullOrWhiteSpace(projectName)
                        ? Safe(() => _project?.Name) ?? throw new PortalException(PortalErrorCode.InvalidParams, "No project is open; give projectName.")
                        : projectName.Trim();

                    var (keep, remove) = ArchiveRules.Plan(Directory.GetFiles(archiveDirectory), name);

                    var result = new PruneResult
                    {
                        Directory = archiveDirectory,
                        ProjectName = name,
                        Kept = keep.Select(a => Path.GetFileName(a.Path)).ToList(),
                        Deleted = remove.Select(a => Path.GetFileName(a.Path)).ToList()
                    };

                    if (!keep.Any(a => a.Kind == ArchiveRules.Full))
                    {
                        result.Warnings.Add($"No full archive of '{name}' is in the directory; only interim archives would remain.");
                    }

                    if (!delete || remove.Count == 0)
                    {
                        return result;
                    }

                    foreach (var archive in remove)
                    {
                        File.Delete(archive.Path);

                        if (File.Exists(archive.Path))
                        {
                            throw new PortalException(PortalErrorCode.DeleteFailed,
                                $"'{archive.Path}' still exists after the delete. Files deleted before it: {string.Join(", ", remove.TakeWhile(a => a != archive).Select(a => Path.GetFileName(a.Path)))}.");
                        }
                    }

                    result.Applied = true;

                    return result;
                },
                ("archiveDirectory", archiveDirectory), ("projectName", projectName), ("delete", delete));
        }

        #endregion
    }
}
