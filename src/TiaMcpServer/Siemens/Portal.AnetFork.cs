using Microsoft.Extensions.Logging;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.Safety;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.WatchAndForceTables;
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

        /// <summary>Objects that are not edited but read the changed interface, so they turn inconsistent.</summary>
        public List<string> Dependents { get; set; } = new List<string>();

        /// <summary>
        /// The fail-safe blocks among <see cref="Dependents"/>, plus fail-safe blocks that could
        /// not be exported and whose dependence is therefore unknown. They are not imported, but
        /// the safety program has to be compiled again because of them.
        /// </summary>
        public List<string> FailsafeDependents { get; set; } = new List<string>();

        /// <summary>
        /// HMI tags whose PLC path passes a renamed member, as "hmiTag=newPlcPath" assignments for
        /// SetHmiTagPlcTags. Only filled when an HMI software path was given.
        /// </summary>
        public List<string> HmiDependents { get; set; } = new List<string>();

        /// <summary>
        /// After a real run: the blocks that are inconsistent now and were not before - what TIA
        /// Portal actually did, as opposed to the prediction in <see cref="Dependents"/>.
        /// Fail-safe blocks carry the suffix " (fail-safe)".
        /// </summary>
        public List<string> InconsistentAfter { get; set; } = new List<string>();
    }

    /// <summary>One object of a batch rename, with what the rename would touch in the safety program.</summary>
    public sealed class BatchRenameItem
    {
        /// <summary>'tag', 'block', 'type' or 'technologicalObject'.</summary>
        public string Kind { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public string OldName { get; set; } = string.Empty;

        public string NewName { get; set; } = string.Empty;

        /// <summary>True when the object itself is a fail-safe block.</summary>
        public bool Failsafe { get; set; }

        /// <summary>
        /// Fail-safe blocks found in the cross references of the object or of any of its members,
        /// in either direction. Openness has no 'safety tag' attribute, so this is what marks an
        /// object as safety relevant; it errs towards listing a block too many.
        /// </summary>
        public List<string> UsedByFailsafe { get; set; } = new List<string>();

        /// <summary>Number of objects that use it or one of its members ('UsedBy' references only).</summary>
        public int UserCount { get; set; }
    }

    public sealed class BatchRenameResult
    {
        public bool Applied { get; set; }

        public List<BatchRenameItem> Items { get; set; } = new List<BatchRenameItem>();

        public List<string> Warnings { get; set; } = new List<string>();
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

        public List<TechnologicalInstanceDB> GetTechnologicalObjects(string softwarePath)
        {
            return Operation.Run(_logger, nameof(GetTechnologicalObjects), PortalErrorCode.NotFound,
                () =>
                {
                    var objects = new List<TechnologicalInstanceDB>();

                    WalkRecursive<TechnologicalInstanceDBGroup, TechnologicalInstanceDB>(
                        GetPlcSoftwareOrThrow(softwarePath).TechnologicalObjectGroup,
                        objects, g => g.TechnologicalObjects, g => g.Groups, t => t.Name);

                    return objects;
                },
                ("softwarePath", softwarePath));
        }

        public bool RenameWatchTableGroup(string softwarePath, string groupPath, string newName)
        {
            return Operation.Run(_logger, nameof(RenameWatchTableGroup), PortalErrorCode.RenameFailed,
                () =>
                {
                    EnsureValidName(newName);

                    var group = GetWatchTableGroupByPath(softwarePath, groupPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Watch table group not found at '{groupPath}'. Use 'GetSoftwareTree' to discover valid group paths.");

                    var userGroup = group as PlcWatchAndForceTableUserGroup
                        ?? throw new PortalException(PortalErrorCode.NotSupported,
                            $"'{groupPath}' is the system group 'Watch and force tables', which cannot be renamed.");

                    EnsureFreeName(((PlcWatchAndForceTableGroup)userGroup.Parent).Groups.Select(g => g.Name), userGroup.Name, newName, "watch table group");
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

                    var objects = GetTechnologicalObjects(softwarePath);

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

        /// <summary>
        /// Blocks the safety runtime groups name: the F-OB, the main safety block, its instance
        /// DB and the F-runtime-group information DB. Refreshed by LoadSafetyBlocks at the start
        /// of every operation that asks IsFailsafe.
        /// </summary>
        private HashSet<string> _safetyBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The programming language alone does not mark every block of the safety program: seen
        /// live, a know-how protected F-OB was not recognised by it. The runtime groups of the
        /// safety administration name those blocks explicitly.
        /// </summary>
        private void LoadSafetyBlocks(string softwarePath)
        {
            _safetyBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var administration = (GetSoftwareContainer(softwarePath)?.Parent as DeviceItem)?.GetService<SafetyAdministration>();

                if (administration == null)
                {
                    return;
                }

                foreach (var group in administration.RuntimeGroups)
                {
                    foreach (var name in new[] { group.FOBName, group.MainSafetyBlockName, group.MainSafetyBlockIDbName, group.InfoDbName })
                    {
                        if (!string.IsNullOrEmpty(name))
                        {
                            _safetyBlocks.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // A PLC without a safety program, or one that refuses the service: the language check still applies.
                _logger?.LogDebug(ex, "Safety runtime groups could not be read for {SoftwarePath}", softwarePath);
            }
        }

        // F_LAD, F_FBD, F_DB, F_CALL, ...: every fail-safe language of the enum carries this prefix.
        private bool IsFailsafe(PlcBlock block) =>
            block.ProgrammingLanguage.ToString().StartsWith("F_", StringComparison.Ordinal) || _safetyBlocks.Contains(block.Name);

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
        private XmlEditResult Finish(string softwarePath, XmlEditResult result, List<Exported> edited, bool dryRun, bool allowFailsafe)
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

            if (result.FailsafeDependents.Count > 0)
            {
                result.Warnings.Add(
                    "Fail-safe block(s) depend on what is imported: " + string.Join(", ", result.FailsafeDependents) +
                    ". They are not edited, but they can turn inconsistent, and then the safety program has to be compiled and accepted again by the operator.");
            }

            if (dryRun || edited.Count == 0)
            {
                return result;
            }

            if (!allowFailsafe && (edited.Any(e => e.Info.Failsafe) || result.FailsafeDependents.Count > 0))
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    "This would change the safety program through fail-safe block(s): " +
                    string.Join(", ", edited.Where(e => e.Info.Failsafe).Select(e => e.Info.Path).Concat(result.FailsafeDependents)) +
                    ". Review the dry run with the operator and repeat with allowFailsafe=true if that is intended.");
            }

            var before = new HashSet<string>(Inconsistent(softwarePath), StringComparer.OrdinalIgnoreCase);

            foreach (var item in edited)
            {
                item.Doc.Save(item.File.FullName, SaveOptions.DisableFormatting);
                item.Import();
            }

            result.Applied = true;
            result.InconsistentAfter = Inconsistent(softwarePath).Where(p => !before.Contains(p)).ToList();

            return result;
        }

        /// <summary>Paths of the blocks that are inconsistent right now, fail-safe ones marked.</summary>
        private List<string> Inconsistent(string softwarePath) =>
            GetBlocks(softwarePath).Where(b => !b.IsConsistent)
                .Select(b => GetBlockPath(b) + (IsFailsafe(b) ? " (fail-safe)" : string.Empty)).ToList();

        /// <summary>
        /// What an import of <paramref name="block"/> can make inconsistent although its interface
        /// stays the same: its instance DBs and whatever uses the block or one of those DBs. Seen
        /// live after a comment-only import - the instance DB and the calling OB were inconsistent.
        /// From the cross references, so it needs no export of other blocks.
        /// </summary>
        private void AddUsers(string softwarePath, PlcBlock block, XmlEditResult result)
        {
            var blocks = GetBlocks(softwarePath);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var source in new[] { block }.Concat(blocks.OfType<InstanceDB>().Where(d => block.Name.Equals(d.InstanceOfName, StringComparison.OrdinalIgnoreCase))))
                {
                    names.Add(source.Name);

                    foreach (var references in source.GetService<CrossReferenceService>()?.GetCrossReferences(CrossReferenceFilter.AllObjects).Sources.ToList() ?? new List<SourceObject>())
                    {
                        CollectReferences(references, ignored, names);
                    }
                }
            }
            catch (Exception ex)
            {
                // Not knowing must not read as "the safety program is not affected".
                result.Warnings.Add($"The users of '{block.Name}' could not be read from the cross references ({ex.Message}).");
                result.FailsafeDependents.Add("unknown (cross references could not be read)");
            }

            names.Remove(block.Name);

            var users = blocks.Where(b => names.Contains(b.Name)).ToList();

            result.Dependents = users.Select(GetBlockPath).ToList();
            result.FailsafeDependents.AddRange(users.Where(IsFailsafe).Select(GetBlockPath));

            if (users.Count > 0)
            {
                result.Warnings.Add(
                    $"{users.Count} object(s) use this block or its instance DB and may be inconsistent after the import (see Dependents); compile them afterwards.");
            }
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
                    LoadSafetyBlocks(softwarePath);

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

                        if (result.Changes.Count == 0)
                        {
                            return result;
                        }

                        AddUsers(softwarePath, block, result);

                        return Finish(softwarePath, result, new List<Exported> { exported }, dryRun, allowFailsafe);
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
            string softwarePath, string ownerPath, bool ownerIsType, IDictionary<string, string> renames, bool dryRun = true, bool allowFailsafe = false,
            string hmiSoftwarePath = "")
        {
            return Operation.Run(_logger, nameof(RenameMembers), PortalErrorCode.ImportFailed,
                () =>
                {
                    if (renames.Count == 0)
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams, "No rename was given.");
                    }

                    LoadSafetyBlocks(softwarePath);

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
                            else if (IsFailsafe(block))
                            {
                                // Not knowing must not read as "the safety program is not affected".
                                result.FailsafeDependents.Add($"{GetBlockPath(block)} (not exported, dependence unknown)");
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

                        var dependents = exported.Where(e => renamed.Dependents.Contains(e.Name)).ToList();

                        if (!string.IsNullOrEmpty(hmiSoftwarePath))
                        {
                            var blocks = new HashSet<string>(renamed.Dependents.Concat(renamed.Changed), StringComparer.OrdinalIgnoreCase) { ownerName };

                            result.HmiDependents = HmiDependents(hmiSoftwarePath, blocks, renames);

                            if (result.HmiDependents.Count > 0)
                            {
                                result.Warnings.Add(
                                    $"{result.HmiDependents.Count} HMI tag(s) point at a renamed member and lose their PLC tag through the import " +
                                    "(see HmiDependents); keep that list and pass it to 'SetHmiTagPlcTags' afterwards.");
                            }
                        }

                        result.Dependents = dependents.Select(e => e.Info.Path).ToList();
                        result.FailsafeDependents.InsertRange(0, dependents.Where(e => e.Info.Failsafe).Select(e => e.Info.Path));

                        if (dependents.Count > 0)
                        {
                            result.Warnings.Add(
                                $"{dependents.Count} further object(s) read the changed interface and will be inconsistent until compiled (see Dependents).");
                        }

                        // The declaration has to exist before anything that refers to it is imported.
                        var edited = exported
                            .Where(e => renamed.Changed.Contains(e.Name))
                            .OrderBy(e => e.Name.Equals(ownerName, StringComparison.OrdinalIgnoreCase) ? 0 : e.Info.Kind == "type" ? 1 : 2)
                            .ToList();

                        return Finish(softwarePath, result, edited, dryRun, allowFailsafe);
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

        #region batch rename

        /// <summary>An object of a batch: what to report, how to rename it, where its cross references come from.</summary>
        private sealed class Renamable
        {
            public BatchRenameItem Info = new BatchRenameItem();
            public Action<string> SetName = _ => { };
            public object? Source;
        }

        /// <summary>
        /// Collects the names in the cross references of <paramref name="source"/> and of its
        /// members. References to a member of a DB hang below SourceObject.Children, not on the
        /// DB itself, so reading only the top level misses every use of a member - seen live as
        /// an instance DB reporting fewer references than there were blocks using it.
        /// </summary>
        /// <param name="all">Every referenced name, whatever the direction of the reference.</param>
        /// <param name="users">Only the names with a 'UsedBy' location.</param>
        private static void CollectReferences(SourceObject source, HashSet<string> all, HashSet<string> users)
        {
            foreach (var reference in source.References)
            {
                all.Add(reference.Name);

                if (reference.Locations.Any(l => l.ReferenceType == ReferenceType.UsedBy))
                {
                    users.Add(reference.Name);
                }
            }

            foreach (var child in source.Children)
            {
                CollectReferences(child, all, users);
            }
        }

        /// <summary>
        /// Shared tail of the batch renames: plans the order (BatchRename.Plan), reports which
        /// fail-safe blocks each object is used by and, unless this is a dry run, sets the names.
        /// </summary>
        private BatchRenameResult RenameBatch(string softwarePath, List<Renamable> items, IEnumerable<string> allNames, bool dryRun, bool allowFailsafe)
        {
            var steps = BatchRename.Plan(
                items.Select(i => i.Info.OldName).ToList(), items.Select(i => i.Info.NewName).ToList(), allNames);

            LoadSafetyBlocks(softwarePath);
            var failsafeBlocks = new HashSet<string>(GetBlocks(softwarePath).Where(IsFailsafe).Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            var result = new BatchRenameResult { Items = items.Select(i => i.Info).ToList() };

            foreach (var item in items)
            {
                try
                {
                    var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var service = (item.Source as IEngineeringServiceProvider)?.GetService<CrossReferenceService>();

                    foreach (var source in service?.GetCrossReferences(CrossReferenceFilter.AllObjects).Sources.ToList() ?? new List<SourceObject>())
                    {
                        CollectReferences(source, all, users);
                    }

                    item.Info.UserCount = users.Count;
                    item.Info.UsedByFailsafe = all.Where(failsafeBlocks.Contains).ToList();
                }
                catch (Exception ex)
                {
                    // Not knowing must not read as "not used by the safety program".
                    item.Info.UsedByFailsafe = new List<string> { "unknown" };
                    result.Warnings.Add($"'{item.Info.OldName}': cross references could not be read ({ex.Message}), so its use in fail-safe blocks is unknown.");
                }
            }

            var safety = items.Where(i => i.Info.Failsafe || i.Info.UsedByFailsafe.Count > 0).Select(i => i.Info.OldName).ToList();

            if (safety.Count > 0)
            {
                result.Warnings.Add(
                    $"{safety.Count} object(s) are fail-safe blocks or used by fail-safe blocks: {string.Join(", ", safety)}. " +
                    "Renaming them changes the safety program, which then has to be compiled and accepted again by the operator.");
            }

            if (dryRun || steps.Count == 0)
            {
                return result;
            }

            if (safety.Count > 0 && !allowFailsafe)
            {
                throw new PortalException(PortalErrorCode.InvalidState,
                    $"This would change the safety program through: {string.Join(", ", safety)}. Review the dry run with the operator and " +
                    "repeat with allowFailsafe=true if that is intended, or leave those objects out of the batch.");
            }

            foreach (var (index, name) in steps)
            {
                items[index].SetName(name);
            }

            result.Applied = true;

            return result;
        }

        /// <summary>
        /// Renames PLC tags by name, across all tag tables, in one transaction. The new names may
        /// be names other tags of the batch hold now, so a swap needs no temporary name from the
        /// caller. Tag names are unique within a PLC, which is why no table path is needed.
        /// </summary>
        /// <param name="renames">Current tag name to new tag name.</param>
        public BatchRenameResult RenameTags(string softwarePath, IDictionary<string, string> renames, bool dryRun = true, bool allowFailsafe = false)
        {
            return Operation.Run(_logger, nameof(RenameTags), PortalErrorCode.RenameFailed,
                () =>
                {
                    var tags = GetTags(softwarePath);
                    var items = new List<Renamable>();
                    var missing = new List<string>();

                    foreach (var rename in renames)
                    {
                        EnsureValidName(rename.Value, allowSlash: true);

                        var tag = tags.FirstOrDefault(t => t.Name.Equals(rename.Key, StringComparison.OrdinalIgnoreCase));

                        if (tag == null)
                        {
                            missing.Add(rename.Key);
                            continue;
                        }

                        items.Add(new Renamable
                        {
                            Info = new BatchRenameItem
                            {
                                Kind = "tag",
                                Path = tag.Parent is PlcTagTable table ? $"{GetTagTablePath(table)}/{tag.Name}" : tag.Name,
                                OldName = tag.Name,
                                NewName = rename.Value
                            },
                            SetName = name => tag.Name = name,
                            Source = tag
                        });
                    }

                    if (missing.Count > 0)
                    {
                        throw new PortalException(PortalErrorCode.NotFound,
                            $"No PLC tag named: {string.Join(", ", missing)}. Use 'GetTags' to list the available tags.");
                    }

                    return RenameBatch(softwarePath, items, tags.Select(t => t.Name), dryRun, allowFailsafe);
                },
                ("softwarePath", softwarePath), ("count", renames.Count), ("dryRun", dryRun));
        }

        /// <summary>
        /// Renames blocks, technology objects and PLC data types by name in one transaction, with
        /// the same swap support as <see cref="RenameTags"/>. A name is looked up among the
        /// blocks first, then the technology objects, then the types.
        /// </summary>
        public BatchRenameResult RenameObjects(string softwarePath, IDictionary<string, string> renames, bool dryRun = true, bool allowFailsafe = false)
        {
            return Operation.Run(_logger, nameof(RenameObjects), PortalErrorCode.RenameFailed,
                () =>
                {
                    LoadSafetyBlocks(softwarePath);

                    var blocks = GetBlocks(softwarePath);
                    var objects = GetTechnologicalObjects(softwarePath);
                    var types = GetTypes(softwarePath);
                    var items = new List<Renamable>();
                    var missing = new List<string>();

                    foreach (var rename in renames)
                    {
                        EnsureValidName(rename.Value);

                        bool Named(string name) => name.Equals(rename.Key, StringComparison.OrdinalIgnoreCase);

                        var info = new BatchRenameItem { NewName = rename.Value };

                        if (blocks.FirstOrDefault(b => Named(b.Name)) is PlcBlock block)
                        {
                            EnsureNotKnowHowProtected(block);
                            info.Kind = "block";
                            info.Path = GetBlockPath(block);
                            info.OldName = block.Name;
                            info.Failsafe = IsFailsafe(block);
                            items.Add(new Renamable { Info = info, SetName = name => block.Name = name, Source = block });
                        }
                        else if (objects.FirstOrDefault(t => Named(t.Name)) is TechnologicalInstanceDB technologicalObject)
                        {
                            info.Kind = "technologicalObject";
                            info.Path = technologicalObject.Name;
                            info.OldName = technologicalObject.Name;
                            items.Add(new Renamable { Info = info, SetName = name => technologicalObject.Name = name, Source = technologicalObject });
                        }
                        else if (types.FirstOrDefault(t => Named(t.Name)) is PlcType type)
                        {
                            EnsureNotKnowHowProtected(type);
                            info.Kind = "type";
                            info.Path = GetTypePath(type);
                            info.OldName = type.Name;
                            items.Add(new Renamable { Info = info, SetName = name => type.Name = name, Source = type });
                        }
                        else
                        {
                            missing.Add(rename.Key);
                        }
                    }

                    if (missing.Count > 0)
                    {
                        throw new PortalException(PortalErrorCode.NotFound,
                            $"No block, technology object or PLC data type named: {string.Join(", ", missing)}.");
                    }

                    return RenameBatch(softwarePath, items,
                        blocks.Select(b => b.Name).Concat(objects.Select(t => t.Name)).Concat(types.Select(t => t.Name)),
                        dryRun, allowFailsafe);
                },
                ("softwarePath", softwarePath), ("count", renames.Count), ("dryRun", dryRun));
        }

        #endregion

        #region tag comments

        /// <summary>
        /// Runs <paramref name="edit"/> over the comment of every tag of a table (or of all
        /// tables) and writes back what it changed. A comment has one text per project language.
        /// </summary>
        /// <param name="tagTablePath">Empty covers every tag table of the PLC.</param>
        /// <param name="culture">Empty covers every language.</param>
        public List<XmlChange> EditTagComments(string softwarePath, string tagTablePath, Func<string, string> edit, string culture = "", bool dryRun = true)
        {
            return Operation.Run(_logger, nameof(EditTagComments), PortalErrorCode.RenameFailed,
                () => EditComments(GetTags(softwarePath, tagTablePath), edit, culture, dryRun),
                ("softwarePath", softwarePath), ("tagTablePath", tagTablePath), ("dryRun", dryRun));
        }

        public List<XmlChange> SetTagComment(string softwarePath, string tagPath, string comment, string culture = "")
        {
            return Operation.Run(_logger, nameof(SetTagComment), PortalErrorCode.RenameFailed,
                () =>
                {
                    var tag = GetTag(softwarePath, tagPath)
                        ?? throw new PortalException(PortalErrorCode.NotFound,
                            $"Tag not found at '{tagPath}'. Use 'GetTags' to list the available tags.");

                    var cultures = tag.Comment.Items.Select(i => i.Language.Culture.Name).ToList();

                    if (string.IsNullOrEmpty(culture) ? cultures.Count != 1 : !cultures.Contains(culture, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new PortalException(PortalErrorCode.InvalidParams,
                            $"The comment exists in {cultures.Count} language(s) ({string.Join(", ", cultures)}); pass one of them as 'culture'.");
                    }

                    return EditComments(new[] { tag }, _ => comment, culture, false);
                },
                ("softwarePath", softwarePath), ("tagPath", tagPath));
        }

        private static List<XmlChange> EditComments(IEnumerable<PlcTag> tags, Func<string, string> edit, string culture, bool dryRun)
        {
            var changes = new List<XmlChange>();

            foreach (var tag in tags)
            {
                foreach (var item in tag.Comment.Items)
                {
                    var language = item.Language.Culture.Name;
                    var oldText = item.Text ?? string.Empty;

                    if (!string.IsNullOrEmpty(culture) && !language.Equals(culture, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var newText = edit(oldText);

                    if (newText == oldText)
                    {
                        continue;
                    }

                    changes.Add(new XmlChange { Object = tag.Name, Where = $"comment [{language}]", Old = oldText, New = newText });

                    if (!dryRun)
                    {
                        item.Text = newText;
                    }
                }
            }

            return changes;
        }

        #endregion

        #region compile

        /// <summary>
        /// Compiles one block, or every block of one block group, instead of the whole PLC
        /// software - enough to make the callers of a renamed block consistent again. Fail-safe
        /// blocks are refused: compiling the safety program stays with the operator.
        /// </summary>
        /// <param name="path">Root-relative path of a block or of a block group.</param>
        /// <returns>
        /// The compiler result, the compiled blocks that are still inconsistent afterwards, and
        /// every other inconsistent block of the PLC. The compiler can report success and leave a
        /// block inconsistent - seen live on a caller of renamed technology objects - and the
        /// instance DB and callers of a compiled block are not covered by its compile at all.
        /// </returns>
        public (CompilerResult Result, List<string> Inconsistent, List<string> OtherInconsistent) CompileBlocks(string softwarePath, string path)
        {
            return Operation.Run(_logger, nameof(CompileBlocks), PortalErrorCode.InvalidState,
                () =>
                {
                    IEngineeringServiceProvider target;
                    var covered = new List<PlcBlock>();

                    LoadSafetyBlocks(softwarePath);

                    if (GetBlock(softwarePath, path) is PlcBlock block)
                    {
                        target = block;
                        covered.Add(block);
                    }
                    else
                    {
                        var group = GetPlcBlockGroupByPath(softwarePath, path)
                            ?? throw new PortalException(PortalErrorCode.NotFound,
                                $"No block or block group found at '{path}'. Use 'GetSoftwareTree' to discover valid paths.");

                        target = group;
                        GetBlocksRecursive(group, covered);
                    }

                    var failsafe = covered.Where(IsFailsafe).Select(GetBlockPath).ToList();

                    if (failsafe.Count > 0)
                    {
                        throw new PortalException(PortalErrorCode.NotSupported,
                            $"'{path}' covers fail-safe block(s): {string.Join(", ", failsafe)}. Compiling the safety program is left to the operator; " +
                            "compile the standard blocks one by one instead.");
                    }

                    var compiler = target.GetService<ICompilable>()
                        ?? throw new PortalException(PortalErrorCode.NotSupported, $"'{path}' cannot be compiled on its own.");

                    var result = compiler.Compile();
                    var own = covered.Where(b => !b.IsConsistent).Select(GetBlockPath).ToList();

                    return (result, own, Inconsistent(softwarePath).Where(p => !own.Contains(p)).ToList());
                },
                ("softwarePath", softwarePath), ("path", path));
        }

        #endregion
    }
}
