using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TiaMcpServer.Siemens
{
    /// <summary>One block or PLC data type an export -> edit -> import operation touches.</summary>
    public sealed class XmlEditObject
    {
        /// <summary>Root-relative path, as the block and type tools expect it.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>'block' or 'type'.</summary>
        public string Kind { get; set; } = string.Empty;

        public bool Failsafe { get; set; }

        public int Changes { get; set; }
    }

    public sealed class XmlEditResult
    {
        /// <summary>False for a dry run, and when there was nothing to change.</summary>
        public bool Applied { get; set; }

        /// <summary>The objects that are (or would be) imported back, in import order.</summary>
        public List<XmlEditObject> Objects { get; set; } = new List<XmlEditObject>();

        public List<XmlChange> Changes { get; set; } = new List<XmlChange>();

        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Objects TIA Portal would not export, so references inside them were not seen.</summary>
        public List<string> NotScanned { get; set; } = new List<string>();
    }

    public partial class Portal
    {
        // ANet fork additions (branch 'anet'), kept in this one file so merges from upstream stay
        // trivial. Two families:
        //
        // - renames Openness has a setter for but upstream does not expose: block, type and tag
        //   table groups, technology objects;
        // - edits Openness has no setter for, composed from export -> edit the XML -> import:
        //   network titles and comments, and the names of interface / DB / PLC data type members.
        //   The XML edits themselves live in SimaticMl.cs.
        //
        // Callers: the tools in McpServer.AnetFork.cs, registered only under '--allow-write'.
        // Affected API: none existing - all members are new. File I/O: the export -> import
        // operations write their intermediate .xml files into a private directory below the OS
        // temp directory and remove it in a finally block. Project changes stay in memory until
        // SaveProject / SaveSession.
        //
        // An import replaces the whole object, so unlike a setter it marks the block as changed
        // (download needed) and, for a fail-safe block, changes the F-signature. That is why
        // these operations default to a dry run and refuse fail-safe blocks unless told otherwise.

        #region renames with a setter

        private static void EnsureFreeName(IEnumerable<string> siblings, string currentName, string newName, string kind)
        {
            // Compared without case, as TIA Portal does; renaming an object to another casing of its own name stays possible.
            if (!currentName.Equals(newName, StringComparison.OrdinalIgnoreCase)
                && siblings.Any(n => n.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new PortalException(PortalErrorCode.InvalidParams,
                    $"A {kind} named '{newName}' already exists next to '{currentName}'. Choose another name.");
            }
        }

        public bool RenameBlockGroup(string softwarePath, string groupPath, string newName)
        {
            return Operation.Run(_logger, nameof(RenameBlockGroup), PortalErrorCode.RenameFailed,
                () =>
                {
                    EnsureValidName(newName);

                    var group = EnsureUserGroup(GetPlcBlockGroupByPath(softwarePath, groupPath), groupPath);

                    EnsureFreeName(((PlcBlockGroup)group.Parent).Groups.Select(g => g.Name), group.Name, newName, "block group");
                    group.Name = newName;

                    return true;
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath), ("newName", newName));
        }

        public bool RenameTypeGroup(string softwarePath, string groupPath, string newName)
        {
            return Operation.Run(_logger, nameof(RenameTypeGroup), PortalErrorCode.RenameFailed,
                () =>
                {
                    EnsureValidName(newName);

                    var group = EnsureUserGroup(GetPlcTypeGroupByPath(softwarePath, groupPath), groupPath);

                    EnsureFreeName(((PlcTypeGroup)group.Parent).Groups.Select(g => g.Name), group.Name, newName, "type group");
                    group.Name = newName;

                    return true;
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath), ("newName", newName));
        }

        public bool RenameTagTableGroup(string softwarePath, string groupPath, string newName)
        {
            return Operation.Run(_logger, nameof(RenameTagTableGroup), PortalErrorCode.RenameFailed,
                () =>
                {
                    EnsureValidName(newName);

                    var group = GetTagTableGroupByPath(softwarePath, groupPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Tag table group not found at '{groupPath}'. Use 'GetSoftwareTree' to discover valid group paths.");

                    var userGroup = group as PlcTagTableUserGroup
                        ?? throw new PortalException(PortalErrorCode.NotSupported,
                            $"'{groupPath}' is the system group 'PLC tags', which cannot be renamed.");

                    EnsureFreeName(((PlcTagTableGroup)userGroup.Parent).Groups.Select(g => g.Name), userGroup.Name, newName, "tag table group");
                    userGroup.Name = newName;

                    return true;
                },
                ("softwarePath", softwarePath), ("groupPath", groupPath), ("newName", newName));
        }

        /// <param name="name">Name of the technology object; they are unique within a PLC, so no path is needed.</param>
        public bool RenameTechnologicalObject(string softwarePath, string name, string newName)
        {
            return Operation.Run(_logger, nameof(RenameTechnologicalObject), PortalErrorCode.RenameFailed,
                () =>
                {
                    EnsureValidName(newName);

                    var objects = new List<TechnologicalInstanceDB>();

                    WalkRecursive<TechnologicalInstanceDBGroup, TechnologicalInstanceDB>(
                        GetPlcSoftwareOrThrow(softwarePath).TechnologicalObjectGroup,
                        objects, g => g.TechnologicalObjects, g => g.Groups, t => t.Name);

                    var technologicalObject = objects.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Technology object '{name}' not found. Available: {string.Join(", ", objects.Select(t => t.Name))}.");

                    // A technology object is a data block and shares the block namespace.
                    EnsureFreeName(
                        objects.Select(t => t.Name).Concat(GetBlocks(softwarePath).Select(b => b.Name)),
                        technologicalObject.Name, newName, "block or technology object");
                    technologicalObject.Name = newName;

                    return true;
                },
                ("softwarePath", softwarePath), ("name", name), ("newName", newName));
        }

        #endregion

        #region export -> edit -> import

        /// <summary>An exported object: where its file is, and how to put it back.</summary>
        private sealed class Exported
        {
            public string Name = string.Empty;
            public XmlEditObject Info = new XmlEditObject();
            public FileInfo File = null!;
            public XDocument Doc = null!;
            public Action Import = () => { };
        }

        // F_LAD, F_FBD, F_DB, F_CALL, ...: every fail-safe language of the enum carries this prefix.
        private static bool IsFailsafe(PlcBlock block) =>
            block.ProgrammingLanguage.ToString().StartsWith("F_", StringComparison.Ordinal);

        private Exported ExportForEdit(PlcBlock block, string directory, int index)
        {
            var file = new FileInfo(Path.Combine(directory, $"block{index}.xml"));
            var group = (PlcBlockGroup)block.Parent;

            block.Export(file, ExportOptions.None);

            return new Exported
            {
                Name = block.Name,
                Info = new XmlEditObject { Path = GetBlockPath(block), Kind = "block", Failsafe = IsFailsafe(block) },
                File = file,
                Doc = XDocument.Load(file.FullName, LoadOptions.PreserveWhitespace),
                Import = () => group.Blocks.Import(file, ImportOptions.Override)
            };
        }

        private Exported ExportForEdit(PlcType type, string directory, int index)
        {
            var file = new FileInfo(Path.Combine(directory, $"type{index}.xml"));
            var group = (PlcTypeGroup)type.Parent;

            type.Export(file, ExportOptions.None);

            return new Exported
            {
                Name = type.Name,
                Info = new XmlEditObject { Path = GetTypePath(type), Kind = "type" },
                File = file,
                Doc = XDocument.Load(file.FullName, LoadOptions.PreserveWhitespace),
                Import = () => group.Types.Import(file, ImportOptions.Override)
            };
        }

        /// <summary>
        /// Fills in the result for the edited objects and, unless this is a dry run, imports them
        /// in the given order.
        /// </summary>
        private static XmlEditResult Finish(XmlEditResult result, List<Exported> edited, bool dryRun, bool allowFailsafe)
        {
            foreach (var item in edited)
            {
                item.Info.Changes = result.Changes.Count(c => c.Object.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
                result.Objects.Add(item.Info);

                if (item.Info.Failsafe)
                {
                    result.Warnings.Add(
                        $"'{item.Info.Path}' is a fail-safe block: importing it changes the F-signature, so the safety program " +
                        "has to be compiled and accepted again by the operator.");
                }
            }

            if (dryRun || edited.Count == 0)
            {
                return result;
            }

            if (!allowFailsafe && edited.Any(e => e.Info.Failsafe))
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    "This would modify fail-safe block(s): " +
                    string.Join(", ", edited.Where(e => e.Info.Failsafe).Select(e => e.Info.Path)) +
                    ". Review the dry run with the operator and repeat with allowFailsafe=true if that is intended.");
            }

            foreach (var item in edited)
            {
                item.Doc.Save(item.File.FullName, SaveOptions.DisableFormatting);
                item.Import();
            }

            result.Applied = true;

            return result;
        }

        /// <summary>
        /// Exports one block, lets <paramref name="edit"/> change the titles and comments of its
        /// networks in the XML, and imports it back over itself.
        /// </summary>
        /// <param name="edit">One of the SimaticMl network text edits; returns what it changed.</param>
        public XmlEditResult EditNetworkTexts(
            string softwarePath, string blockPath, Func<XDocument, List<XmlChange>> edit, bool dryRun = true, bool allowFailsafe = false)
        {
            return Operation.Run(_logger, nameof(EditNetworkTexts), PortalErrorCode.ImportFailed,
                () =>
                {
                    var block = GetBlock(softwarePath, blockPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Block not found at '{blockPath}'. Use 'GetBlocks' to list the available blocks.");

                    EnsureNotKnowHowProtected(block);
                    EnsureConsistent(block);

                    var directory = CreateTempExportDirectory();

                    try
                    {
                        var exported = ExportForEdit(block, directory, 0);
                        var result = new XmlEditResult { Changes = edit(exported.Doc) };

                        return Finish(result, result.Changes.Count > 0 ? new List<Exported> { exported } : new List<Exported>(), dryRun, allowFailsafe);
                    }
                    finally
                    {
                        DeleteTempExportDirectory(directory);
                    }
                },
                ("softwarePath", softwarePath), ("blockPath", blockPath), ("dryRun", dryRun));
        }

        /// <summary>
        /// Renames members of a block interface, a data block or a PLC data type, and the
        /// references to them in every block of the PLC.
        ///
        /// Every block and type is exported, because a reference can only be attributed to the
        /// renamed member by following its access path through the declarations (see
        /// SimaticMl.RenameMembers). The owner is imported first, then the types, then the blocks.
        /// Not covered, because they are not part of a block export: HMI tags, watch and force
        /// tables, technology object parameters, traces.
        /// </summary>
        /// <param name="ownerIsType">True when <paramref name="ownerPath"/> names a PLC data type, false for a block.</param>
        /// <param name="renames">Member path inside the owner ("Config.Speed") to its new name.</param>
        public XmlEditResult RenameMembers(
            string softwarePath, string ownerPath, bool ownerIsType, IDictionary<string, string> renames, bool dryRun = true, bool allowFailsafe = false)
        {
            return Operation.Run(_logger, nameof(RenameMembers), PortalErrorCode.ImportFailed,
                () =>
                {
                    if (renames.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No rename was given.");
                    }

                    foreach (var newName in renames.Values)
                    {
                        EnsureValidName(newName);
                    }

                    string ownerName;

                    if (ownerIsType)
                    {
                        var type = GetType(softwarePath, ownerPath)
                            ?? throw new PortalException(PortalErrorCode.NotFound,
                                $"Type not found at '{ownerPath}'. Use 'GetTypes' to list the available types.");

                        EnsureNotKnowHowProtected(type);
                        EnsureConsistent(type);
                        ownerName = type.Name;
                    }
                    else
                    {
                        var block = GetBlock(softwarePath, ownerPath)
                            ?? throw new PortalException(PortalErrorCode.NotFound,
                                $"Block not found at '{ownerPath}'. Use 'GetBlocks' to list the available blocks.");

                        EnsureNotKnowHowProtected(block);
                        EnsureConsistent(block);
                        ownerName = block.Name;
                    }

                    var result = new XmlEditResult();
                    var exported = new List<Exported>();
                    var directory = CreateTempExportDirectory();

                    try
                    {
                        foreach (var type in GetTypes(softwarePath))
                        {
                            if (Scannable(result, GetTypePath(type), type.IsKnowHowProtected, type.IsConsistent))
                            {
                                exported.Add(ExportForEdit(type, directory, exported.Count));
                            }
                        }

                        foreach (var block in GetBlocks(softwarePath))
                        {
                            if (Scannable(result, GetBlockPath(block), block.IsKnowHowProtected, block.IsConsistent))
                            {
                                exported.Add(ExportForEdit(block, directory, exported.Count));
                            }
                        }

                        var docs = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
                        var tagTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var item in exported)
                        {
                            docs[item.Name] = item.Doc;
                        }

                        foreach (var tag in GetTags(softwarePath))
                        {
                            tagTypes[tag.Name] = tag.DataTypeName;
                        }

                        var renamed = SimaticMl.RenameMembers(docs, tagTypes, ownerName, renames);

                        result.Changes = renamed.Changes;
                        result.Warnings.AddRange(renamed.Warnings);

                        // The declaration has to exist before anything that refers to it is imported.
                        var edited = exported
                            .Where(e => renamed.Changed.Contains(e.Name))
                            .OrderBy(e => e.Name.Equals(ownerName, StringComparison.OrdinalIgnoreCase) ? 0 : e.Info.Kind == "type" ? 1 : 2)
                            .ToList();

                        return Finish(result, edited, dryRun, allowFailsafe);
                    }
                    finally
                    {
                        DeleteTempExportDirectory(directory);
                    }
                },
                ("softwarePath", softwarePath), ("ownerPath", ownerPath), ("dryRun", dryRun));
        }

        private static bool Scannable(XmlEditResult result, string path, bool isKnowHowProtected, bool isConsistent)
        {
            if (isKnowHowProtected)
            {
                result.NotScanned.Add($"{path}: know-how protected");
            }
            else if (!isConsistent)
            {
                result.NotScanned.Add($"{path}: inconsistent, compile the software first");
            }

            return !isKnowHowProtected && isConsistent;
        }

        #endregion
    }
}
