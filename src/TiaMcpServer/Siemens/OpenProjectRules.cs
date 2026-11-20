using System;
using System.IO;

namespace TiaMcpServer.Siemens
{
    public enum OpenDecision { Keep, Replace, Refuse }

    /// <summary>
    /// What OpenProject does with the project already open in the instance. Pure, so tests need no TIA Portal.
    /// </summary>
    public static class OpenProjectRules
    {
        public static OpenDecision Decide(string? openPath, bool openModified, string requestedPath)
        {
            if (openPath is null)
            {
                return OpenDecision.Replace; // nothing open
            }

            // An open project whose path cannot be read ("") counts as a different one, never as nothing open.
            if (openPath.Length > 0 && string.Equals(Path.GetFullPath(openPath), Path.GetFullPath(requestedPath), StringComparison.OrdinalIgnoreCase))
            {
                return OpenDecision.Keep;
            }

            return openModified ? OpenDecision.Refuse : OpenDecision.Replace;
        }
    }
}
