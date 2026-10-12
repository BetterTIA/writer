# ANet fork (TIAC_Writer)

Branch `anet` of `heilingbrunner/tiaportal-mcp`, forked at `684969a`. Server version `0.2.0.1`.
Local only - nothing is pushed.

The fork adds what renaming objects in a real project needed and upstream did not have. All
additions live in files of their own (`*.AnetFork.cs`, `SimaticMl.cs`, `PathMatch.cs`,
`Test9SimaticMl.cs`) so a merge from upstream stays trivial; the few edits to upstream files are
listed at the end.

## New tools (all need `--allow-write`)

| Tool                        | How                    | Notes                                                                     |
| --------------------------- | ---------------------- | ------------------------------------------------------------------------- |
| `RenameBlockGroup`          | setter `Name`          | sibling group of the new name is rejected                                 |
| `RenameTypeGroup`           | setter `Name`          | same                                                                      |
| `RenameTagTableGroup`       | setter `Name`          | same                                                                      |
| `RenameTechnologicalObject` | setter `Name`          | by name; a block or technology object of the new name is rejected         |
| `SetNetworkText`            | export, edit, import   | title and/or comment of one network; `dryRun` defaults to true            |
| `ReplaceNetworkText`        | export, edit, import   | literal replace in all network titles and comments of a block; `dryRun`   |
| `RenameMembers`             | export, edit, import   | members of a block interface, DB or PLC data type plus references; `dryRun` |

`RenameTagTable`, `UpdateTag` and every tool that takes a `tagTablePath` or `tagPath` now also find
tag tables whose name contains `/` (upstream split the path at the last `/`).

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
`Test9SimaticMl`; the member rename additionally as an in-memory dry run on real V21 exports, where
its reference counts matched an independent count.

Not verified: any of the new tools against a running TIA Portal. In particular whether
`Import(Override)` accepts every edited block, how instance DBs and start values react to a renamed
member, and import of fail-safe blocks.

## Running the tests that need no TIA Portal

```powershell
dotnet test tests/TiaMcpServer.Test/TiaMcpServer.Test.csproj -c Release --filter "FullyQualifiedName~Test7ToolRegistration|FullyQualifiedName~Test8SourceFileEncoding|FullyQualifiedName~Test9SimaticMl"
```

Do not run the unfiltered suite on a machine where a real project is open: the other test classes
connect to TIA Portal.

## Edits to upstream files

- `Siemens/Portal.Tags.cs`: `GetTagTable` / `GetTag` fall back to whole-name matching;
  `RenameTagTable` and `UpdateTag` accept a new name with `/`.
- `Siemens/Portal.Blocks.cs`: `EnsureValidName(name, allowSlash)`.
- `ModelContextProtocol/McpServer.Tags.cs`: `RenameTagTable` / `UpdateTag` report the new path read
  back from the object instead of splitting the old path.
- `ModelContextProtocol/McpPrompts.cs`: class made `partial`.
- `tests/TiaMcpServer.Test/Test7ToolRegistration.cs`: registers `Engineering.Resolver`, without
  which the `BuildTools` tests fail with "Siemens.Engineering.Step7 not found".
- `TiaMcpServer.csproj`: `AssemblyVersion` 0.2.0.1, so `--doctor` tells the fork from the original.
