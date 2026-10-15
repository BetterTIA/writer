using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaMcpServer.Siemens
{
    public partial class Portal
    {
        // ANet fork additions (branch 'anet'): two safeguards every write runs through right
        // before it changes the project, modelled on Czarnak/tia-portal-mcp (MIT):
        //
        // - a new name is checked against every symbol of the CPU (tags, user constants, blocks,
        //   system blocks, technology objects, PLC data types), not only against the namespace
        //   of the renamed object - TIA Portal refuses a duplicate anyway, but late and with a
        //   message that names nothing;
        // - an import that was given the content hash of the export it was built from exports
        //   the object again and refuses to write when the two differ (ContentHash).
        //
        // Callers: the rename and create methods of Portal.Blocks.cs, Portal.Types.cs,
        // Portal.Tags.cs, Portal.AnetFork.cs and the import tools of McpServer. Affected API:
        // none existing - all members are new. File I/O: the hash comparison writes a temporary
        // export below the OS temp directory and removes it again.

        #region name collisions

        /// <summary>Every symbol name of the CPU with what holds it, compared without case as TIA Portal does.</summary>
        private Dictionary<string, string> CpuNames(string softwarePath)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Add(string name, string kind)
            {
                if (!string.IsNullOrEmpty(name) && !names.ContainsKey(name))
                {
                    names[name] = kind;
                }
            }

            foreach (var tag in GetTags(softwarePath)) Add(tag.Name, "tag");
            foreach (var constant in GetUserConstants(softwarePath)) Add(constant.Name, "user constant");
            foreach (var block in GetBlocks(softwarePath)) Add(block.Name, "block");
            foreach (var block in SystemBlocks(softwarePath)) Add(block.Name, "system block");
            foreach (var technologicalObject in GetTechnologicalObjects(softwarePath)) Add(technologicalObject.Name, "technology object");
            foreach (var type in GetTypes(softwarePath)) Add(type.Name, "PLC data type");

            return names;
        }

        /// <summary>The blocks below 'System blocks' (program resources, generated instances), which GetBlocks leaves out.</summary>
        private List<PlcBlock> SystemBlocks(string softwarePath)
        {
            var list = new List<PlcBlock>();

            foreach (var group in GetPlcSoftwareOrThrow(softwarePath).BlockGroup.SystemBlockGroups)
            {
                CollectSystemBlocks(group, list);
            }

            return list;
        }

        private static void CollectSystemBlocks(PlcSystemBlockGroup group, List<PlcBlock> list)
        {
            list.AddRange(group.Blocks);

            foreach (var child in group.Groups)
            {
                CollectSystemBlocks(child, list);
            }
        }

        /// <param name="currentName">The name of the object being renamed, so a change of casing only is no collision; null when creating.</param>
        private void EnsureCpuNameFree(string softwarePath, string? currentName, string newName) =>
            EnsureCpuNameFree(CpuNames(softwarePath), currentName, newName);

        private static void EnsureCpuNameFree(IDictionary<string, string> cpuNames, string? currentName, string newName)
        {
            if (currentName != null && currentName.Equals(newName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (cpuNames.TryGetValue(newName, out var kind))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"The name '{newName}' is already used by a {kind} of this PLC. Choose another name; nothing was changed.");
            }
        }

        #endregion

        #region content hash

        /// <summary>
        /// Exports the block again and compares it with <paramref name="expectedContentHash"/>;
        /// an empty hash skips the check. Throws 'state_changed' when the block differs, no
        /// longer exists, or is inconsistent now (TIA Portal exports no inconsistent block).
        /// </summary>
        public void VerifyBlockContentHash(string softwarePath, string blockPath, string expectedContentHash)
        {
            if (string.IsNullOrWhiteSpace(expectedContentHash))
            {
                return;
            }

            Operation.Run(_logger, nameof(VerifyBlockContentHash), PortalErrorCode.InvalidState,
                () =>
                {
                    var format = ContentHash.FormatOf(expectedContentHash);

                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.InvalidState,
                            $"state_changed: block '{blockPath}' no longer exists, so the expected content hash cannot be compared. Nothing was written.");

                    if (!block.IsConsistent)
                    {
                        throw new PortalException(PortalErrorCode.InvalidState,
                            $"state_changed: '{blockPath}' is inconsistent now and cannot be exported for the comparison. Nothing was written; compile the software, export again and retry.");
                    }

                    ContentHash.Verify(expectedContentHash, ExportedContentHash(block, format), blockPath);
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath));
        }

        /// <summary>The hash of a fresh export of the block in the given format, the export removed again.</summary>
        private string ExportedContentHash(PlcBlock block, string format)
        {
            var directory = CreateTempExportDirectory();

            try
            {
                if (format == ContentHash.Documents)
                {
                    var result = block.ExportAsDocuments(new DirectoryInfo(directory), block.Name);

                    if (result == null || result.State != DocumentResultState.Success)
                    {
                        throw new PortalException(PortalErrorCode.ExportFailed,
                            $"'{block.Name}' could not be exported as documents for the content check (state {result?.State.ToString() ?? "none"}).");
                    }

                    return ContentHash.OfFiles(format, Path.Combine(directory, $"{block.Name}.s7dcl"), Path.Combine(directory, $"{block.Name}.s7res"));
                }

                if (format != ContentHash.Xml)
                {
                    throw new PortalException(PortalErrorCode.InvalidParams,
                        $"Unknown content hash format '{format}'. The Writer computes 'xml' (ExportXmlBlock) and 'documents' (ExportAsDocuments, ExportBlocksAsDocuments).");
                }

                var file = Path.Combine(directory, "block.xml");

                block.Export(new FileInfo(file), ExportOptions.None);

                return ContentHash.OfFiles(format, file);
            }
            finally
            {
                DeleteTempExportDirectory(directory);
            }
        }

        /// <summary>
        /// For the export -> edit -> import tools, which have the fresh export in hand already:
        /// compares it with the hash the caller took from ExportXmlBlock.
        /// </summary>
        private static void VerifyExportedHash(Exported? exported, string expectedContentHash)
        {
            if (string.IsNullOrWhiteSpace(expectedContentHash))
            {
                return;
            }

            var format = ContentHash.FormatOf(expectedContentHash);

            if (format != ContentHash.Xml)
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"This tool compares SimaticML exports; pass the 'xml' content hash of ExportXmlBlock, not '{format}'.");
            }

            if (exported == null)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    "state_changed: the owner was not exported, so the expected content hash cannot be compared. Nothing was written.");
            }

            ContentHash.Verify(expectedContentHash, ContentHash.OfFiles(ContentHash.Xml, exported.File.FullName), exported.Info.Path);
        }

        #endregion
    }
}
