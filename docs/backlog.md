# Backlog (audit of 0.6.0, 2026-10-04)

Read-only audits of four areas: symbol models, code emission, pipeline and testing harness, and repo/CI/docs. Items are audit claims, not yet verified by a failing test. Priority is the value to a generic generator author: H (high), M (medium) or L (low).

## A. Member and type kinds missing end to end (model, writer and discovery)
1. **H** Constructors (incl. static, chained `: this/base`, C# 14 partial), operators, conversion operators and destructors.
   - Not modelled: `TypeModel` keeps only ordinary methods.
   - Not writable. The writer ignores `MethodKind`, so a `.ctor` model renders as `void .ctor(...)`. That is a bug.
   - Not discoverable.
2. **H** Enums (members, underlying type) and delegates (signature).
   - `TypeModel.From` throws on them.
   - No `KindKeyword` and no enum-member API exists for writing them.
   - `ForTypesWithAttribute` only accepts `TypeDeclarationSyntax`, so they can't be discovered.
3. **H** Events:
   - No `ForEventsWithAttribute`.
   - The writer can't emit accessor or partial event implementations; non-field-like events are rejected.
   - Explicit interface events and properties are rejected by the writer.
   - `EventModel` has no `HasExplicitAccessibility`.
4. **M** Indexers: `PropertyModel.From` throws, and there are no parameters to model them, write them or discover them.
5. **M** Primary constructor and positional record parameters: nothing models them or their order, and the scoped `NewType` can't emit them (`WriteNewTypeDeclaration` can).
6. **M** Nested types appear only as names (`MemberNames`).
7. **M** C# 14 extension members: extension blocks throw, so receivers and block members can't be modelled.
8. **M** Partial methods: `MethodModel` has no `IsPartial`, so a modelled implementation part loses `partial` when written.

## B. Model gaps
9. **H** `TypeRef` is a display string plus flags. It has no type arguments, array element type or rank, tuple names, pointer element type, or per-argument nullability.
10. **M** Attributes:
    - No constructor parameter names.
    - No attribute location for diagnostics.
    - No return-value attributes.
    - `[field:]` attributes on auto-properties are lost.
    - Type parameters have no attributes.
11. **M** `TypeParameterModel` has no `AllowsRefLikeType` and no ordinal.
12. **M** `PropertyModel` has no auto-property or `field`-keyword flag and no accessor attributes.
13. **M** `FieldModel` has no `RefKind` (ref fields) and no fixed-size buffer information.
14. **L** `IsParams` doesn't tell params arrays from params collections.
15. **L** `ContainingTypeModel` has no `IsStatic`, `IsAbstract`, `IsSealed` or `IsFileLocal`.
16. **L** `ConstantValue` has no accessors for char, float, decimal, byte or the unsigned types.
17. **L** No model carries doc comments, and events and parameters have no location.
18. **L** No helper builds a `TypeRef` from a name; the Notify sample builds them by hand.

## C. Writer gaps
19. **M** No abstract or interface property signatures (no property counterpart to `WriteMethodSignature`).
20. **M** No expression-bodied members (`=> expr;`).
21. **M** `FileScope` can't write file-scoped namespaces (only the unscoped `WriteNamespace` can).
22. **M** No directives (`#pragma`, `#if`, `#nullable disable`); the header hard-codes `#nullable enable`.
23. **M** No doc comment (`///`) API.
24. **M** No `[GeneratedCode]`/`[ExcludeFromCodeCoverage]` helpers, and `AttributeModel` has no target specifier (`assembly:`, `return:`, `field:`).
25. **M** No `do`/`while`.
26. **M** No `await using`, `await foreach`, `lock`, `checked`, `unsafe`, `fixed`, `yield`, `goto`/labels, local functions, lambdas or switch expressions.
27. **L** No modifiers for `new` (hiding), `unsafe` or `extern` on properties.
28. **L** No `global using`, `using static` or alias API.
29. **L** `HintName.For` only takes a `TypeModel` (no `NewTypeModel` or plain-name overload).
30. **L** Unguarded:
    - instance members in a new static class (CS0708);
    - `Using` after a namespace has closed (CS1529).
31. **L** Known limitations:
    - `ToString()` may throw after a scope throws.
    - Labels are not tracked across lambda or local-function boundaries, and jumps out of `finally` are unchecked.

## D. Pipeline gaps
32. **H** No discovery by base type or interface (inherited attributes are invisible to `ForAttributeWithMetadataName`).
33. **H** No `[assembly:]`/`[module:]` attribute discovery. Parameter and type-parameter discovery are M.
34. **H** Diagnostic locations: `LocationInfo.ToLocation` creates external-file locations, so `#pragma warning disable` and `[SuppressMessage]` may not apply. Untested; verify first.
35. **H** No compilation-level helpers: language version, nullable context, "does type X exist", referenced-assembly checks.
36. **M** `DiagnosticInfo` has no additional locations, no properties bag and no severity override.
37. **M** No per-file options for C# sources (`build_metadata.Compile.*`, `.editorconfig`).
38. **M** No combinators: group-by, distinct, deterministic sort after `Collect`, or an equatable `Combine`. `Result.Combine` stops at three inputs.
39. **L** Build properties are strings only (no typed parsing, no invalid-value diagnostic).
40. **L** `IsFirstMarkedDeclaration` scans every syntax tree for each item.
41. **L** The comment at `PartialTypeValidation.cs:11` is wrong: `DiagnosticDescriptor` does have value equality.

## E. Testing harness gaps
42. **H** Warnings in generated code don't fail a run; only errors do.
43. **H** Compilation options are fixed: nullable always on, DLL output, no unsafe, no assembly name.
44. **H** References are the test host's assemblies only (no netstandard2.0 or net48 sets, and no way to drop a reference).
45. **M** One generator per run: no multiple generators, no analyzer plus generator, no suppressors.
46. **M** Tree paths are always `Source{i}.cs`.
47. **M** Missing assertions:
    - the exact set of hint names;
    - all diagnostics at once;
    - span end;
    - compiler diagnostics.
48. **M** Cacheability supports only trivia and unrelated edits: no custom edit script, no additional-file or option change, and no per-step assertion API.
49. **M** No emit-and-load helper to run the generated code.
50. **L** The disabled-output kind is hard-coded to `None` (the IDE disables implementation outputs).
51. **L** No performance or allocation checks.
52. **L** `Haitch.Roslyn.Testing` targets only net10.0.

## F. Repo, CI, packaging, docs
53. **M** `publish.yml` publishes any `v*.*.*` tag:
    - no CI-passed check;
    - no approval environment;
    - no csharpier check;
    - no .NET 11 job.
54. **M** The main suite runs only on Roslyn 4.12, so the 4.14+ `AddEmbeddedAttributeDefinition` shadowing is untested.
55. **M** The package smoke test doesn't set `EnforceExtendedAnalyzerRules` or turn on the Roslyn analyzers (RS1035 and similar are untested), and nothing smoke-tests the packed Testing package.
56. **M** `docs/issues/README.md` stops at 21.1.
57. **L** Rename `docs/spec-next.md` (it is the round 3 spec) to `spec-round3.md`.
58. **L** The `README.md` quick start uses `writer.WriteLine` inside a type scope; `scope.Line()` is the current API.
59. **L** The `IsSealed=false` quirk for sealed interface members isn't in the web docs.
60. **L** Actions are pinned by tag, not SHA. The .NET 11 job uses `dotnet-quality: preview` and needs switching at GA.
61. **L** The package copyright uses `DateTime.Now.Year`.
62. **L** The sample generators have no `[Generator]` attribute and aren't packaged as an analyzer.
63. **L** These are untested:
    - `MethodModel.IsExtern`;
    - static abstract interface members;
    - generic-attribute `MetadataName`;
    - `LocationInfo` contents;
    - `TypeModel.IsUnion` and `IsExtensionBlock`;
    - `ConstantValue.ForType` and `ForArray`.
