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
    public async Task Should_find_an_interface_a_class_implements_through_another_interface()
    {
        bool found = IsOrImplementsShape("Ns.Cube");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_find_a_read_only_collection_an_array_implements()
    {
        bool found = TypeOf("int[]")
            .IsOrImplements("System.Collections.Generic.IReadOnlyCollection`1");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_not_find_an_interface_a_type_does_not_implement()
    {
        bool found = IsOrImplementsShape("Ns.Plain");

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task Should_find_a_constructed_interface_a_type_implements_with_the_given_argument()
    {
        bool found = IsOrImplementsEquatableTo("Ns.Own", "Ns.Own");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_not_find_a_constructed_interface_a_type_implements_only_with_another_argument()
    {
        bool found = IsOrImplementsEquatableTo("Ns.Foreign", "Ns.Foreign");

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task Should_find_a_constructed_interface_as_itself_with_the_given_argument()
    {
        bool found = IsOrImplementsEquatableTo("IEquatable<Ns.Other>", "Ns.Other");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_find_a_constructed_interface_a_type_implements_with_the_given_and_another_argument()
    {
        bool found = IsOrImplementsEquatableTo("Ns.Twice", "Ns.Twice");

        await Assert.That(found).IsTrue();
    }

    private static bool IsOrImplementsEquatableTo(string type, string argument)
    {
        INamedTypeSymbol holder = Holder(type, argument);

        return FieldType(holder, "Value")
            .IsOrImplements("System.IEquatable`1", FieldType(holder, "Argument"));
    }

    [Test]
    public async Task Should_find_a_direct_base()
    {
        string? found = BaseOf("Ns.Tile", "Ns.Square");

        await Assert.That(found).IsEqualTo("Ns.Square");
    }

    [Test]
    public async Task Should_find_a_base_two_levels_up()
    {
        string? found = BaseOf("Ns.Mosaic", "Ns.Square");

        await Assert.That(found).IsEqualTo("Ns.Square");
    }

    [Test]
    public async Task Should_find_a_generic_base_constructed_as_declared()
    {
        string? found = BaseOf("Ns.OrderValidator", "Ns.Validator`1");

        await Assert.That(found).IsEqualTo("Ns.Validator<int>");
    }

    [Test]
    public async Task Should_not_find_a_type_as_its_own_base()
    {
        string? found = BaseOf("Ns.Square", "Ns.Square");

        await Assert.That(found).IsNull();
    }

    [Test]
    public async Task Should_not_find_a_base_a_type_does_not_derive_from()
    {
        string? found = BaseOf("Ns.Plain", "Ns.Square");

        await Assert.That(found).IsNull();
    }

    private static string? BaseOf(string type, string metadataName) =>
        TypeOf(type).FindBase(metadataName)?.ToDisplayString();

    [Test]
    public async Task Should_list_an_enums_members_in_declaration_order()
    {
        string members = EnumMembersOf("Ns.Size");

        await Assert.That(members).IsEqualTo("Small, Large, Medium");
    }

    [Test]
    public async Task Should_list_both_names_of_an_enum_alias_in_declaration_order()
    {
        string members = EnumMembersOf("Ns.Level");

        await Assert.That(members).IsEqualTo("Low, Minimum, High");
    }

    [Test]
    public async Task Should_list_no_members_of_an_empty_enum()
    {
        string members = EnumMembersOf("Ns.Empty");

        await Assert.That(members).IsEmpty();
    }

    [Test]
    public async Task Should_list_no_enum_members_of_a_class()
    {
        string members = EnumMembersOf("Ns.Limits");

        await Assert.That(members).IsEmpty();
    }

    private static string EnumMembersOf(string type) =>
        string.Join(", ", NamedTypeOf(type).EnumMembers().Select(member => member.Name));

    private static INamedTypeSymbol NamedTypeOf(string type) => (INamedTypeSymbol)TypeOf(type);

    private static bool IsOrImplementsShape(string type) =>
        TypeOf(type).IsOrImplements("Ns.IShape`1");

    private static string? UnderlyingOf(string type) =>
        TypeOf(type).NullableUnderlying()?.ToDisplayString();

    private static ITypeSymbol TypeOf(string type) => FieldType(Holder(type, "int"), "Value");

    private static ITypeSymbol FieldType(INamedTypeSymbol holder, string field) =>
        ((IFieldSymbol)holder.GetMembers(field).Single()).Type;

    private static INamedTypeSymbol Holder(string type, string argument)
    {
        return CompilationHelper.GetNamedTypeSymbol(
            $$"""
            using System;

            enum Color
            {
                Red,
            }

            class Holder
            {
                public {{type}} Value = default!;

                public {{argument}} Argument = default!;
            }

            namespace Ns
            {
                struct Other { }

                struct Own : IEquatable<Own>
                {
                    public bool Equals(Own other) => true;
                }

                struct Foreign : IEquatable<Other>
                {
                    public bool Equals(Other other) => true;
                }

                struct Twice : IEquatable<Twice>, IEquatable<Other>
                {
                    public bool Equals(Twice other) => true;

                    public bool Equals(Other other) => true;
                }

                interface IShape<T> { }

                class Square : IShape<int> { }

                class Tile : Square { }

                class Mosaic : Tile { }

                class Validator<T> { }

                class OrderValidator : Validator<int> { }

                interface ISolid<T> : IShape<T> { }

                class Cube : ISolid<int> { }

                class Plain { }

                enum Empty { }

                enum Level
                {
                    Low = 0,
                    Minimum = Low,
                    High = 1,
                }

                class Limits
                {
                    public const int Max = 10;
                }

                enum Size
                {
                    Small = 1,
                    Large = 3,
                    Medium = 2,
                }
            }
            """,
            "Holder"
        );
    }
}
