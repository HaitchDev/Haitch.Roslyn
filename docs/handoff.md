# Handoff

## State (2026-10-02)
- Every planned issue is implemented and reviewed, with high and medium findings fixed: 0.1–0.8, 1.1–1.2, 2.1–2.5, 3.1–3.5, 4.1–4.2, 5.1–5.2, 6.1–6.6, 7.1–7.2, 8.1–8.2, 9.1–9.5, 10.1–10.7.
- 9.4/9.5: `.github/workflows/publish.yml` (tag `v*.*.*`, NuGet trusted publishing via `NUGET_USER`) and `ci.yml`. Validated by YAML parse and running their commands locally (`actionlint` not installed). They have never run on GitHub. The review found that the pack-based tests overwrote the shared `bin/Release` DLL with version 1.2.3; the tests now pack into an isolated `--artifacts-path`.
- Added during this session:
  - 0.7: the smoke consumer proves Polyfill types and records compile in a `netstandard2.0` consumer of the package.
  - 0.8: Roslyn bumped to 4.12.0 (the human's decision), so consumers' generators need 4.12+.
  - 6.6: the harness checks errors after generation.
  - 10.6 and 10.7: partial method and partial property implementation.
  - Slice 10, the typed scoped writer: `writer.File()` → `Using`/`Namespace` → `Type` → `Method`/`Field`/`AutoProperty`/`Property` → `BodyScope`/accessors.
- Last green: 364/364. Nothing committed.

## Held for the human
- First push (CI run) and first `v*` tag (publish): both are the human's call.
- Scoped-writer limitations, accepted and documented:
  - a parent scope can be written to while a child is open;
  - copying a scope and disposing both closes the block twice;
  - `Using` after `Namespace` emits invalid C#;
  - opening an accessor while another accessor's body is open nests it.
- Workers twice tried to write files via Bash (once blocked; once `sed -i` got through, and the edit was correct). Suggested: widen the repo hook to block in-place Bash writes.

## Known limitations / follow-ups
- `ForTypesWithAttribute` can't request members (`TypeModel.From(includeMembers: true)`), so the sample generator can't guard against a user's own `ToString`/`TypeName`.
- Roslyn reports `IsSealed=false` for `sealed` interface members, so models built from symbols lose it.
- `MethodModel.From` takes no `CancellationToken`.
- `AssertCacheable` can't express "edit an unrelated second file" or require strictly `Unchanged`; 6.5 and 7.2 keep hand-rolled reruns for those.
- The scoped writer's `Type(TypeModel)` renders partial declarations only; builders for new non-partial types would be a future issue. `AutoProperty` writes any model as an auto-property; ref/volatile are not modelled; explicit interface members are rejected.
- `HintName.For`: Roslyn compares hint names case-insensitively, so types differing only in case collide (documented).
- `AddEmbeddedAttributeDefinition` may raise CS0436 across InternalsVisibleTo assemblies (unverified on newer compilers).
- `SourceWriterExtensions` does not render attribute lists. `EquatableArray<T>` does not implement `IEnumerable<T>`.
- `CompilationHelper` pins C# 12 parse options; the partial-member tests use local C# 13 helpers.
- Tooling: Rider's test explorer fails to start, but `dotnet test` via `mcp__rider__execute_terminal_command` (`executeInShell: true`) works. The TUnit filter is `--treenode-filter`. Rider's reformat may strip `using Haitch.Roslyn.Types;` from tests, and can restore a stale buffer after `create_new_file`, so read the file back before reformatting.
- Remaining issue files predate the Working Agreement's **Constraints** heading (only 0.8, 6.6, 10.6 and 10.7 have it).

## Open questions for the human
- Commit now? (Recommended: yes. Nothing since `aa764ec` is committed.)
- `.editorconfig` with 2-space XML indentation? (Recommended.)
