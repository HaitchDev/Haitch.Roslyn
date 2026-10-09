using System.Linq;
using Haitch.Roslyn.Models;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Models;

public class TypeRefTests
{
    [Test]
    public async Task Should_capture_a_primitive_type()
    {
        const string source = "class Sample { public int Field; }";

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("int");
        await Assert.That(typeRef.SpecialType).IsEqualTo(SpecialType.System_Int32);
        await Assert.That(typeRef.IsValueType).IsTrue();
        await Assert.That(typeRef.NullableAnnotation).IsEqualTo(NullableAnnotation.NotAnnotated);
    }

    [Test]
    public async Task Should_capture_a_generic_type()
    {
        const string source = """
            using System.Collections.Generic;

            class Sample { public List<int> Field; }
            """;

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert
            .That(typeRef.FullyQualifiedName)
            .IsEqualTo("global::System.Collections.Generic.List<int>");
        await Assert.That(typeRef.IsValueType).IsFalse();
    }

    [Test]
    public async Task Should_capture_an_array_type()
    {
        const string source = "class Sample { public int[] Field; }";

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("int[]");
        await Assert.That(typeRef.IsValueType).IsFalse();
    }

    [Test]
    public async Task Should_capture_a_nested_type()
    {
        const string source = """
            namespace Example;

            class Outer { public class Inner { } public Outer.Inner Field; }
            """;

        ITypeSymbol type = GetFieldType(source, "Example.Outer", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("global::Example.Outer.Inner");
    }

    [Test]
    public async Task Should_capture_a_nullable_reference_type()
    {
        const string source = "class Sample { public string? Field; }";

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("string?");
        await Assert.That(typeRef.NullableAnnotation).IsEqualTo(NullableAnnotation.Annotated);
        await Assert.That(typeRef.IsValueType).IsFalse();
    }

    [Test]
    public async Task Should_capture_a_nullable_value_type()
    {
        const string source = "class Sample { public int? Field; }";

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("int?");
        await Assert.That(typeRef.IsValueType).IsTrue();
    }

    [Test]
    public async Task Should_capture_a_type_parameter()
    {
        const string source = "class Sample<T> { public T Field; }";

        ITypeSymbol type = GetFieldType(source, "Sample`1", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("T");
        await Assert.That(typeRef.SpecialType).IsEqualTo(SpecialType.None);
    }

    [Test]
    public async Task Should_be_equal_for_captures_from_two_separate_compilations_of_the_same_source()
    {
        const string source = "class Sample { public int Field; }";

        ITypeSymbol first = GetFieldType(source, "Sample", "Field");
        ITypeSymbol second = GetFieldType(source, "Sample", "Field");

        TypeRef firstRef = TypeRef.From(first);
        TypeRef secondRef = TypeRef.From(second);

        await Assert.That(firstRef).IsEqualTo(secondRef);
        await Assert.That(firstRef.GetHashCode()).IsEqualTo(secondRef.GetHashCode());
    }

    [Test]
    public async Task Should_capture_nested_nullability_in_a_generic_type_argument()
    {
        const string source = """
            using System.Collections.Generic;

            class Sample { public List<string?> Field; }
            """;

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert
            .That(typeRef.FullyQualifiedName)
            .IsEqualTo("global::System.Collections.Generic.List<string?>");
    }

    [Test]
    public async Task Should_capture_nested_nullability_in_an_array_element()
    {
        const string source = "class Sample { public string?[] Field; }";

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("string?[]");
    }

    [Test]
    public async Task Should_capture_nested_nullability_in_a_tuple_element()
    {
        const string source = "class Sample { public (string? A, int B) Field; }";

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("(string? A, int B)");
    }

    [Test]
    public async Task Should_capture_nested_nullability_across_multiple_levels_of_generics()
    {
        const string source = """
            using System.Collections.Generic;

            class Sample { public Dictionary<string, List<int?>> Field; }
            """;

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert
            .That(typeRef.FullyQualifiedName)
            .IsEqualTo(
                "global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<int?>>"
            );
    }

    [Test]
    public async Task Should_capture_a_dynamic_type()
    {
        const string source = "class Sample { public dynamic Field; }";

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("dynamic");
    }

    [Test]
    public async Task Should_capture_a_pointer_type()
    {
        const string source = "unsafe class Sample { public int* Field; }";

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.FullyQualifiedName).IsEqualTo("int*");
    }

    [Test]
    public async Task Should_capture_the_type_kind()
    {
        const string source =
            "class Sample<T> { public Sample<T> ClassField; public T TypeParameterField; }";

        ITypeSymbol classType = GetFieldType(source, "Sample`1", "ClassField");
        ITypeSymbol typeParameterType = GetFieldType(source, "Sample`1", "TypeParameterField");

        TypeRef classRef = TypeRef.From(classType);
        TypeRef typeParameterRef = TypeRef.From(typeParameterType);

        await Assert.That(classRef.TypeKind).IsEqualTo(TypeKind.Class);
        await Assert.That(typeParameterRef.TypeKind).IsEqualTo(TypeKind.TypeParameter);
    }

    [Test]
    public async Task Should_capture_the_type_arguments_of_a_generic_type()
    {
        const string source = """
            using System.Collections.Generic;

            class Sample { public Dictionary<string, int> Field; }
            """;

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.TypeArguments.Count).IsEqualTo(2);
        await Assert.That(typeRef.TypeArguments[0].FullyQualifiedName).IsEqualTo("string");
        await Assert.That(typeRef.TypeArguments[1].FullyQualifiedName).IsEqualTo("int");
    }

    [Test]
    public async Task Should_capture_nested_type_arguments_recursively()
    {
        const string source = """
            using System.Collections.Generic;

            class Sample { public Dictionary<string, List<int>> Field; }
            """;

        ITypeSymbol type = GetFieldType(source, "Sample", "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.TypeArguments.Count).IsEqualTo(2);
        await Assert.That(typeRef.TypeArguments[1].TypeArguments.Count).IsEqualTo(1);
        await Assert
            .That(typeRef.TypeArguments[1].TypeArguments[0].FullyQualifiedName)
            .IsEqualTo("int");
    }

    [Test]
    [Arguments("class Sample { public int Field; }")]
    [Arguments("class Sample { public int[] Field; }")]
    [Arguments("unsafe class Sample { public int* Field; }")]
    [Arguments("class Sample<T> { public T Field; }")]
    public async Task Should_have_no_type_arguments_for_non_generic_array_pointer_and_type_parameter_types(
        string source
    )
    {
        string metadataName = source.Contains("Sample<T>") ? "Sample`1" : "Sample";

        ITypeSymbol type = GetFieldType(source, metadataName, "Field");

        TypeRef typeRef = TypeRef.From(type);

        await Assert.That(typeRef.TypeArguments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Should_not_be_equal_when_only_the_type_arguments_differ()
    {
        TypeRef first = new(
            "global::Example.Box",
            NullableAnnotation.NotAnnotated,
            SpecialType.None,
            TypeKind.Class,
            false
        )
        {
            TypeArguments =
            [
                new(
                    "int",
                    NullableAnnotation.NotAnnotated,
                    SpecialType.System_Int32,
                    TypeKind.Struct,
                    true
                ),
            ],
        };
        TypeRef second = first with
        {
            TypeArguments =
            [
                new(
                    "string",
                    NullableAnnotation.NotAnnotated,
                    SpecialType.System_String,
                    TypeKind.Class,
                    false
                ),
            ],
        };

        await Assert.That(first).IsNotEqualTo(second);
    }

    [Test]
    public async Task Should_have_equal_hash_codes_when_type_arguments_are_equal()
    {
        const string source = """
            using System.Collections.Generic;

            class Sample { public List<int> Field; }
            """;

        TypeRef first = TypeRef.From(GetFieldType(source, "Sample", "Field"));
        TypeRef second = TypeRef.From(GetFieldType(source, "Sample", "Field"));

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    private static ITypeSymbol GetFieldType(string source, string metadataName, string fieldName)
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, metadataName);
        IFieldSymbol field = type.GetMembers(fieldName).OfType<IFieldSymbol>().Single();

        return field.Type;
    }
}
