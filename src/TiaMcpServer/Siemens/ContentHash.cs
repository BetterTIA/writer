using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// The fingerprint of an exported object, '&lt;format&gt;:sha256:&lt;hex&gt;', that an import
    /// can hand back as expectedContentHash: right before the write the object is exported
    /// again and the two fingerprints have to match, otherwise the project changed in between
    /// and nothing is written. The Writer computes both sides itself; a hash from another tool
    /// over another export does not compare.
    ///
    /// Formats: 'xml' over the SimaticML export of a block or type, 'documents' over the
    /// .s7dcl and .s7res files of a block. Pure, so Test11Safeguards covers it without TIA Portal.
    /// </summary>
    public static class ContentHash
    {
        public const string Xml = "xml";

        public const string Documents = "documents";

        private static readonly Regex Shape = new Regex("^(?<format>[a-z0-9]+):sha256:(?<hex>[0-9a-f]{64})$", RegexOptions.Compiled);

        /// <summary>SHA-256 over the parts in order, a zero byte between them so parts cannot shift into each other.</summary>
        public static string OfBytes(string format, params byte[][] parts)
        {
            using (var sha = SHA256.Create())
            {
                var separator = new byte[] { 0 };

                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        sha.TransformBlock(separator, 0, 1, null, 0);
                    }

                    sha.TransformBlock(parts[i], 0, parts[i].Length, null, 0);
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                return $"{format}:sha256:{BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant()}";
            }
        }

        /// <summary>A file that does not exist counts as empty (a block without texts has no .s7res).</summary>
        public static string OfFiles(string format, params string[] paths) =>
            OfBytes(format, paths.Select(p => File.Exists(p) ? File.ReadAllBytes(p) : Array.Empty<byte>()).ToArray());

        /// <summary>The format part of a hash, after checking its shape.</summary>
        public static string FormatOf(string hash)
        {
            var match = Shape.Match((hash ?? string.Empty).Trim());

            if (!match.Success)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"'{hash}' is not a content hash. Expected '<format>:sha256:<64 hex digits>' as an export tool returned it.");
            }

            return match.Groups["format"].Value;
        }

        /// <summary>Throws when the object changed since it was exported; the message names the object and both hashes.</summary>
        public static void Verify(string expected, string actual, string what)
        {
            if (!string.Equals(expected.Trim(), actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"state_changed: '{what}' changed since it was exported (expected {expected.Trim()}, now {actual}). " +
                    "Nothing was written. Export it again, redo the edit on the fresh export and retry.");
            }
        }
    }
}
