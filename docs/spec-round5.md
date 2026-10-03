# Spec: round 5 (0.5.0): close the harness and writer gaps

## What and why
1. **Raw lines in types**: `TypeScope.Line(string)` writes raw text (such as `#pragma`) at the current indent, so generators never reach past a scope to `SourceWriter`.
2. **Realistic cacheability**: every `AssertCacheable` rerun uses a new, equal options provider, as the IDE does, so generators that compare providers by reference fail.
3. **Any additional text**: the harness takes any `AdditionalText`, including unreadable ones, so no test needs to drive `CSharpGeneratorDriver` itself.
4. **Migration**: direct-driver tests move onto the harness, the Notify sample's `#pragma` lines move to `TypeScope.Line`, and the 0.3 delegating overloads are deleted.

## Assumptions
- Version 0.5.0. Roslyn 4.12 floor stays. There are no existing users, so breaking changes to the testing package are fine; no compatibility shims.
- `TypeScope.Line` behaves exactly like `SourceWriter.WriteLine` called inside the type scope today, so the Notify sample's output stays byte-identical.
- Reruns always use a new provider; there is no option to keep the old instance.
- `HarnessAdditionalText` becomes a class deriving from `AdditionalText`; `GeneratorHarnessInput.AdditionalTexts` becomes `IReadOnlyList<AdditionalText>?`; per-file options still match by path.
- The testing package ships an `UnreadableAdditionalText(path)` whose `GetText` returns null.
- Each issue is test-first with a behavioural red against a stub. One implementer at a time. `tools/test-dotnet11.sh` runs before the release.

## Slices and issues
**Slice 28: Raw lines on TypeScope.** Demo: the Notify sample's output is byte-identical with no direct `SourceWriter` call.
- 28.1 `TypeScope.Line(string)`.
- 28.2 The Notify sample writes its `CS0067` pragmas through `scope.Line`.

**Slice 29: New provider on rerun.** Demo: `AssertCacheable` fails a generator that compares options providers by reference and passes `ForAdditionalFiles`.
- 29.1 Every `AssertCacheable` rerun builds a new, equal `HarnessAnalyzerConfigOptionsProvider`.

**Slice 30: Any AdditionalText.** Demo: a harness test runs an unreadable file and asserts its diagnostic.
- 30.1 `HarnessAdditionalText : AdditionalText`; `AdditionalTexts` is `IReadOnlyList<AdditionalText>?`; the internal wrapper is removed.
- 30.2 `UnreadableAdditionalText(path)` in the testing package.

**Slice 31: Migration.** Demo: no test outside the harness's own tests calls `CSharpGeneratorDriver`.
- 31.1 `AdditionalFileTests` and `BuildPropertyTests` use the harness; `DictionaryOptionsProvider` is deleted.
- 31.2 `TextConstantsGeneratorTests` uses the harness, including the unreadable-file and equal-provider tests.
- 31.3 The 0.3 `Run`/`AssertCacheable` overloads that delegate to the `GeneratorHarnessInput` ones are deleted and their callers updated (split if over five files).

Then: release 0.5.0 and update the haitch-web docs.
