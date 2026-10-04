# Spec: round 7 (0.6.1): bug fixes

## What and why
1. **Non-ordinary method kinds**: writing a constructor, destructor, operator or conversion `MethodModel` gives `void .ctor()`-style output that doesn't compile. The writer rejects them with a clear error instead.
2. **Partial method implementations**: writing the implementation part of a partial method drops `partial`, so it doesn't compile next to its definition. `MethodModel` records partial-ness and the writer keeps it.
3. **Suppressible diagnostic locations**: `LocationInfo.ToLocation` returns an external-file location, so `#pragma warning disable` cannot suppress a generator diagnostic. Diagnostics reported through `LocationInfo` land in the source tree.
4. A wrong comment in `PartialTypeValidation.cs` and the stale issues index are fixed alongside.

## Assumptions
- Bug fixes only: a bug is an accepted input with a wrong result, proven by a failing test. A missing API is not a bug.
- The failing probe tests from the round's verification step are the red for each issue.
- Roslyn 4.12 floor stays. No existing users, so breaking changes need no shims.
- Constructors and operators stay unsupported by the writer; rejection is the fix.

## Slices and issues
**Slice 35: Writer fixes.** Demo: a constructor model is rejected naming its kind, and a written partial implementation compiles next to its definition.
- 35.1 The writer rejects non-ordinary `MethodKind`s.
- 35.2 `MethodModel.IsPartial`, and the writer keeps `partial` on implementation parts.

**Slice 36: Suppressible locations.** Demo: a `#pragma warning disable` suppresses a diagnostic reported through `LocationInfo`.
- 36.1 `LocationInfo.ToLocation(Compilation)` binds to the source tree, and `ReportDiagnostics` uses it by combining only its diagnostics branch with `CompilationProvider` (an Opus design check measured stored trees as stale or cache-breaking).
