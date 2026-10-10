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
    public async Task Should_unwrap_a_nullable_enum_to_the_enum()
    {
        string? underlying = UnderlyingOf("Color?");

        await Assert.That(underlying).IsEqualTo("Color");
    }

    [Test]
    public async Task Should_not_unwrap_a_non_nullable_struct()
    {
        string? underlying = UnderlyingOf("int");

        await Assert.That(underlying).IsNull();
    }

    [Test]
    public async Task Should_not_unwrap_a_nullable_annotated_reference_type()
    {
        string? underlying = UnderlyingOf("string?");

        await Assert.That(underlying).IsNull();
    }

    [Test]
    public async Task Should_not_unwrap_the_unconstructed_nullable_definition()
    {
        ITypeSymbol? underlying = TypeOf("int?").OriginalDefinition.NullableUnderlying();

        await Assert.That(underlying).IsNull();
    }

    [Test]
    public async Task Should_find_a_constructed_interface_as_itself()
    {
        bool found = IsOrImplementsShape("Ns.IShape<int>");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_find_an_interface_a_class_implements_directly()
    {
        bool found = IsOrImplementsShape("Ns.Square");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_find_an_interface_a_class_implements_through_its_base_class()
    {
        bool found = IsOrImplementsShape("Ns.Tile");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_not_find_an_interface_a_type_does_not_implement()
    {
        bool found = IsOrImplementsShape("Ns.Plain");

        await Assert.That(found).IsFalse();
    }

    private static bool IsOrImplementsShape(string type) =>
        TypeOf(type).IsOrImplements("Ns.IShape`1");

    private static string? UnderlyingOf(string type) =>
        TypeOf(type).NullableUnderlying()?.ToDisplayString();

    private static ITypeSymbol TypeOf(string type)
    {
        INamedTypeSymbol holder = CompilationHelper.GetNamedTypeSymbol(
            $$"""
            using System;

            enum Color
            {
                Red,
            }

            class Holder
            {
                public {{type}} Value = default!;
            }

            namespace Ns
            {
                interface IShape<T> { }

                class Square : IShape<int> { }

                class Tile : Square { }

                class Plain { }
            }
            """,
            "Holder"
        );

        return ((IFieldSymbol)holder.GetMembers("Value").Single()).Type;
    }
}
