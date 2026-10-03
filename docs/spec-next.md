# Spec: round 3 (0.3.0): C# 15, writer guards, NewType and model gaps, hint-name case

## What and why
1. **C# 15**: generators built on Haitch.Roslyn see and emit union types, `closed` hierarchies and labeled `break`/`continue`, and the repo proves its output compiles under C# 15. The Roslyn 4.12 floor stays: detection uses syntax and interfaces that a 4.12-compiled generator can read inside a newer compiler.
2. **Scoped-writer guards**: the caller errors documented in 0.2.0 throw `InvalidOperationException` with a readable message, always (not debug-only), instead of emitting misnested or uncompilable code.
3. **NewType gaps**: type-parameter variance, primary constructors and positional records.
4. **Model fixes**: `CancellationToken` on the `From` factories, explicit interface members, `volatile` fields and ref-returning properties.
5. **Hint names**: types whose names differ only in case can get distinct hint names.

## Assumptions
- C# 15 is preview until .NET 11 GA; we build against the RC now and accept churn. Shipped code keeps compiling at C# 14.
- Union = the type implements `System.Runtime.CompilerServices.IUnion`; case types = parameters of its implicit one-parameter constructors. Closed = a `closed` modifier token on any declaring syntax reference, falling back to reflection on the host compiler's `ITypeSymbol.IsClosed` (missing property = false). Prototyped against SDK 11.0.100-rc.1 (compiler 5.11).
- C# 15 tests live in a new net10.0 test project on Roslyn 5.9.0 with `LanguageVersion.Preview` and polyfills for `IUnion`, `UnionAttribute` and `IsClosedTypeAttribute`. The main test project stays on 4.12. CI adds a job that builds and tests on the .NET 11 SDK.
- `TypeModel.From` on an extension block (a `TypeKind` 4.12 doesn't know) throws `ArgumentException` naming the block; it doesn't silently skip.
- Writer guards use one open-block depth counter on `SourceWriter`; each scope records its depth when opened and throws when written to, chained or disposed at the wrong depth.
- Catch ordering is checked only where strings allow it: a repeated exception type (exact text) and anything after an unfiltered general catch. Full type-hierarchy ordering is out of scope (`Catch` takes strings, not symbols).
- `Attribute()` buffers its line and flushes it when the next member is accepted, so a rejected member leaves nothing written; disposing with an attribute pending throws.
- Every new model field is an `init` property, so existing positional construction keeps compiling. All types stay `internal`.

## Slices and issues
**Slice 17: C# 15.** Demo: a generator using `ForTypesWithAttribute` on a `partial union` and a `closed` record emits partial parts and a new union that compile under C# 15.
- 17.1 C# 15 test project: net10.0, Roslyn 5.9.0, Preview parse options, polyfill source, one passing smoke test; added to the solution and `Directory.Packages.props`.
- 17.2 CI job on the .NET 11 SDK that builds and tests the solution.
- 17.3 Unions in `TypeModel`/`ContainingTypeModel` (kind + case types); partial declarations write `partial union`. Fixes today's CS0261 (`partial struct` for a union).
- 17.4 `TypeModel.IsClosed`, detected from syntax with the reflection fallback.
- 17.5 `TypeModel.From` on an extension block throws a clear `ArgumentException`; the containing class still models (extension indexers filtered).
- 17.6 `NewTypeModel` emits `closed` classes/records and `union X(A, B)` declarations, rejecting invalid combinations (closed + abstract/sealed/static, closed on struct/interface, empty case list).
- 17.7 Loop and switch scopes take an optional label; `Break(label)`/`Continue(label)` write labeled jumps and reject labels that don't name an enclosing loop (or switch, for break).

**Slice 18: Writer guards.** Demo: each documented misuse throws with a message naming the scope.
- 18.1 Open-block depth on `SourceWriter`; `BodyScope` throws when written to while a child is open and when a copy is disposed twice.
- 18.2 `ElseIf`/`Else`/`Catch`/`Finally`/`Case`/`Default` throw while a nested block is open.
- 18.3 File/Namespace/Type/Property scopes throw when written to while a child is open.
- 18.4 `TryScope`: disposing a try with neither catch nor finally throws; a repeated catch type throws.
- 18.5 Switch fall-through: `BodyScope` gains `Break()`/`Return()`/`Throw()`/`GotoCase()`; a case section that doesn't end in a jump throws when the next section opens or the switch closes.
- 18.6 Buffered `Attribute()` on File/Namespace/Type scopes: a rejected member leaves no attribute; a dangling attribute throws on dispose.

**Slice 19: NewType gaps.** Demo: a generic covariant interface and a positional record generated and compiled.
- 19.1 `TypeParameterModel.Variance` (read in `From`, rendered as `in`/`out`, rejected outside interfaces).
- 19.2 Primary constructor parameters on `NewTypeModel` for classes, structs and records (positional records), rejected on interfaces, static classes and unions.

**Slice 20: Model fixes.** Demo: the sample generator models a type with an explicit interface property and a volatile field.
- 20.1 Optional `CancellationToken` on `TypeModel.From` and `MethodModel.From` (the two that loop), checked per member and passed by `ForTypesWithAttribute`.
- 20.2 Explicit interface members: `PropertyModel` gains `ExplicitInterface`; `TypeModel.From(includeMembers: true)` includes explicit method and property implementations, flagged.
- 20.3 `FieldModel.IsVolatile` and `PropertyModel.RefKind`, rendered by the writers; `AutoProperty` rejects ref-returning models.

**Slice 21: Hint names.** Demo: `Foo` and `foo` in one namespace generate two files.
- 21.1 `HintName.For` opt-in case disambiguation: a short stable hash of the case-sensitive name; default names unchanged.

Then: release 0.3.0 and update the haitch-web docs.
