# Spec: round 6 (0.6.0): explicit usings, partial events, members on containing types

## What and why
1. **Explicit usings in Testing**: `Haitch.Roslyn.Testing` turns `ImplicitUsings` off, so both shipped projects follow the same rule.
2. **Partial events**: a C# 14 partial event becomes one `EventModel` with `IsPartial = true`, as partial methods and properties do, instead of being untested.
3. **Members on containing types**: member discovery (`ForFieldsWithAttribute`, `ForPropertiesWithAttribute`, `ForMethodsWithAttribute`) can include the containing type's members, and `TypeModel` carries every member name for clash checks, so the Notify sample drops its owners step.

## Assumptions
- Version 0.6.0. Roslyn 4.12 floor stays, so no code in `src/` may call a Roslyn API newer than 4.12. Partial-event tests live in `tests/Haitch.Roslyn.CSharp15.Tests` (Roslyn 5.9).
- There are no existing users. Breaking changes are fine and need no shims.
- A partial event is never field-like, because its implementation must declare accessors.
- Including containing-type members is opt-in (default `false`), because it ties the step's equality to every member edit.
- `TypeModel.MemberNames` includes nested types, indexers and compiler-made members, because a generated member can clash with any of them.
- Each issue is test-first with a behavioural red against a stub. One implementer at a time. `tools/test-dotnet11.sh` runs before the release.

## Slices and issues
**Slice 32: Explicit usings.** Demo: the Testing csproj has no `ImplicitUsings`, and the check passes.
- 32.1 Turn `ImplicitUsings` off in `Haitch.Roslyn.Testing` and add explicit usings.

**Slice 33: Partial events.** Demo: a partial event declared in two parts yields one `EventModel` with `IsPartial = true`.
- 33.1 `EventModel.IsPartial` and one model per partial event in `TypeModel.From`.

**Slice 34: Members on containing types.** Demo: the Notify sample has no owners step, and its output is byte-identical.
- 34.1 An `includeContainingTypeMembers` option on member discovery.
- 34.2 `TypeModel.MemberNames` (added after 34.3's first attempt found `TypeModel` lacked nested-type and indexer names).
- 34.3 The Notify sample uses both and deletes its owners step.

Then: release 0.6.0 and update the haitch-web docs.
