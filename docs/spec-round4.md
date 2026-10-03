# Spec: round 4 (0.4.0): serve generator authors end to end

## What and why
1. **Check and format**: one command (`.claude/check.sh`) proves the repo is healthy; csharpier is enforced locally and in CI.
2. **Harness**: generators can be tested with additional files, build options and broken input, using diagnostic and expected-output assertions instead of hand-rolled string checks.
3. **Member discovery**: generators find attributed methods, properties and fields, not only types, and can filter them further.
4. **Typed attribute arguments**: generators read `[Foo(Name = "x", Count = 3)]` without casting `object? Value`.
5. **Options and additional files**: generators read build properties and additional files as cacheable models.

Each library slice is demonstrated by a small sample generator in `samples/`, tested with the new harness assertions.

## Assumptions
- Version 0.4.0. Roslyn 4.12 floor stays; all `src/Haitch.Roslyn` types stay `internal`; new model fields are `init` properties.
- csharpier is a local dotnet tool (`.config/dotnet-tools.json`); the repo is reformatted once in a single mechanical commit, exempt from the five-file Touches rule.
- `samples/` holds a generator project `Haitch.Roslyn.Samples` (netstandard2.0, links `src/Haitch.Roslyn/**/*.cs` like the test project does) and a TUnit project `Haitch.Roslyn.Samples.Tests`. Both are in the solution, not packable, and covered by the check.
- New harness input goes through a `GeneratorHarnessInput` options object (`init` properties: sources, references, parse options, additional texts, global and per-file options, `AllowInputErrors`). New `Run`/`AssertCacheable` overloads take it; the existing overloads delegate to them, so 0.3 callers still compile.
- Assertions throw `GeneratorTestException`, keeping the testing package framework-agnostic.
- Expected-output files need no new dependency: paths resolve relative to the calling test file (`[CallerFilePath]`); a missing file, or `HAITCH_ACCEPT=1`, writes the actual output and fails. Line endings are normalized.
- With `AllowInputErrors`, the harness throws only for errors located in generated trees; input errors are exposed on the result.
- Field discovery targets `VariableDeclaratorSyntax` (what `ForAttributeWithMetadataName` reports for fields). Member items carry the member model, the containing `TypeModel` without members, `SyntaxInfo` and the attributes.
- Build properties are read as `build_property.<Name>`, additional-file metadata as `build_metadata.AdditionalFiles.<Name>`; docs note the consumer's `CompilerVisibleProperty`/`CompilerVisibleItemMetadata`.
- Each issue is test-first with a behavioural red against a stub. One implementer at a time.

## Slices and issues
**Slice 22: Foundation.** Demo: `.claude/check.sh` passes locally and CI runs the format check.
- 22.1 csharpier tool manifest and a whole-repo reformat (mechanical, nothing else).
- 22.2 `.claude/check.sh` (restore, build, `csharpier check`, test) and a format step in the CI `build` job.
- 22.3 `samples/` scaffold: both projects, a trivial sample generator and one passing harness test, in the solution.

**Slice 23: Harness.** Demo: a test feeds an additional file and a build property, runs against code with a syntax error, and asserts one diagnostic and an expected-output file.
- 23.1 `GeneratorHarnessInput` with `Run`/`AssertCacheable` overloads; old overloads delegate.
- 23.2 Additional texts, global and per-file analyzer-config options reach the driver.
- 23.3 `AllowInputErrors`: input errors recorded on the result; only generated-tree errors throw.
- 23.4 Diagnostic assertions: `AssertNoDiagnostics()`, `AssertDiagnostic(id, …)` with optional severity, file/line/column and message substring.
- 23.5 Expected-output assertions: `AssertSource(hintName, expected)` inline and `AssertSourceFile(hintName, path)` with accept mode and a first-differing-line message; plus the slice demo test.

**Slice 24: Member discovery.** Demo: a `[Notify]` sample turns attributed fields into change-notifying properties.
- 24.1 `ForMethodsWithAttribute`.
- 24.2 `ForPropertiesWithAttribute` and `ForFieldsWithAttribute`.
- 24.3 Optional `Func<SyntaxNode, CancellationToken, bool>` predicate on all four `For…WithAttribute` methods, applied after the built-in kind check.
- 24.4 The `[Notify]` sample and its tests.

**Slice 27: Generator gaps (runs before slice 25).** Demo: `NotifyGenerator` drops its workarounds with byte-identical output and is at least 150 lines shorter.
- 27.1 `TypeModel.BaseType`, `Interfaces` and `AllInterfaces`.
- 27.2 `EventModel` and `TypeModel.Events`.
- 27.3 `TypeScope.Event` for field-like events.
- 27.4 An optional base list on the innermost partial declaration written by `Type(TypeModel)`.
- 27.5 `PartialTypeValidation.ValidateContainingTypes` for members.
- 27.6 The `[Notify]` sample uses 27.1–27.5.

**Slice 25: Typed attribute arguments.** Demo: `[Notify(Name = "Title", Raise = false)]` changes the output.
- 25.1 `ConstantValue` typed accessors (`TryGetString`, `TryGetBoolean`, `TryGetInt32`/`Int64`/`Double`, `TryGetEnum<TEnum>`, `TryGetType`, `TryGetArray`, `TryGetStringArray`); false on mismatch, never throw.
- 25.2 `AttributeModel.TryGetNamedArgument`/`TryGetConstructorArgument` and a `Find(fullyQualifiedMetadataName)` extension on `EquatableArray<AttributeModel>`.
- 25.3 The `[Notify]` sample reads its arguments through the accessors.

**Slice 26: Options and additional files.** Demo: a sample turns `*.txt` additional files into string constants in the `RootNamespace` build property's namespace.
- 26.1 Build-property providers: `ForBuildProperty(name)` and `ForBuildProperties(names)` (equatable model).
- 26.2 `ForAdditionalFiles(pathPredicate, …)`: equatable `AdditionalFileModel` (path, content, requested metadata); unreadable text is a `Result` failure.
- 26.3 The text-constants sample and its tests, including `AssertCacheable`.

Then: release 0.4.0 and update the haitch-web docs.
