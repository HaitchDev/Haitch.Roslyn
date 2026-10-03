using System.Linq;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Types;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Writing;

public class NewTypeDeclarationTests
{
    private const string Support = "public class Base { }\npublic interface IMarker { }\n";

    private static readonly TypeRef BaseClass = new(
        "global::Base",
        NullableAnnotation.NotAnnotated,
        SpecialType.None,
        TypeKind.Class,
        false
    );
    private static readonly TypeRef Marker = new(
        "global::IMarker",
        NullableAnnotation.NotAnnotated,
        SpecialType.None,
        TypeKind.Interface,
        false
    );

    [Test]
    public async Task Should_write_a_public_sealed_class_with_a_base_class_and_an_interface()
    {
        NewTypeModel model = new("Widget", TypeDeclarationKind.Class, Accessibility.Public)
        {
            IsSealed = true,
            BaseTypes = new[] { BaseClass, Marker }.ToEquatableArray(),
        };

        await AssertWrites(
            model,
            """
            public sealed class Widget : global::Base, global::IMarker
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_an_internal_static_class()
    {
        NewTypeModel model = new("Helpers", TypeDeclarationKind.Class, Accessibility.Internal)
        {
            IsStatic = true,
        };

        await AssertWrites(
            model,
            """
            internal static class Helpers
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_a_public_abstract_class()
    {
        NewTypeModel model = new("Shape", TypeDeclarationKind.Class, Accessibility.Public)
        {
            IsAbstract = true,
        };

        await AssertWrites(
            model,
            """
            public abstract class Shape
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_a_public_readonly_struct()
    {
        NewTypeModel model = new("Point", TypeDeclarationKind.Struct, Accessibility.Public)
        {
            IsReadOnly = true,
        };

        await AssertWrites(
            model,
            """
            public readonly struct Point
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_a_public_readonly_ref_struct()
    {
        NewTypeModel model = new("Span2", TypeDeclarationKind.Struct, Accessibility.Public)
        {
            IsReadOnly = true,
            IsRefLikeType = true,
        };

        await AssertWrites(
            model,
            """
            public readonly ref struct Span2
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_a_public_readonly_record_struct()
    {
        NewTypeModel model = new("Pair", TypeDeclarationKind.RecordStruct, Accessibility.Public)
        {
            IsReadOnly = true,
        };

        await AssertWrites(
            model,
            """
            public readonly record struct Pair
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_a_public_sealed_record()
    {
        NewTypeModel model = new("Person", TypeDeclarationKind.RecordClass, Accessibility.Public)
        {
            IsSealed = true,
        };

        await AssertWrites(
            model,
            """
            public sealed record Person
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_an_internal_interface_with_a_generic_constraint()
    {
        TypeParameterModel typeParameter = new(
            "T",
            new[] { Marker }.ToEquatableArray(),
            HasReferenceTypeConstraint: true,
            NullableAnnotation.NotAnnotated,
            HasValueTypeConstraint: false,
            HasUnmanagedTypeConstraint: false,
            HasNotNullConstraint: false,
            HasConstructorConstraint: true
        );
        NewTypeModel model = new("IBox", TypeDeclarationKind.Interface, Accessibility.Internal)
        {
            TypeParameters = new[] { typeParameter }.ToEquatableArray(),
        };

        await AssertWrites(
            model,
            """
            internal interface IBox<T>
                where T : class, global::IMarker, new()
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_variance_on_an_interface_type_parameters()
    {
        NewTypeModel model = new("IMap", TypeDeclarationKind.Interface, Accessibility.Public)
        {
            TypeParameters = new[]
            {
                Parameter("TIn", VarianceKind.In),
                Parameter("TOut", VarianceKind.Out),
                Parameter("TNone", VarianceKind.None),
            }.ToEquatableArray(),
        };

        await AssertWrites(
            model,
            """
            public interface IMap<in TIn, out TOut, TNone>
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_variance_on_a_partial_variant_interface()
    {
        NewTypeModel model = new("IMap", TypeDeclarationKind.Interface, Accessibility.Public)
        {
            IsPartial = true,
            TypeParameters = new[] { Parameter("T", VarianceKind.Out) }.ToEquatableArray(),
        };

        await AssertWrites(
            model,
            """
            public partial interface IMap<out T>
            {
            }

            """
        );
    }

    private static TypeParameterModel Parameter(string name, VarianceKind variance)
    {
        return new TypeParameterModel(
            name,
            default,
            false,
            NullableAnnotation.None,
            false,
            false,
            false,
            false
        )
        {
            Variance = variance,
        };
    }

    private static readonly TypeRef StringRef = new(
        "string",
        NullableAnnotation.NotAnnotated,
        SpecialType.System_String,
        TypeKind.Class,
        false
    );
    private static readonly TypeRef IntRef = new(
        "int",
        NullableAnnotation.NotAnnotated,
        SpecialType.System_Int32,
        TypeKind.Struct,
        true
    );
    private static readonly TypeRef StringArrayRef = new(
        "string[]",
        NullableAnnotation.NotAnnotated,
        SpecialType.None,
        TypeKind.Array,
        false
    );

    private static ParameterModel Positional(
        string name,
        TypeRef type,
        ConstantValue? defaultValue = null,
        bool isParams = false,
        RefKind refKind = RefKind.None
    )
    {
        return new ParameterModel(
            name,
            type,
            refKind,
            ScopedKind.None,
            isParams,
            defaultValue,
            false,
            default
        );
    }

    [Test]
    public async Task Should_end_a_positional_record_without_a_body_with_a_semicolon()
    {
        NewTypeModel model = new("Person", TypeDeclarationKind.RecordClass, Accessibility.Public)
        {
            PrimaryConstructorParameters = new[]
            {
                Positional("Name", StringRef),
                Positional("Age", IntRef),
            }.ToEquatableArray(),
        };

        await AssertWritesBodyless(model, "public record Person(string Name, int Age);\n");
    }

    [Test]
    public async Task Should_open_a_body_for_a_positional_record_written_with_the_block_method()
    {
        NewTypeModel model = new("Person", TypeDeclarationKind.RecordClass, Accessibility.Public)
        {
            PrimaryConstructorParameters = new[]
            {
                Positional("Name", StringRef),
                Positional("Age", IntRef),
            }.ToEquatableArray(),
        };

        await AssertWrites(
            model,
            """
            public record Person(string Name, int Age)
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_reject_a_bodyless_declaration_for_a_non_record_and_leave_the_writer_empty()
    {
        NewTypeModel model = new("Widget", TypeDeclarationKind.Class, Accessibility.Public)
        {
            PrimaryConstructorParameters = new[] { Positional("id", IntRef) }.ToEquatableArray(),
        };
        SourceWriter writer = new();

        await Assert
            .That(() => writer.WriteBodylessNewTypeDeclaration(model))
            .Throws<ArgumentException>();
        await Assert.That(writer.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Should_reject_a_bodyless_declaration_without_a_primary_constructor_list()
    {
        NewTypeModel model = new("Person", TypeDeclarationKind.RecordClass, Accessibility.Public);
        SourceWriter writer = new();

        await Assert
            .That(() => writer.WriteBodylessNewTypeDeclaration(model))
            .Throws<ArgumentException>();
        await Assert.That(writer.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Should_reject_an_illegal_bodyless_declaration_and_leave_the_writer_empty()
    {
        NewTypeModel model = new("Person", TypeDeclarationKind.RecordClass, Accessibility.Public)
        {
            IsStatic = true,
            PrimaryConstructorParameters = new EquatableArray<ParameterModel>(),
        };
        SourceWriter writer = new();

        await Assert
            .That(() => writer.WriteBodylessNewTypeDeclaration(model))
            .Throws<ArgumentException>();
        await Assert.That(writer.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Should_write_an_empty_primary_constructor_list_for_an_empty_array()
    {
        NewTypeModel model = new("Unit", TypeDeclarationKind.RecordStruct, Accessibility.Public)
        {
            PrimaryConstructorParameters = new EquatableArray<ParameterModel>(),
        };

        await AssertWritesBodyless(model, "public record struct Unit();\n");
    }

    [Test]
    public async Task Should_keep_the_body_for_a_non_record_with_primary_constructor_parameters()
    {
        NewTypeModel model = new("Widget", TypeDeclarationKind.Class, Accessibility.Public)
        {
            BaseTypes = new[] { Marker }.ToEquatableArray(),
            PrimaryConstructorParameters = new[] { Positional("id", IntRef) }.ToEquatableArray(),
        };

        await AssertWrites(
            model,
            """
            public class Widget(int id) : global::IMarker
            {
            }

            """,
            compileBody: "public int Copy = id; "
        );
    }

    [Test]
    public async Task Should_write_parameter_defaults_params_and_ref_kinds()
    {
        NewTypeModel model = new("Buffer", TypeDeclarationKind.Struct, Accessibility.Public)
        {
            PrimaryConstructorParameters = new[]
            {
                Positional("count", IntRef, ConstantValue.ForPrimitive(5)),
                Positional("names", StringArrayRef, isParams: true),
            }.ToEquatableArray(),
        };

        await AssertWrites(
            model,
            """
            public struct Buffer(int count = 5, params string[] names)
            {
            }

            """,
            compileBody: "public int Total = count + names.Length; "
        );
    }

    [Test]
    public async Task Should_write_a_generic_positional_record_with_constraints_before_the_semicolon()
    {
        TypeParameterModel typeParameter = new(
            "T",
            default,
            HasReferenceTypeConstraint: true,
            NullableAnnotation.NotAnnotated,
            HasValueTypeConstraint: false,
            HasUnmanagedTypeConstraint: false,
            HasNotNullConstraint: false,
            HasConstructorConstraint: false
        );
        TypeRef valueRef = new(
            "T",
            NullableAnnotation.NotAnnotated,
            SpecialType.None,
            TypeKind.TypeParameter,
            false
        );
        NewTypeModel model = new("Box", TypeDeclarationKind.RecordClass, Accessibility.Public)
        {
            TypeParameters = new[] { typeParameter }.ToEquatableArray(),
            BaseTypes = new[] { Marker }.ToEquatableArray(),
            PrimaryConstructorParameters = new[]
            {
                Positional("Value", valueRef),
            }.ToEquatableArray(),
        };

        await AssertWritesBodyless(
            model,
            """
            public record Box<T>(T Value) : global::IMarker
                where T : class;

            """
        );
    }

    [Test]
    public async Task Should_write_a_file_sealed_class()
    {
        NewTypeModel model = new("Hidden", TypeDeclarationKind.Class, Accessibility.NotApplicable)
        {
            IsFileLocal = true,
            IsSealed = true,
        };

        await AssertWrites(
            model,
            """
            file sealed class Hidden
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_no_accessibility_for_not_applicable_without_the_file_flag()
    {
        NewTypeModel model = new("Plain", TypeDeclarationKind.Class, Accessibility.NotApplicable);

        await AssertWrites(
            model,
            """
            class Plain
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_a_public_partial_class_when_opted_in()
    {
        NewTypeModel model = new("Split", TypeDeclarationKind.Class, Accessibility.Public)
        {
            IsPartial = true,
        };

        await AssertWrites(
            model,
            """
            public partial class Split
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_modifiers_in_the_documented_order()
    {
        NewTypeModel model = new("Ordered", TypeDeclarationKind.Struct, Accessibility.Public)
        {
            IsReadOnly = true,
            IsRefLikeType = true,
            IsPartial = true,
        };

        await AssertWrites(
            model,
            """
            public readonly ref partial struct Ordered
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_not_be_equal_when_only_base_types_differ()
    {
        NewTypeModel a = Model(TypeDeclarationKind.Class) with
        {
            BaseTypes = new[] { BaseClass }.ToEquatableArray(),
        };
        NewTypeModel b = Model(TypeDeclarationKind.Class) with
        {
            BaseTypes = new[] { Marker }.ToEquatableArray(),
        };

        await Assert.That(a).IsNotEqualTo(b);
    }

    [Test]
    public async Task Should_escape_a_keyword_name()
    {
        NewTypeModel model = new("class", TypeDeclarationKind.Class, Accessibility.Public);

        await AssertWrites(
            model,
            """
            public class @class
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_accept_an_already_escaped_name()
    {
        NewTypeModel model = new("@class", TypeDeclarationKind.Class, Accessibility.Public);

        await AssertWrites(
            model,
            """
            public class @class
            {
            }

            """
        );
    }

    [Test]
    public async Task Should_write_interface_base_types_on_structs_interfaces_and_records()
    {
        foreach (
            TypeDeclarationKind kind in new[]
            {
                TypeDeclarationKind.Struct,
                TypeDeclarationKind.Interface,
                TypeDeclarationKind.RecordClass,
                TypeDeclarationKind.RecordStruct,
            }
        )
        {
            NewTypeModel model = new("Impl", kind, Accessibility.Public)
            {
                BaseTypes = new[] { Marker }.ToEquatableArray(),
            };
            SourceWriter writer = new();

            using (writer.WriteNewTypeDeclaration(model)) { }

            await Assert.That(writer.ToString()).Contains(" Impl : global::IMarker");
            await AssertCompiles(writer.ToString());
        }
    }

    [Test]
    public async Task Should_be_value_equal()
    {
        NewTypeModel Create()
        {
            return new NewTypeModel("A", TypeDeclarationKind.Class, Accessibility.Public)
            {
                BaseTypes = new[] { BaseClass }.ToEquatableArray(),
            };
        }

        await Assert.That(Create()).IsEqualTo(Create());

        NewTypeModel RecordWith(EquatableArray<ParameterModel> parameters)
        {
            return new NewTypeModel("R", TypeDeclarationKind.RecordClass, Accessibility.Public)
            {
                PrimaryConstructorParameters = parameters,
            };
        }

        NewTypeModel WithVariance(VarianceKind variance)
        {
            return new NewTypeModel("I", TypeDeclarationKind.Interface, Accessibility.Public)
            {
                TypeParameters = new[] { Parameter("T", variance) }.ToEquatableArray(),
            };
        }

        EquatableArray<ParameterModel> One() =>
            new[] { Positional("x", IntRef) }.ToEquatableArray();

        await Assert.That(RecordWith(One())).IsEqualTo(RecordWith(One()));
        await Assert
            .That(RecordWith(One()))
            .IsNotEqualTo(RecordWith(new[] { Positional("y", IntRef) }.ToEquatableArray()));
        await Assert
            .That(RecordWith(new EquatableArray<ParameterModel>()))
            .IsEqualTo(RecordWith(new EquatableArray<ParameterModel>()));
        await Assert
            .That(RecordWith(new EquatableArray<ParameterModel>()))
            .IsNotEqualTo(RecordWith(One()));
        await Assert
            .That(Model(TypeDeclarationKind.RecordClass) with { Name = "R" })
            .IsNotEqualTo(RecordWith(new EquatableArray<ParameterModel>()));

        await Assert.That(WithVariance(VarianceKind.Out)).IsEqualTo(WithVariance(VarianceKind.Out));
        await Assert
            .That(WithVariance(VarianceKind.Out))
            .IsNotEqualTo(WithVariance(VarianceKind.In));
        await Assert
            .That(WithVariance(VarianceKind.None))
            .IsNotEqualTo(WithVariance(VarianceKind.Out));
    }

    [Test]
    [MethodDataSource(nameof(IllegalModels))]
    public async Task Should_reject_an_illegal_combination_and_leave_the_writer_empty(int caseIndex)
    {
        SourceWriter writer = new();
        NewTypeModel model = IllegalCases()[caseIndex]();

        await Assert.That(() => writer.WriteNewTypeDeclaration(model)).Throws<ArgumentException>();
        await Assert.That(writer.ToString()).IsEqualTo(string.Empty);
    }

    public static IEnumerable<int> IllegalModels()
    {
        return Enumerable.Range(0, IllegalCases().Count);
    }

    private static List<Func<NewTypeModel>> IllegalCases()
    {
        return IllegalCaseIterator().ToList();
    }

    private static IEnumerable<Func<NewTypeModel>> IllegalCaseIterator()
    {
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                IsStatic = true,
                IsAbstract = true,
            };
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                IsStatic = true,
                IsSealed = true,
            };
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                IsStatic = true,
                BaseTypes = new[] { Marker }.ToEquatableArray(),
            };
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                IsAbstract = true,
                IsSealed = true,
            };

        foreach (
            TypeDeclarationKind kind in new[]
            {
                TypeDeclarationKind.Struct,
                TypeDeclarationKind.RecordStruct,
                TypeDeclarationKind.Interface,
            }
        )
        {
            TypeDeclarationKind captured = kind;

            yield return () => Model(captured) with { IsStatic = true };
            yield return () => Model(captured) with { IsAbstract = true };
            yield return () => Model(captured) with { IsSealed = true };
        }

        foreach (
            TypeDeclarationKind kind in new[]
            {
                TypeDeclarationKind.Class,
                TypeDeclarationKind.RecordClass,
                TypeDeclarationKind.Interface,
            }
        )
        {
            TypeDeclarationKind captured = kind;

            yield return () => Model(captured) with { IsReadOnly = true };
            yield return () => Model(captured) with { IsRefLikeType = true };
        }

        yield return () => Model(TypeDeclarationKind.RecordStruct) with { IsRefLikeType = true };

        foreach (
            TypeDeclarationKind kind in new[]
            {
                TypeDeclarationKind.Class,
                TypeDeclarationKind.Struct,
                TypeDeclarationKind.RecordClass,
                TypeDeclarationKind.RecordStruct,
            }
        )
        {
            TypeDeclarationKind captured = kind;

            yield return () =>
                Model(captured) with
                {
                    TypeParameters = new[] { Parameter("T", VarianceKind.Out) }.ToEquatableArray(),
                };
        }

        yield return () => Model(TypeDeclarationKind.Class) with { Name = " " };

        yield return () =>
            Model(TypeDeclarationKind.Interface) with
            {
                PrimaryConstructorParameters = new EquatableArray<ParameterModel>(),
            };
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                IsStatic = true,
                PrimaryConstructorParameters = new EquatableArray<ParameterModel>(),
            };
        yield return () =>
            Model(TypeDeclarationKind.Union) with
            {
                UnionCaseTypes = new[] { "int" }.ToEquatableArray(),
                PrimaryConstructorParameters = new[] { Positional("x", IntRef) }.ToEquatableArray(),
            };
        yield return () => Model(TypeDeclarationKind.Class) with { Name = "" };
        yield return () => Model(TypeDeclarationKind.RecordClass) with { IsStatic = true };
        yield return () => Model(TypeDeclarationKind.Class) with { Name = "1abc" };
        yield return () => Model(TypeDeclarationKind.Class) with { Name = "a b" };
        yield return () => Model(TypeDeclarationKind.Class) with { IsFileLocal = true };
        yield return () =>
            Model(TypeDeclarationKind.Struct) with
            {
                BaseTypes = new[] { BaseClass }.ToEquatableArray(),
            };
        yield return () =>
            Model(TypeDeclarationKind.RecordStruct) with
            {
                BaseTypes = new[] { BaseClass }.ToEquatableArray(),
            };
        yield return () =>
            Model(TypeDeclarationKind.Interface) with
            {
                BaseTypes = new[] { BaseClass }.ToEquatableArray(),
            };
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                BaseTypes = new[] { BaseClass, BaseClass }.ToEquatableArray(),
            };
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                BaseTypes = new[] { Marker, BaseClass }.ToEquatableArray(),
            };
    }

    private static NewTypeModel Model(TypeDeclarationKind kind)
    {
        return new NewTypeModel("T0", kind, Accessibility.Public);
    }

    private static async Task AssertWritesBodyless(NewTypeModel model, string expected)
    {
        SourceWriter writer = new();

        writer.WriteBodylessNewTypeDeclaration(model);

        await Assert.That(writer.ToString()).IsEqualTo(expected);
        await AssertCompiles(writer.ToString());
    }

    private static async Task AssertWrites(
        NewTypeModel model,
        string expected,
        string? compileBody = null
    )
    {
        SourceWriter writer = new();

        using (writer.WriteNewTypeDeclaration(model)) { }

        string output = writer.ToString();

        await Assert.That(output).IsEqualTo(expected);

        // A primary constructor parameter that nothing reads is a warning, so the compile check gives it a reader.
        await AssertCompiles(
            compileBody is null ? output : output.Replace("{\n}", "{ " + compileBody + "}")
        );
    }

    private static async Task AssertCompiles(string output)
    {
        CSharpCompilation compilation = CompilationHelper.Compile(
            $"#nullable enable\n{Support}{output}",
            allowErrors: true
        );
        string[] problems = compilation
            .GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning
            )
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        await Assert.That(problems).IsEmpty();
    }
}
