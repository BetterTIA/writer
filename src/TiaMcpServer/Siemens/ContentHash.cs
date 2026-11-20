using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

        /// <summary>
        /// Elements left out of an 'xml' hash because TIA Portal rewrites them on every export
        /// without the block changing: the DocumentInfo header (Created, export settings,
        /// installed products) and the date elements of a block's attribute list. An explicit
        /// list, not a substring match: real content such as the AccessModifier attribute of a
        /// Component must never drop out. Attributes are never removed. Add a name only after
        /// two exports of an unchanged block are seen to differ in it.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> Volatile = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            "Created", "DocumentInfo", "ModifiedDate", "ParameterModified", "InterfaceModifiedDate", "CodeModifiedDate", "StructureModified",
        };

        /// <summary>The XML of an export without the volatile parts, no indentation, LF line ends.</summary>
        public static byte[] NormalizeXml(byte[] xml)
        {
            try
            {
                XDocument doc;
                using (var stream = new MemoryStream(xml))
                {
                    doc = XDocument.Load(stream, LoadOptions.None);
                }

                foreach (var element in doc.Descendants().Where(e => Volatile.Contains(e.Name.LocalName)).ToList())
                {
                    element.Remove();
                }

                return Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting).Replace("\r\n", "\n"));
            }
            catch (System.Xml.XmlException)
            {
                // Not XML after all: hash the text as it is, only with the line ends unified.
                return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(xml).Replace("\r\n", "\n"));
            }
        }

        /// <summary>The 'xml' hash of an exported file, over its normalized content.</summary>
        public static string OfXmlFile(string path) => OfBytes(Xml, NormalizeXml(File.ReadAllBytes(path)));

        /// <summary>A file that does not exist counts as empty (a block without texts has no .s7res); null when none exists, so no hash of nothing is ever handed out.</summary>
        public static string? OfFiles(string format, params string[] paths) =>
            paths.All(p => !File.Exists(p)) ? null : OfBytes(format, paths.Select(p => File.Exists(p) ? File.ReadAllBytes(p) : Array.Empty<byte>()).ToArray());

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
        public static void Verify(string expected, string? actual, string what)
        {
            if (!string.Equals(expected.Trim(), actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"state_changed: '{what}' changed since it was exported (expected {expected.Trim()}, now {actual ?? "no files"}). " +
                    "Nothing was written. Export it again, redo the edit on the fresh export and retry.");
            }
        }
    }
}
