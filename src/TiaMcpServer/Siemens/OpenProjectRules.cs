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
            if (string.IsNullOrEmpty(openPath))
            {
                return OpenDecision.Replace; // nothing open
            }

            if (string.Equals(Path.GetFullPath(openPath), Path.GetFullPath(requestedPath), StringComparison.OrdinalIgnoreCase))
            {
                return OpenDecision.Keep;
            }

            return openModified ? OpenDecision.Refuse : OpenDecision.Replace;
        }
    }
}
