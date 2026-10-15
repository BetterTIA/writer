using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Naming and retention of project archives (ArchiveProject, PruneArchives), kept pure so
    /// Test12Archive covers them without TIA Portal.
    ///
    /// A full archive is '&lt;project&gt;_V&lt;major&gt;.&lt;minor&gt;.&lt;patch&gt;.zap&lt;tia&gt;' - the
    /// version lives in the archive name only, the project itself carries none. An interim
    /// archive adds the time it was taken: '&lt;project&gt;_V1.2.0_2026-10-15_1430.zap21', the
    /// version being the one the work is heading for. Pruning keeps every full archive and the
    /// newest interim ones; only interim archives whose name matches this pattern exactly are
    /// ever deleted.
    /// </summary>
    public static class ArchiveRules
    {
        public const string Full = "full";

        public const string Interim = "interim";

        /// <summary>Interim archives kept per project by PruneArchives, newest first.</summary>
        public const int InterimToKeep = 3;

        private const string TimestampFormat = "yyyy-MM-dd_HHmm";

        private static readonly Regex VersionShape = new Regex(@"^[Vv]?(\d+)\.(\d+)\.(\d+)$", RegexOptions.Compiled);

        private static readonly Regex FileShape = new Regex(
            @"^(?<project>.+)_V(?<version>\d+\.\d+\.\d+)(?:_(?<time>\d{4}-\d{2}-\d{2}_\d{4}))?\.zap\d+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public sealed class ArchiveFile
        {
            public string Path { get; set; } = string.Empty;

            public string Project { get; set; } = string.Empty;

            /// <summary>'V1.2.3'.</summary>
            public string Version { get; set; } = string.Empty;

            public string Kind { get; set; } = Full;

            /// <summary>When an interim archive was taken, from its name; null for a full archive.</summary>
            public DateTime? Time { get; set; }

            internal int[] VersionKey { get; set; } = new int[3];
        }

        /// <summary>'1.2.3' or 'V1.2.3' to 'V1.2.3'; anything else is refused - the number is the operator's decision, never guessed.</summary>
        public static string NormalizeVersion(string? version)
        {
            var match = VersionShape.Match((version ?? string.Empty).Trim());

            if (!match.Success)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"'{version}' is not a version. Write it as V<major>.<minor>.<patch>, e.g. V1.2.0; the operator confirms the number.");
            }

            return $"V{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";
        }

        public static string NormalizeKind(string? kind)
        {
            if (string.Equals(kind, Full, StringComparison.OrdinalIgnoreCase))
            {
                return Full;
            }

            if (string.Equals(kind, Interim, StringComparison.OrdinalIgnoreCase))
            {
                return Interim;
            }

            throw new PortalException(PortalErrorCode.InvalidParams, $"Unknown archive kind '{kind}'. Use 'full' or 'interim'.");
        }

        /// <param name="version">Already normalized ('V1.2.3').</param>
        /// <param name="kind">Already normalized.</param>
        public static string FileName(string projectName, string version, string kind, DateTime time, int tiaMajorVersion)
        {
            var stamp = kind == Interim ? "_" + time.ToString(TimestampFormat, CultureInfo.InvariantCulture) : string.Empty;

            return $"{projectName}_{version}{stamp}.zap{tiaMajorVersion}";
        }

        /// <summary>The archive a path names, or null when the file name does not match the pattern exactly.</summary>
        public static ArchiveFile? Parse(string path)
        {
            var match = FileShape.Match(Path.GetFileName(path));

            if (!match.Success)
            {
                return null;
            }

            var file = new ArchiveFile
            {
                Path = path,
                Project = match.Groups["project"].Value,
                Version = "V" + match.Groups["version"].Value,
                VersionKey = match.Groups["version"].Value.Split('.').Select(int.Parse).ToArray()
            };

            if (match.Groups["time"].Success)
            {
                if (!DateTime.TryParseExact(match.Groups["time"].Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                {
                    return null;
                }

                file.Kind = Interim;
                file.Time = time;
            }

            return file;
        }

        /// <summary>
        /// Splits the archives of one project into the ones to keep and the ones to delete:
        /// every full archive stays, the newest <see cref="InterimToKeep"/> interim archives
        /// stay, older interim archives go. Files of other projects or of another shape are
        /// not returned at all, so they can never be deleted.
        /// </summary>
        public static (List<ArchiveFile> Keep, List<ArchiveFile> Delete) Plan(IEnumerable<string> paths, string projectName)
        {
            var archives = paths
                .Select(Parse)
                .Where(a => a != null && string.Equals(a.Project, projectName, StringComparison.OrdinalIgnoreCase))
                .Select(a => a!)
                .ToList();

            var keep = archives.Where(a => a.Kind == Full).ToList();
            var interim = archives
                .Where(a => a.Kind == Interim)
                .OrderByDescending(a => a.Time)
                .ThenByDescending(a => a.VersionKey[0]).ThenByDescending(a => a.VersionKey[1]).ThenByDescending(a => a.VersionKey[2])
                .ToList();

            keep.AddRange(interim.Take(InterimToKeep));

            return (keep, interim.Skip(InterimToKeep).ToList());
        }
    }
}
