# ANet fork (TIAC_Writer)

Branch `anet` of `heilingbrunner/tiaportal-mcp`, forked at `684969a`. Server version `0.2.0.3`.
Local only - nothing is pushed.

The fork adds what renaming objects in a real project needed and upstream did not have. All
additions live in files of their own (`*.AnetFork.cs`, `SimaticMl.cs`, `PathMatch.cs`,
`BatchRename.cs`, `Test9SimaticMl.cs`) so a merge from upstream stays trivial; the few edits to upstream files are
listed at the end.

## New tools (all but `CompileBlock` need `--allow-write`)

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
| `CompileBlock`              | `ICompilable`          | one block or one block group; refuses fail-safe blocks                    |

`RenameTagTable`, `UpdateTag`, `RenameWatchTable` and every tool that takes a tag table, tag or watch
table path now also find tables whose name contains `/` (upstream split the path at the last `/`).

`GetProject` no longer fails on projects whose attributes hold Openness objects: attribute values
that the JSON serializer cannot write are reported as text (`Helper.GetAttributeList`).

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
- `TiaMcpServer.csproj`: `AssemblyVersion` 0.2.0.3, so `--doctor` tells the fork from the original.
