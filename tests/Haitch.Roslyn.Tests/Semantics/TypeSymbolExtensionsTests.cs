using Haitch.Roslyn.Semantics;
using Haitch.Roslyn.Tests.Models;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Semantics;

public class TypeSymbolExtensionsTests
{
    [Test]
    public async Task Should_unwrap_a_nullable_written_as_nullable_of_t()
    {
        ITypeSymbol? underlying = TypeOf("Nullable<int>").NullableUnderlying();

        await Assert.That(underlying?.SpecialType).IsEqualTo(SpecialType.System_Int32);
    }

    [Test]
    public async Task Should_not_unwrap_a_non_nullable_struct()
    {
        ITypeSymbol? underlying = TypeOf("int").NullableUnderlying();

        await Assert.That(underlying).IsNull();
    }

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
