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
    /// An archive is '&lt;project&gt;_&lt;yyyy-MM-dd_HHmm&gt;_V&lt;major&gt;.&lt;minor&gt;.&lt;patch&gt;.zap&lt;tia&gt;':
    /// the time it was taken, then the version at the end. The version lives in the archive name
    /// only, the project carries none. A patch of 0 ('V2.3.0') marks a full version - the tool
    /// declared working and tested; every checkpoint on the way there counts the patch up
    /// ('V2.2.1', 'V2.2.2', ...) and is an interim archive. Pruning keeps the newest full archive
    /// and the three newest interim ones, newest by the time in the name; only files whose name
    /// matches this pattern exactly are ever deleted.
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
            @"^(?<project>.+)_(?<time>\d{4}-\d{2}-\d{2}_\d{4})_V(?<version>\d+\.\d+\.\d+)\.zap\d+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public sealed class ArchiveFile
        {
            public string Path { get; set; } = string.Empty;

            public string Project { get; set; } = string.Empty;

            /// <summary>'V1.2.3'.</summary>
            public string Version { get; set; } = string.Empty;

            /// <summary>'full' when the patch is 0, otherwise 'interim'.</summary>
            public string Kind { get; set; } = Full;

            /// <summary>When the archive was taken, from its name.</summary>
            public DateTime Time { get; set; }

            internal int[] VersionKey { get; set; } = new int[3];
        }

        public sealed class PrunePlan
        {
            public List<ArchiveFile> Keep { get; set; } = new List<ArchiveFile>();

            public List<ArchiveFile> Delete { get; set; } = new List<ArchiveFile>();

            /// <summary>Archives that are older by time but higher by version than a kept one: kept as well, and named here.</summary>
            public List<string> Warnings { get; set; } = new List<string>();
        }

        /// <summary>'1.2.3' or 'V1.2.3' to 'V1.2.3'; anything else is refused - the number is the operator's decision, never guessed.</summary>
        public static string NormalizeVersion(string? version)
        {
            var match = VersionShape.Match((version ?? string.Empty).Trim());

            if (!match.Success)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"'{version}' is not a version. Write it as V<major>.<minor>.<patch>, e.g. V2.2.1 for a checkpoint or V2.3.0 for a full version; the operator confirms the number.");
            }

            return $"V{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";
        }

        /// <summary>'full' for a patch of 0, 'interim' otherwise.</summary>
        /// <param name="version">Already normalized ('V1.2.3').</param>
        public static string KindOf(string version) => version.EndsWith(".0", StringComparison.Ordinal) ? Full : Interim;

        /// <param name="version">Already normalized ('V1.2.3').</param>
        public static string FileName(string projectName, string version, DateTime time, int tiaMajorVersion) =>
            $"{projectName}_{time.ToString(TimestampFormat, CultureInfo.InvariantCulture)}_{version}.zap{tiaMajorVersion}";

        /// <summary>The archive a path names, or null when the file name does not match the pattern exactly.</summary>
        public static ArchiveFile? Parse(string path)
        {
            var match = FileShape.Match(Path.GetFileName(path));

            if (!match.Success
                || !DateTime.TryParseExact(match.Groups["time"].Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                return null;
            }

            var version = "V" + match.Groups["version"].Value;

            return new ArchiveFile
            {
                Path = path,
                Project = match.Groups["project"].Value,
                Version = version,
                Kind = KindOf(version),
                Time = time,
                VersionKey = match.Groups["version"].Value.Split('.').Select(int.Parse).ToArray()
            };
        }

        private static int CompareVersion(ArchiveFile a, ArchiveFile b)
        {
            for (var i = 0; i < 3; i++)
            {
                var c = a.VersionKey[i].CompareTo(b.VersionKey[i]);

                if (c != 0)
                {
                    return c;
                }
            }

            return 0;
        }

        /// <summary>Newest by the time in the name first; the version only breaks a tie.</summary>
        private static List<ArchiveFile> NewestFirst(IEnumerable<ArchiveFile> archives)
        {
            var list = archives.ToList();

            list.Sort((a, b) => b.Time != a.Time ? b.Time.CompareTo(a.Time) : CompareVersion(b, a));

            return list;
        }

        /// <summary>
        /// Splits the archives of one project into the ones to keep and the ones to delete: the
        /// newest full archive stays, the <see cref="InterimToKeep"/> newest interim archives
        /// stay, everything older goes - newest by the time in the name, so a later archive
        /// with a lower version number is never deleted in favour of an earlier one. An archive
        /// that would go although its version is higher than a kept one of its kind is kept as
        /// well and reported, because time and version disagree and the operator has to look.
        /// Files of other projects or of another shape are not returned at all, so they can never
        /// be deleted.
        /// </summary>
        public static PrunePlan Plan(IEnumerable<string> paths, string projectName)
        {
            var archives = paths
                .Select(Parse)
                .Where(a => a != null && string.Equals(a.Project, projectName, StringComparison.OrdinalIgnoreCase))
                .Select(a => a!)
                .ToList();

            var full = NewestFirst(archives.Where(a => a.Kind == Full));
            var interim = NewestFirst(archives.Where(a => a.Kind == Interim));

            var plan = new PrunePlan();
            plan.Keep.AddRange(full.Take(1));
            plan.Keep.AddRange(interim.Take(InterimToKeep));

            foreach (var candidate in full.Skip(1).Concat(interim.Skip(InterimToKeep)))
            {
                var kept = plan.Keep.Where(k => k.Kind == candidate.Kind).ToList();

                if (kept.Any(k => CompareVersion(candidate, k) > 0))
                {
                    plan.Keep.Add(candidate);
                    plan.Warnings.Add(
                        $"'{Path.GetFileName(candidate.Path)}' is older by time than the kept {candidate.Kind} archive(s) but has a higher version; kept, not deleted - check which one is the real newer state.");
                }
                else
                {
                    plan.Delete.Add(candidate);
                }
            }

            return plan;
        }
    }
}
