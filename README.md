# Haitch.Roslyn

Source-only utilities for Roslyn incremental source generators. The package ships `.cs` files that compile into your project as `internal` types, so nothing is added to your generator's dependencies at runtime.

## Requirements

- C# 14 or later (`<LangVersion>14</LangVersion>` or `latest`), because the Polyfill dependency uses C# 14 extension blocks.
- Your own reference to `Microsoft.CodeAnalysis.CSharp` 4.12.0 or later. Generator projects already have one; the package deliberately does not pass Roslyn on as a dependency.
- Polyfill arrives as a transitive dependency, so there is nothing to add for it.

```xml
<PropertyGroup>
  <TargetFramework>netstandard2.0</TargetFramework>
  <LangVersion>14</LangVersion>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" PrivateAssets="all" />
  <PackageReference Include="Haitch.Roslyn" Version="*" PrivateAssets="all" />
</ItemGroup>
```

## What's inside

| Namespace | Types | Purpose |
|---|---|---|
| `Haitch.Roslyn.Types` | `EquatableArray<T>` | Value-equal array for pipeline models, so incremental caching works. |
| `Haitch.Roslyn.Diagnostics` | `LocationInfo`, `DiagnosticInfo`, `Result<T>`, `Result.Combine`/`Collect`, `ReportDiagnostics` | Cache-safe diagnostics and a railway-style result that is split into reported diagnostics and successful values. |
| `Haitch.Roslyn.Models` | `TypeRef`, `AttributeModel`, `ConstantValue`, `TypeModel`, `ContainingTypeModel`, member models, `SyntaxInfo` | Value-equal snapshots of symbols and syntax, holding no Roslyn objects. |
| `Haitch.Roslyn.Writing` | `SourceWriter`, `SourceWriterExtensions` | Indented source writing with block scopes, plus typed helpers that write headers, namespaces, nested partial declarations and signatures from the models. |
| `Haitch.Roslyn.Generators` | `ForTypesWithAttribute`, `AddEmbeddedAttributeDefinition`, `AddMarkerAttribute` | `ForAttributeWithMetadataName` returning models, and marker attributes marked `[Embedded]`. |

## Testing generators

`Haitch.Roslyn.Testing` is a regular (non-source-only) package for your test project. It targets net10.0 and is test-framework agnostic: every failure is a `GeneratorTestException`.

```shell
dotnet add package Haitch.Roslyn.Testing
```

`GeneratorHarness.Run` compiles in-memory sources (nullable enabled, the test host's platform references), runs the generator with step tracking, and throws `GeneratorTestException` if the generator throws or the input or generated output has compile errors. Errors are checked after generation, so inputs may use post-initialization types such as marker attributes. It returns a `GeneratorHarnessResult` with `Sources` (generated text by hint name), `Diagnostics` (the generator's own), `Compilation`, `InputCompilation`, `Driver` and `RunResult`.

```csharp
var result = GeneratorHarness.Run(new MyGenerator(), ["[My] public partial class Foo;"]);

// TUnit assertion; any framework works
await Assert.That(result.Sources).ContainsKey("Foo.g.cs");
```

`GeneratorHarness.AssertCacheable` runs the generator, reruns it on a cloned compilation, then on one where the first source gained a trailing comment, and requires every output of the named steps to be `Cached` or `Unchanged`. It returns the first run's `GeneratorHarnessResult`. Name the steps with `WithTrackingName` in the generator:

```csharp
// in the generator
var models = context.SyntaxProvider
    .ForAttributeWithMetadataName("My.MyAttribute", (_, _) => true, (ctx, _) => Model.From(ctx))
    .WithTrackingName("Models");

// in the test
GeneratorHarness.AssertCacheable(new MyGenerator(), ["[My] public partial class Foo;"], "Models");
```

`CachingHazardWalker.Find` is what the harness uses on each tracked output. It returns the member path of the first value that defeats caching (symbols, syntax, locations, diagnostics, compilations, arrays, `ImmutableArray<T>`, classes without an `Equals` override), or `null`. Call it directly to check a model.

Caveats:

- Name model steps only. Steps that combine with `CompilationProvider` or output syntax nodes are legitimately `Modified` by the trivia edit.
- The edit targets the first source, so put the code your tracked steps read there.
- An unknown or never-run step name fails; the message lists the steps that exist.
- Pass a `CacheabilityOptions` for `UnrelatedEditSourceIndex` (a third run appends `namespace HarnessUnrelatedEdit { }` to that source; a step that depends on the whole compilation fails) and `RequireRecomputationAfterTriviaEdit` (after the trivia edit at least one output of each named step must be `Unchanged`, proving the step re-ran for the edited source and produced an equal value; name the per-item model step, since an aggregate such as `Collect` over unchanged items reports `Cached` and fails). Passing `null` positionally as the fourth argument is ambiguous between the overloads; use named arguments.
- The walker can report false positives: BCL types with internal state that override `Equals` are walked field by field, and value-equal collection wrappers other than `EquatableArray<T>` are reported if they hold a hazard or lack an `Equals` override. Models nested deeper than 256 levels are reported.

## Notes

- `TypeModel.From` captures members only when `includeMembers: true`, because members make the model change on every member edit. Indexers are skipped.
- `ForTypesWithAttribute` yields one item per type, even when several partial declarations carry the attribute; the item carries every application of the attribute across all parts.
- `default(Result<T>)` is a failure with no diagnostics.
