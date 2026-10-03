# Handoff

## State (2026-10-03)
- Published: `Haitch.Roslyn` and `Haitch.Roslyn.Testing` **0.1.1** on nuget.org (tag `v0.1.1`, commit `d44e287`). The docs are live in `haitch-web` (`d526410`).
- Round 2 is done and reviewed, with high and medium findings fixed: issues 11.1–16.4. It is committed locally and not pushed or released. Last check: build has 0 warnings; `dotnet test` gives 592/592.
  - 11: `ForTypesWithAttribute(..., includeMembers)`. The sample reports SAMPLE005 for a user's own `ToString`/`TypeName`.
  - 12: attribute rendering (`WriteAttribute(s)`), `WellKnownAttributes`, and `Attribute()` on File/Namespace/Type scopes. Enums render as member names, arrays as typed, small ints with casts. Typed nulls render as `default(T)` because `(T)null` warns CS8600 under `#nullable enable`.
  - 13: `NewTypeModel`, `WriteNewTypeDeclaration`, and `NewType()` on scopes. Illegal kind/modifier/base/accessibility combinations are rejected, and top-level and nested rules differ.
  - 14: `EquatableArray<T>` is `IReadOnlyList<T>` and supports collection expressions via `[CollectionBuilder]` (from Polyfill). There is an identity `ToEquatableArray` overload.
  - 15: `CacheabilityOptions`.
    - `UnrelatedEditSourceIndex` appends `namespace HarnessUnrelatedEdit { }` to the given source.
    - `RequireRecomputationAfterTriviaEdit`: every output of each named step must be Cached or Unchanged, and at least one must be Unchanged. Attributing outputs to sources by value was rejected after experiments.
    - 6.5 and 7.2 tests are migrated.
  - 16: statement scopes on BodyScope, IfScope and TryScope: `If`/`ElseIf`/`Else`, `ForEach`/`For`/`While`/`Using`, `Try`/`Catch`/`Finally`, `Switch`/`Case`/`Default`. They share `Open*` helpers.

## Next
- Decide: release **0.2.0** (round 2 adds a public API in Testing plus source-only features). That means push `main`, wait for CI, then tag `v0.2.0`.
- Update the `haitch-web` docs for round 2 (attributes, NewType, statement scopes, `CacheabilityOptions`, collection expressions).
- `.claude/settings.json` and `.claude/hooks/no-bash-writes.sh` are not committed. They block in-place writes in `Bash` and in `mcp__rider__execute_terminal_command`. Commit them if they should be shared.

## Working rules learned
- Run one implementer at a time (shared working tree). Read-only Opus reviewers can run alongside it but must not build.
- Workers must show a *behavioural* red against a stub, not only a build error.
- Compile-the-output tests count warnings as failures under `#nullable enable`.
- Rider's reformat can strip `using Haitch.Roslyn.Types;` and restore stale buffers, so read the file back after `create_new_file`. The TUnit filter is `--treenode-filter`. Run `dotnet test` via `mcp__rider__execute_terminal_command` with `executeInShell: true`.

## Known limitations / follow-ups
- Scoped writer (documented caller errors):
  - a parent scope is writable while a child is open;
  - copying a scope and disposing both closes twice;
  - ElseIf/Else/Catch/Case with a nested block still open misnests;
  - a try with no catch or finally;
  - catch ordering (CS0160);
  - switch fall-through (CS0163/CS8070);
  - a dangling `Attribute()`; a rejected member after `Attribute()` leaves the attribute written.
- `TypeParameterModel` has no variance. Primary constructors and positional records are not supported by `NewType`. `AutoProperty` writes any model as an auto-property; ref/volatile are not modelled; explicit interface members are rejected.
- `MethodModel.From` takes no `CancellationToken`. Roslyn reports `IsSealed=false` for `sealed` interface members.
- `HintName.For` is case-insensitive in Roslyn, so names that differ only in case collide (documented).
- `AddEmbeddedAttributeDefinition` is shadowed by Roslyn 4.14+'s built-in instance method (documented in the web docs).
- The remaining old issue files predate the **Constraints** heading.
