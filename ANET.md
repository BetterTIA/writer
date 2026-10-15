# ANet fork (TIAC_Writer)

Branch `anet` of `heilingbrunner/tiaportal-mcp`, forked at `684969a`. Server version `0.2.4.0`.
Local only - nothing is pushed.

The fork adds what renaming objects in a real project needed and upstream did not have. All
additions live in files of their own (`*.AnetFork.cs`, `SimaticMl.cs`, `PathMatch.cs`,
`BatchRename.cs`, `Test9SimaticMl.cs`) so a merge from upstream stays trivial; the few edits to upstream files are
listed at the end.

## New tools (all but `CompileBlock` and `GetTechnologicalObjects` need `--allow-write`)

| Tool                        | How                    | Notes                                                                     |
| --------------------------- | ---------------------- | ------------------------------------------------------------------------- |
| `RenameBlockGroup`          | setter `Name`          | sibling group of the new name is rejected                                 |
| `RenameTypeGroup`           | setter `Name`          | same                                                                      |
| `RenameTagTableGroup`       | setter `Name`          | same                                                                      |
| `RenameTechnologicalObject` | setter `Name`          | by name; a block or technology object of the new name is rejected         |
| `SetNetworkText`            | export, edit, import   | title and/or comment of one network; `dryRun` defaults to true            |
| `ReplaceNetworkText`        | export, edit, import   | `find=replace` pairs in one pass; scope `networks` or `all` (block title/comment, GRAPH texts); `dryRun` |
| `RenameMembers`             | export, edit, import   | members of a block interface, DB or PLC data type plus references; `dryRun` |

| `RenameTags`                | setter `Name`, batch   | by name across all tables, swap without temp names, one transaction; `dryRun` |
| `RenameObjects`             | setter `Name`, batch   | blocks, technology objects and PLC data types by name, swap; `dryRun`     |
| `SetTagComment`             | setter                 | comment of one tag                                                        |
| `ReplaceTagComments`        | setter, batch          | `find=replace` pairs in one pass over a table or all tables; `dryRun`     |
| `RenameWatchTableGroup`     | setter `Name`          | sibling group of the new name is rejected                                 |
| `GetTechnologicalObjects`   | read                   | name, DB number and attributes of every technology object                 |
| `CompileBlock`              | `ICompilable`          | one block or one block group; refuses fail-safe blocks; names what is still inconsistent afterwards |

`RenameTagTable`, `UpdateTag`, `RenameWatchTable` and every tool that takes a tag table, tag or watch
table path now also find tables whose name contains `/` (upstream split the path at the last `/`).

`GetProject` no longer fails on projects whose attributes hold Openness objects: attribute values
that the JSON serializer cannot write are reported as text (`Helper.GetAttributeList`).

## Project archives (0.2.4.0)

`ArchiveProject` writes the open project as a compressed zap file (`Project.Archive` with
`ProjectArchivationMode.Compressed`) into an archive folder outside the project folder;
`PruneArchives` keeps every full archive and the newest three interim archives of a project and
deletes the older interim ones. Neither tool schedules itself: the agent or the BetterTIA Editor
calls them (before the first change of a session, after a series of accepted changes, at the end
of the day), and the operator confirms the version number - the tool never invents one.

| Name                                      | Kind      | Meaning                                                   |
| ----------------------------------------- | --------- | --------------------------------------------------------- |
| `<project>_V1.2.0.zap21`                  | `full`    | a fully working version; the version lives in the archive name only |
| `<project>_V1.2.0_2026-10-15_1430.zap21`  | `interim` | a checkpoint on the way to that version, date and time appended |

Rules (`ArchiveRules`, covered by `Test12Archive`): the version is `V<major>.<minor>.<patch>`;
an existing file is never overwritten; a project with unsaved changes is refused unless
`saveFirst=true` (TIA Portal archives the saved state); after the call the file must exist and be
larger than zero bytes, otherwise the call fails and names the files written in the last minutes.
`PruneArchives` considers only files whose name matches the pattern exactly and belongs to the
named project, never deletes a full archive, lists what would go by default, and deletes only with
`dryRun=false` and `delete=true`; every deletion is in the audit file. Archives of customer
projects stay on the machine - no path to them belongs into this repository.

Archiving a large project takes minutes; the MCP call returns when TIA Portal has written the
file (a client that backgrounds long calls still gets the result). Not verified against a running
TIA Portal: whether `Archive` accepts the file name with its extension, as the Openness
documentation shows, and whether a modified project is refused by TIA Portal itself.

## Write safeguards (0.2.3.0)

Three checks every write runs through, modelled on Czarnak/tia-portal-mcp (MIT):

- __Name collisions right before the write.__ A new name is checked against every symbol of the
  CPU - tags, user constants, blocks, system blocks, technology objects, PLC data types - not
  only against the namespace of the renamed object (`RenameTags` compared with other tags only,
  `RenameObjects` with blocks, technology objects and types only, the upstream `RenameBlock` and
  `RenameType` with nothing). The message names what holds the name. Applied in `RenameTags`,
  `RenameObjects`, `RenameTechnologicalObject`, `RenameBlock`, `RenameType`, `UpdateTag` (new
  name), `CreateTag`, `CreateUserConstant`, `CreateFB`, `CreateInstanceDB`; a dry run reports the
  collision as well, because the check runs before the plan.
- __Content hash on the import paths.__ `ExportXmlBlock` returns `ContentHash`
  (`xml:sha256:<hex>` over the exported file), `ExportAsDocuments` and `ExportBlocksAsDocuments`
  return `documents:sha256:<hex>` over the `.s7dcl` and `.s7res` files (per block in `Items`).
  `ImportXmlBlock`, `ImportFromDocuments`, `SetNetworkText`, `ReplaceNetworkText` and
  `RenameMembers` take `expectedContentHash`: right before the write the Writer exports the
  block (for `RenameMembers` the owner) again and refuses with `state_changed` when the hashes
  differ, when the block no longer exists, or when it is inconsistent now (TIA Portal exports no
  inconsistent block). Both sides are computed by the Writer over its own exports; a hash from
  another tool does not compare. The parameter is optional everywhere, for the callers that
  existed before; the BetterTIA Editor is meant to pass it always.
- __Audit.__ One JSON line per call of a write tool (every `[WriteTool]`, plus the `Import*` and
  `Save*` tools upstream registers unconditionally): time, tool, arguments with strings cut at
  200 characters (no block contents), `dryRun`, outcome and duration, written to
  `%LOCALAPPDATA%\BetterTIA\Writer\audit\writer-audit-<yyyy-MM>.jsonl` (or the directory in
  `TIAMCP_AUDIT_DIR`), append only. Refused and failed calls are recorded too. A record that
  cannot be written is logged and does not fail the call.

Verified without TIA Portal: `ContentHash` and the audit record (`Test11Safeguards`), build,
registration. Not verified against a running TIA Portal: whether two exports of an unchanged
block are byte-identical (the hash assumes it; if TIA Portal writes a timestamp into an export,
every comparison fails and the hash is useless - the first live test must check this), the
system block enumeration, and the cost of `CpuNames` on a large PLC (it reads every tag, block
and type once per rename).

## Hardware and network (0.2.2.0)

Carried over from the BetterTIA Reader (its `network_write`, modelled on Czarnak/tia-portal-mcp, MIT),
so the Writer stays the only tool that changes a project; the Reader keeps the catalog search
(`search_equipment_catalog`) and the hardware read (`read_hardware_config`). Every tool defaults to
`dryRun=true` and runs in one transaction, so a refused setting rolls the earlier ones of the same call back.

| Tool                                             | What it does                                                                                      |
| ------------------------------------------------ | ------------------------------------------------------------------------------------------------- |
| `AddDevice`                                      | device from a catalog `typeIdentifier` (`Devices.Create` / `CreateWithItem`); the real run returns the nodes with their `NodeId` |
| `ConfigureNode`                                  | IP address, subnet mask, PROFINET name, subnet (by `SubnetId`) and IO system (by number) of exactly one node, read back and verified |
| `CreateSubnet` / `UpdateSubnet` / `DeleteSubnet` | Ethernet and PROFIBUS subnets by `SubnetId`; delete disconnects the nodes and deletes no device   |

Selectors are exact: a device by its station (root) name, a node by the `NodeId` a previous read
reported, a subnet by its `SubnetId` attribute, an IO system by its number on the connected subnet.
Zero or several matches fail; nothing falls back to the first match or to a name. A PROFINET name on
a node that auto-generates it first sets `PnDeviceNameAutoGeneration=false` (the attribute is read-only
while it is true, seen live upstream). A node already connected to another subnet is disconnected
before `ConnectToSubnet` (upstream saw "already connected" otherwise).

Fail-safe hardware is told by `Failsafe_*` attributes on a device item (its F-parameters). A node
whose interface item, or an item above it, carries them is refused, and a created device that carries
them is deleted again and refused: safety hardware stays with the operator. F-modules elsewhere in the
station only produce a warning. There is no `allowFailsafe` for hardware.

Verified without TIA Portal: build, the input rules in `HardwareRules` (`Test10Hardware`), tool
registration. The Openness members used (`Node.ConnectToSubnet` / `DisconnectFromSubnet`,
`IoConnector.ConnectToIoSystem`, `Devices.CreateWithItem`, `Subnets.Create`, `Subnet.IoSystems`) were
checked against the V21 assemblies. Not verified against a running TIA Portal: everything else - in
particular the `PnDeviceNameAutoGeneration` sequence, the `Failsafe_` detection on real modules, and
whether `Node.Name` and the attributes read back the way the result expects.

## WinCC Unified (0.2.1.0)

Tools for the HMI software of a Unified device (`softwarePath` names the HMI software, not a PLC).
All of them use setters; every write tool defaults to `dryRun=true`.

| Tool                     | What it does                                                                                 |
| ------------------------ | -------------------------------------------------------------------------------------------- |
| `GetHmiTags`             | tags with table, connection, PLC tag, address; `unbound` = connection but no PLC tag/address |
| `SetHmiTagPlcTags`       | `hmiTag=plcPath` pairs; with `plcSoftwarePath` the start of each path is checked             |
| `RenameHmiTags`          | batch rename with swap; lists tag dynamizations and script lines that use the old names      |
| `RenameHmiScreen`        | renames a screen; lists script lines that hold its name as a string                          |
| `RenameHmiScreens`       | 0.2.1.1: batch with swap in one walk over the screens; `rewriteScripts` fixes the script lines |
| `ReplaceInHmiScripts`    | `find=replace` pairs in one pass over event handlers and script dynamizations                |
| `SetHmiItemAttribute`    | one attribute of a screen item or screen: name, text (one language), graphic, ...            |
| `SetHmiDynamizationTag`  | the tag of one tag dynamization                                                              |
| `CompileHmi`             | compiles the device (the HMI software offers no compiler itself)                             |

`RenameMembers` takes `hmiSoftwarePath`: the dry run then lists the HMI tags whose PLC path passes
a renamed member (`HmiDependents`) as ready-made assignments for `SetHmiTagPlcTags`. Seen live:
those tags are left with an empty PLC tag after the import, so the list has to be taken before.

Screens are walked through `IEngineeringObject` (compositions `Screens`, `ScreenGroups`,
`ScreenItems`, `Dynamizations`, `EventHandlers`, `PropertyEventHandlers`). Not covered: script
modules, and dynamizations on the parts of an item (button content, text part).

None of this was run against a TIA Portal when written. Open in particular: whether TIA Portal
follows an HMI tag rename in tag dynamizations by itself, the notation it expects for `PlcTag`,
and which object compiles a Unified device.

## What an import leaves inconsistent

An import replaces the whole block, and TIA Portal can mark its instance DB and its callers
inconsistent even when only a network comment changed (seen live: yes for one FB called from an OB,
no for another FB and an FC). So `SetNetworkText` and `ReplaceNetworkText` list, from the cross
references, the instance DBs of the block and what uses the block or those DBs (`Dependents`,
fail-safe ones in `FailsafeDependents`, which gate the real run like in `RenameMembers`). After a
real run every export -> edit -> import tool also returns `InconsistentAfter`: the blocks that are
inconsistent now and were not before - the fact, next to the prediction. `CompileBlock` reports the
other inconsistent blocks of the PLC as well, not only the ones it compiled.

## Smaller responses

`RenameTags`, `RenameObjects` and `RenameMembers` take `summaryOnly=true`: the response then holds the
counts, warnings, dependents and only the items that touch the safety program (for `RenameMembers` a
change count per object and network), and the complete result is written as JSON to
`%TEMP%\tiamcp-report-<tool>-<timestamp>.json`, whose path is returned as `ReportPath`.

## Fail-safe blocks

A block counts as fail-safe when its programming language is one of the `F_...` languages or when
a safety runtime group names it (F-OB, main safety block, its instance DB, the information DB).
The second rule exists because a know-how protected F-OB was not recognised by its language.

## Batch renames

`RenameTags` and `RenameObjects` take `currentName=newName` pairs. `BatchRename.Plan` orders the
setter calls: an object whose new name is still held by another object of the batch is parked
under a temporary name first, so swaps, rotations and chains work. A name taken by an object
outside the batch is rejected before anything is renamed.

Openness has no 'safety tag' attribute. The dry run therefore reads the cross references of every
object and of its members (they hang below `SourceObject.Children`; 0.2.0.2 read the top level only
and missed every use of a DB member) and lists the fail-safe blocks found there, in either
direction (`UsedByFailsafe`); `UserCount` counts `UsedBy` references only; such objects, and fail-safe
blocks themselves, are renamed only with `allowFailsafe=true`. When the cross references cannot be
read the use is reported as `unknown` and treated the same way.

Not done: renaming hardware (devices, device items). Whether the PROFINET device name follows the
device name depends on per-interface settings that could not be checked without a running TIA
Portal, and a changed PROFINET name takes the device off the bus until it is assigned again.

## Export, edit, import

Openness has no setter for network titles, network comments and member names. These tools export
the block (or, for `RenameMembers`, every block and PLC data type of the PLC) as SimaticML into a
temp directory, edit the XML (`Siemens/SimaticMl.cs`) and import it back with `Override`.

- `dryRun` is true by default: the call returns the objects that would be imported and every change
  (old text, new text, network) and imports nothing.
- Know-how protected owners are rejected. Know-how protected and inconsistent blocks cannot be
  exported; `RenameMembers` lists them under `NotScanned` - references inside them are not seen.
- Fail-safe blocks are reported with a warning and only imported with `allowFailsafe=true`.
- `RenameMembers` renames a reference only when its access path resolves, declaration by
  declaration, to the renamed member (through DBs, instance DBs, multi-instances, PLC data types,
  arrays and PLC tags of a PLC data type). What mentions an old name but cannot be resolved is
  returned under `Warnings`, never guessed.
- Several renames in one call are applied together, so a swap (`A=B; B=A`) works.
- A changed interface makes more blocks inconsistent than the ones that are edited: every data block
  built from the owner (instance DBs, DBs and types with a member of its type) and every block that
  reads one of those or calls the owner - also when it reads a member that was not renamed.
  `RenameMembers` lists them as `Dependents`; fail-safe blocks among them (`FailsafeDependents`,
  which also holds fail-safe blocks that could not be exported) make the real run need
  `allowFailsafe=true`, because the safety program then has to be compiled again. Found live with
  0.2.0.2, which reported nothing while an F-FB reading another member of the instance DB turned
  inconsistent.
- Not updated by `RenameMembers`: HMI tags, watch and force tables, technology object parameters,
  traces. Start values kept in other DBs for a renamed member, and its current values in the PLC,
  may be reset - back them up first.

## Verified / not verified

Verified without TIA Portal: build, `Test7ToolRegistration`, `Test8SourceFileEncoding` and
`Test9SimaticMl` (43 tests); the member rename additionally as an in-memory dry run on real V21 exports, where
its reference counts matched an independent count.

Seen working against TIA Portal V21 in a project with Safety and WinCC Unified (reported by the
session using it): reading a tag table with `/` in its name (0.2.0.1); the `GetProject` fix,
`RenameObjects` on several blocks including a GRAPH FB and instance DBs, its collision check, and
`CompileBlock` on single blocks and on a group (0.2.0.2).

Not verified against a running TIA Portal: everything else - `RenameTags`, the tag comment tools,
the network text tools, `RenameMembers`, and above all a positive case of `UsedByFailsafe` (an
object that a fail-safe block really uses). In particular whether
`Import(Override)` accepts every edited block, how instance DBs and start values react to a renamed
member, and import of fail-safe blocks.

## Running the tests that need no TIA Portal

```powershell
dotnet test tests/TiaMcpServer.Test/TiaMcpServer.Test.csproj -c Release --filter "FullyQualifiedName~Test7ToolRegistration|FullyQualifiedName~Test8SourceFileEncoding|FullyQualifiedName~Test9SimaticMl"
```

Do not run the unfiltered suite on a machine where a real project is open: the other test classes
connect to TIA Portal.

## Building while the server is in use

`bin\Release\net48\` may be the running MCP server. Build and test with `-c Debug`, then copy
`bin\Debug\net48\` to `dist\<version>\` (git-ignored locally) and point the MCP configuration
there. A Release build would overwrite, or fail on, the files of the running server.

## Edits to upstream files

- `Siemens/Portal.Tags.cs`: `GetTagTable` / `GetTag` / `GetWatchTable` fall back to whole-name
  matching; `RenameTagTable`, `UpdateTag` and `RenameWatchTable` accept a new name with `/`.
- `ModelContextProtocol/Helper.cs`: `GetAttributeList` reports non-serializable values as text.
- `Siemens/Portal.Blocks.cs`: `EnsureValidName(name, allowSlash)`.
- `ModelContextProtocol/McpServer.Tags.cs`: `RenameTagTable` / `UpdateTag` report the new path read
  back from the object instead of splitting the old path.
- `ModelContextProtocol/McpPrompts.cs`: class made `partial`.
- `tests/TiaMcpServer.Test/Test7ToolRegistration.cs`: registers `Engineering.Resolver`, without
  which the `BuildTools` tests fail with "Siemens.Engineering.Step7 not found".
- `Siemens/Portal.Blocks.cs`, `Siemens/Portal.Types.cs`, `Siemens/Portal.Tags.cs`: `RenameBlock`, `CreateFB`,
  `CreateInstanceDB`, `RenameType`, `CreateTag`, `UpdateTag`, `CreateUserConstant` call
  `EnsureCpuNameFree` (0.2.3.0).
- `ModelContextProtocol/McpServer.Blocks.cs`, `McpServer.Documents.cs`, `Responses.cs`: `ContentHash` on the
  XML and document exports, `expectedContentHash` on `ImportXmlBlock` and `ImportFromDocuments` (0.2.3.0).
- `Program.cs`: the `WriteAudit` call-tool filter and the tracking of the audited tools (0.2.3.0).
- `TiaMcpServer.csproj`: `AssemblyVersion` 0.2.3.0, so `--doctor` tells the fork from the original.
