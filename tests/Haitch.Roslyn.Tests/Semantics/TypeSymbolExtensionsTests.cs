using Haitch.Roslyn.Semantics;
using Haitch.Roslyn.Tests.Models;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Semantics;

public class TypeSymbolExtensionsTests
{
    [Test]
    public async Task Should_unwrap_a_nullable_written_as_nullable_of_t()
    {
        string? underlying = UnderlyingOf("Nullable<int>");

        await Assert.That(underlying).IsEqualTo("int");
    }

    [Test]
    public async Task Should_unwrap_a_nullable_written_with_a_question_mark()
    {
        string? underlying = UnderlyingOf("int?");

        await Assert.That(underlying).IsEqualTo("int");
    }

    [Test]
    public async Task Should_not_unwrap_a_non_nullable_struct()
    {
        string? underlying = UnderlyingOf("int");

        await Assert.That(underlying).IsNull();
    }

    [Test]
    public async Task Should_not_unwrap_the_unconstructed_nullable_definition()
    {
        ITypeSymbol? underlying = TypeOf("int?").OriginalDefinition.NullableUnderlying();

        await Assert.That(underlying).IsNull();
    }

    private static string? UnderlyingOf(string type) =>
        TypeOf(type).NullableUnderlying()?.ToDisplayString();

    private static ITypeSymbol TypeOf(string type)
    {
        INamedTypeSymbol holder = CompilationHelper.GetNamedTypeSymbol(
            $$"""
            using System;

            class Holder
            {
                public {{type}} Value = default!;
            }
            """,
            "Holder"
        );

        return ((IFieldSymbol)holder.GetMembers("Value").Single()).Type;
    }
}
