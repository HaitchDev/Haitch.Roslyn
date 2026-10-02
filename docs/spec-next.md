# Spec: testing, hint names, validation, publishing

## What and why
1. **Haitch.Roslyn.Testing**: a normal (not source-only) package that runs a generator in a test, returns its output and diagnostics, and proves incremental caching works: every tracked step is Cached/Unchanged on a rerun, and no step output holds a caching hazard (symbols, syntax, `Compilation`, `ImmutableArray`, plain arrays, reference-equality classes). Test-framework agnostic: it throws exceptions with readable messages.
2. **Hint names and dedupe**: unique, valid hint names built from a `TypeModel`, and `ForTypesWithAttribute` yields one item per type rather than one per marked partial declaration.
3. **Partial-type validation**: one call turns (TypeModel, SyntaxInfo) into a `Result` using diagnostic descriptors the caller supplies (not partial, containing type not partial, file-local).
4. **Publishing**: match Haitch.Unions: central package management, shared package metadata, icon, README and license packed, SourceLink/symbols for the Testing package, a tag-triggered publish workflow using NuGet trusted publishing, plus a CI workflow for pushes and PRs.

## Assumptions
- The Testing package targets `net10.0`, references `Microsoft.CodeAnalysis.CSharp` 4.8.0 as a real dependency, and does not reference Haitch.Roslyn (it inspects any generator).
- The Testing package lives in `src/Haitch.Roslyn.Testing` and is tested from the existing test project.
- Dedupe changes `ForTypesWithAttribute`'s default behaviour; nothing is released, so there's nothing to break.
- Metadata copies Unions' values, with `RepositoryUrl` https://github.com/HaitchDev/Haitch.Roslyn. `icon.png` and `LICENSE.md` are copied from Unions.
- The source-only package has no symbols package (it ships no binaries); the Testing package gets snupkg and SourceLink.
- Versions come from the git tag (`-p:Version=`), with a `0.0.0-dev` fallback.

## Slices and issues
**Slice 6: Testing package.** Demo: this repo's own caching tests use it.
- 6.1 Project scaffold and `GeneratorHarness`: compile sources, run a generator with step tracking, expose generated sources by hint name and diagnostics, and fail if the output doesn't compile.
- 6.2 Cacheability check: rerun on a cloned compilation plus a trivia edit; assert named tracked steps are Cached/Unchanged and name the offending step.
- 6.3 Model hazard walker: walk each tracked step's outputs reflectively and report the path to any hazardous field.
- 6.4 Dogfood: move this repo's three hand-rolled caching tests onto the harness.

**Slice 7: Hint names and dedupe.** Demo: a two-part partial type generates one file.
- 7.1 `HintName.For(TypeModel, suffix)`: namespace, containing types and generic arity, validated against `AddSource`'s rules.
- 7.2 `ForTypesWithAttribute` yields one item per type (keeping the first marked declaration's `SyntaxInfo`, with attributes from all parts), without `Collect()`.

**Slice 8: Validation.** Demo: an end-to-end sample generator.
- 8.1 `PartialTypeDiagnostics` (caller's descriptors) and `Validate` that returns `Result<…>` with locations.
- 8.2 A sample generator in tests: `ForTypesWithAttribute` → `Validate` → `ReportDiagnostics` → `HintName` + `SourceWriterExtensions`, covered by the Testing harness including cacheability.

**Slice 9: Publishing.** Demo: `dotnet pack` produces two correct packages; the workflows lint clean.
- 9.1 Central package management (`Directory.Packages.props`).
- 9.2 `src/Directory.Build.props` metadata, icon, README and license, SourceLink and symbols for Testing; the Testing package gets its own README section.
- 9.3 `.github/workflows/publish.yml` (tag `v*.*.*`, SDK from `global.json`, build, test, pack both, OIDC push).
- 9.4 `.github/workflows/ci.yml` (push to main and PRs: build, test, pack).
