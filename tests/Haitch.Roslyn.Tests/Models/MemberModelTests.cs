using System;
using System.Linq;
using Haitch.Roslyn.Models;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Models;

public class MemberModelTests
{
    [Test]
    public async Task Should_capture_parameter_ref_kinds_default_values_and_params()
    {
        const string source = """
            public static class RefSample
            {
                public static ref readonly int GetRef(ref int a, out int b, in int c, int number = 5, params int[] rest)
                {
                    b = 0;
                    return ref a;
                }
            }
            """;

        IMethodSymbol method = GetMethod(source, "RefSample", "GetRef");

        MethodModel model = MethodModel.From(method);

        await Assert.That(model.ReturnRefKind).IsEqualTo(ReturnRefKind.RefReadOnly);
        await Assert.That(model.Parameters.Count).IsEqualTo(5);
        await Assert.That(model.Parameters[0].RefKind).IsEqualTo(RefKind.Ref);
        await Assert.That(model.Parameters[1].RefKind).IsEqualTo(RefKind.Out);
        await Assert.That(model.Parameters[2].RefKind).IsEqualTo(RefKind.In);
        await Assert
            .That(model.Parameters[3].DefaultValue)
            .IsEqualTo(ConstantValue.ForPrimitive(5));
        await Assert.That(model.Parameters[4].IsParams).IsTrue();
    }

    [Test]
    public async Task Should_capture_a_null_default_parameter_value()
    {
        const string source = """
            public static class NullDefaultSample
            {
                public static void Method(string? text = null) { }
            }
            """;

        IMethodSymbol method = GetMethod(source, "NullDefaultSample", "Method");

        MethodModel model = MethodModel.From(method);

        await Assert.That(model.Parameters[0].DefaultValue!.Kind).IsEqualTo(ConstantValueKind.Null);
    }

    [Test]
    public async Task Should_capture_property_accessors_and_init_required()
    {
        const string source = """
            public class SamplePropertyHost
            {
                public required int Required { get; init; }

                public string Name { get; set; } = "";

                protected virtual bool Flag { get; }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "SamplePropertyHost");
        IPropertySymbol required = type.GetMembers("Required").OfType<IPropertySymbol>().Single();
        IPropertySymbol name = type.GetMembers("Name").OfType<IPropertySymbol>().Single();
        IPropertySymbol flag = type.GetMembers("Flag").OfType<IPropertySymbol>().Single();

        PropertyModel requiredModel = PropertyModel.From(required);
        PropertyModel nameModel = PropertyModel.From(name);
        PropertyModel flagModel = PropertyModel.From(flag);

        await Assert.That(requiredModel.IsRequired).IsTrue();
        await Assert.That(requiredModel.Accessors.Count).IsEqualTo(2);
        await Assert.That(requiredModel.Accessors[1].Kind).IsEqualTo(PropertyAccessorKind.Init);

        await Assert.That(nameModel.Accessors[0].Kind).IsEqualTo(PropertyAccessorKind.Get);
        await Assert.That(nameModel.Accessors[1].Kind).IsEqualTo(PropertyAccessorKind.Set);

        await Assert.That(flagModel.IsVirtual).IsTrue();
        await Assert.That(flagModel.Accessibility).IsEqualTo(Accessibility.Protected);
        await Assert.That(flagModel.Accessors.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Should_capture_method_return_type_generics_and_static_async_partial()
    {
        const string source = """
            using System.Threading.Tasks;

            public static class GenericSample
            {
                public static async Task<int> ComputeAsync<T>(T value)
                    where T : class, System.IDisposable, new()
                {
                    return await Task.FromResult(1);
                }
            }
            """;

        IMethodSymbol method = GetMethod(source, "GenericSample", "ComputeAsync");

        MethodModel model = MethodModel.From(method);

        await Assert
            .That(model.ReturnType.FullyQualifiedName)
            .IsEqualTo("global::System.Threading.Tasks.Task<int>");
        await Assert.That(model.IsStatic).IsTrue();
        await Assert.That(model.IsAsync).IsTrue();
        await Assert.That(model.TypeParameters.Count).IsEqualTo(1);
        await Assert.That(model.TypeParameters[0].Name).IsEqualTo("T");
        await Assert.That(model.TypeParameters[0].HasReferenceTypeConstraint).IsTrue();
        await Assert.That(model.TypeParameters[0].HasConstructorConstraint).IsTrue();
        await Assert.That(model.TypeParameters[0].ConstraintTypes.Count).IsEqualTo(1);
        await Assert
            .That(model.TypeParameters[0].ConstraintTypes[0].FullyQualifiedName)
            .IsEqualTo("global::System.IDisposable");
    }

    [Test]
    public async Task Should_capture_an_extension_method()
    {
        const string source = """
            public static class Extensions
            {
                public static int GetLength(this string value) => value.Length;
            }
            """;

        IMethodSymbol method = GetMethod(source, "Extensions", "GetLength");

        MethodModel model = MethodModel.From(method);

        await Assert.That(model.IsExtensionMethod).IsTrue();
    }

    [Test]
    public async Task Should_capture_a_partial_method_definition()
    {
        const string source = """
            public partial class PartialHost
            {
                private partial void OnChanged();
            }

            public partial class PartialHost
            {
                private partial void OnChanged()
                {
                }
            }
            """;

        IMethodSymbol method = GetMethod(source, "PartialHost", "OnChanged");

        MethodModel model = MethodModel.From(method);

        await Assert.That(model.IsPartialDefinition).IsTrue();
    }

    [Test]
    public async Task Should_capture_field_readonly_and_const_modifiers()
    {
        const string source = """
            public class SampleFieldHost
            {
                public readonly int ReadonlyField;

                public const string ConstField = "hello";

                private static int StaticField;
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "SampleFieldHost");
        IFieldSymbol readonlyField = type.GetMembers("ReadonlyField")
            .OfType<IFieldSymbol>()
            .Single();
        IFieldSymbol constField = type.GetMembers("ConstField").OfType<IFieldSymbol>().Single();
        IFieldSymbol staticField = type.GetMembers("StaticField").OfType<IFieldSymbol>().Single();

        FieldModel readonlyModel = FieldModel.From(readonlyField);
        FieldModel constModel = FieldModel.From(constField);
        FieldModel staticModel = FieldModel.From(staticField);

        await Assert.That(readonlyModel.IsReadOnly).IsTrue();
        await Assert.That(constModel.IsConst).IsTrue();
        await Assert.That(constModel.ConstantValue).IsEqualTo(ConstantValue.ForString("hello"));
        await Assert.That(staticModel.IsStatic).IsTrue();
    }

    [Test]
    public async Task Should_be_equal_across_two_compilations_of_the_same_source()
    {
        const string source = """
            using System.Threading.Tasks;

            public class SampleHost
            {
                public required int Value { get; init; }

                public readonly string Name = "sample";

                public static async Task<int> ComputeAsync<T>(T value, int number = 5)
                    where T : class
                {
                    return await Task.FromResult(number);
                }
            }
            """;

        INamedTypeSymbol first = CompilationHelper.GetNamedTypeSymbol(source, "SampleHost");
        INamedTypeSymbol second = CompilationHelper.GetNamedTypeSymbol(source, "SampleHost");

        PropertyModel firstProperty = PropertyModel.From(
            first.GetMembers("Value").OfType<IPropertySymbol>().Single()
        );
        PropertyModel secondProperty = PropertyModel.From(
            second.GetMembers("Value").OfType<IPropertySymbol>().Single()
        );

        FieldModel firstField = FieldModel.From(
            first.GetMembers("Name").OfType<IFieldSymbol>().Single()
        );
        FieldModel secondField = FieldModel.From(
            second.GetMembers("Name").OfType<IFieldSymbol>().Single()
        );

        MethodModel firstMethod = MethodModel.From(
            first.GetMembers("ComputeAsync").OfType<IMethodSymbol>().Single()
        );
        MethodModel secondMethod = MethodModel.From(
            second.GetMembers("ComputeAsync").OfType<IMethodSymbol>().Single()
        );

        ParameterModel firstParameter = firstMethod.Parameters[0];
        ParameterModel secondParameter = secondMethod.Parameters[0];

        await Assert.That(firstProperty).IsEqualTo(secondProperty);
        await Assert.That(firstProperty.GetHashCode()).IsEqualTo(secondProperty.GetHashCode());

        await Assert.That(firstField).IsEqualTo(secondField);
        await Assert.That(firstField.GetHashCode()).IsEqualTo(secondField.GetHashCode());

        await Assert.That(firstMethod).IsEqualTo(secondMethod);
        await Assert.That(firstMethod.GetHashCode()).IsEqualTo(secondMethod.GetHashCode());

        await Assert.That(firstParameter).IsEqualTo(secondParameter);
        await Assert.That(firstParameter.GetHashCode()).IsEqualTo(secondParameter.GetHashCode());
    }

    [Test]
    public async Task Should_capture_an_enum_default_parameter_value()
    {
        const string source = """
            public enum Color
            {
                Red,
                Green,
                Blue
            }

            public static class EnumDefaultSample
            {
                public static void Method(Color color = Color.Green) { }
            }
            """;

        IMethodSymbol method = GetMethod(source, "EnumDefaultSample", "Method");

        MethodModel model = MethodModel.From(method);
        ConstantValue? defaultValue = model.Parameters[0].DefaultValue;

        await Assert.That(defaultValue!.Kind).IsEqualTo(ConstantValueKind.Enum);
        await Assert.That(defaultValue.Type!.FullyQualifiedName).IsEqualTo("global::Color");
    }

    [Test]
    public async Task Should_capture_a_nullable_enum_default_parameter_value_as_an_enum_constant()
    {
        const string source = """
            public enum Color
            {
                Red,
                Green,
                Blue
            }

            public static class NullableEnumDefaultSample
            {
                public static void Method(Color? color = Color.Green) { }
            }
            """;

        IMethodSymbol method = GetMethod(source, "NullableEnumDefaultSample", "Method");

        MethodModel model = MethodModel.From(method);
        ConstantValue? defaultValue = model.Parameters[0].DefaultValue;

        await Assert.That(defaultValue!.Kind).IsEqualTo(ConstantValueKind.Enum);
        await Assert.That(defaultValue.Type!.FullyQualifiedName).IsEqualTo("global::Color");
    }

    [Test]
    public async Task Should_capture_a_struct_default_literal_parameter_value()
    {
        const string source = """
            public struct Point
            {
                public int X;
                public int Y;
            }

            public static class StructDefaultSample
            {
                public static void Method(Point point = default) { }
            }
            """;

        IMethodSymbol method = GetMethod(source, "StructDefaultSample", "Method");

        MethodModel model = MethodModel.From(method);
        ParameterModel parameter = model.Parameters[0];

        await Assert.That(parameter.DefaultValue!.Kind).IsEqualTo(ConstantValueKind.Null);
        await Assert.That(parameter.IsDefaultLiteral).IsTrue();
    }

    [Test]
    public async Task Should_capture_a_decimal_default_parameter_value()
    {
        const string source = """
            public static class DecimalDefaultSample
            {
                public static void Method(decimal amount = 1.5m) { }
            }
            """;

        IMethodSymbol method = GetMethod(source, "DecimalDefaultSample", "Method");

        MethodModel model = MethodModel.From(method);

        await Assert
            .That(model.Parameters[0].DefaultValue)
            .IsEqualTo(ConstantValue.ForPrimitive(1.5m));
        await Assert.That(model.Parameters[0].IsDefaultLiteral).IsFalse();
    }

    [Test]
    public async Task Should_capture_a_ref_readonly_parameter()
    {
        const string source = """
            public static class RefReadonlyParamSample
            {
                public static int Method(ref readonly int value) => value;
            }
            """;

        IMethodSymbol method = GetMethod(source, "RefReadonlyParamSample", "Method");

        MethodModel model = MethodModel.From(method);

        await Assert.That(model.Parameters[0].RefKind).IsEqualTo(RefKind.RefReadOnlyParameter);
    }

    [Test]
    public async Task Should_capture_scoped_parameter_kinds()
    {
        const string source = """
            public ref struct RefStruct { }

            public static class ScopedParamSample
            {
                public static void Method(scoped ref int a, scoped RefStruct b) { }
            }
            """;

        IMethodSymbol method = GetMethod(source, "ScopedParamSample", "Method");

        MethodModel model = MethodModel.From(method);

        await Assert.That(model.Parameters[0].ScopedKind).IsEqualTo(ScopedKind.ScopedRef);
        await Assert.That(model.Parameters[1].ScopedKind).IsEqualTo(ScopedKind.ScopedValue);
    }

    [Test]
    public async Task Should_capture_a_nullable_class_constraint()
    {
        const string source = """
            public static class NullableClassConstraintSample
            {
                public static void Method<T>(T value) where T : class?
                {
                }
            }
            """;

        IMethodSymbol method = GetMethod(source, "NullableClassConstraintSample", "Method");

        MethodModel model = MethodModel.From(method);

        await Assert
            .That(model.TypeParameters[0].ReferenceTypeConstraintNullableAnnotation)
            .IsEqualTo(NullableAnnotation.Annotated);
    }

    [Test]
    public async Task Should_capture_unmanaged_and_notnull_constraints()
    {
        const string source = """
            public static class ConstraintSample
            {
                public static void UnmanagedMethod<T>(T value) where T : unmanaged
                {
                }

                public static void NotNullMethod<T>(T value) where T : notnull
                {
                }
            }
            """;

        IMethodSymbol unmanagedMethod = GetMethod(source, "ConstraintSample", "UnmanagedMethod");
        IMethodSymbol notNullMethod = GetMethod(source, "ConstraintSample", "NotNullMethod");

        MethodModel unmanagedModel = MethodModel.From(unmanagedMethod);
        MethodModel notNullModel = MethodModel.From(notNullMethod);

        await Assert.That(unmanagedModel.TypeParameters[0].HasUnmanagedTypeConstraint).IsTrue();
        await Assert.That(notNullModel.TypeParameters[0].HasNotNullConstraint).IsTrue();
    }

    [Test]
    public async Task Should_be_equal_across_two_compilations_for_type_parameter_constraint_types()
    {
        const string source = """
            public static class ConstraintEqualitySample
            {
                public static void Method<T>(T value) where T : System.IDisposable
                {
                }
            }
            """;

        IMethodSymbol first = GetMethod(source, "ConstraintEqualitySample", "Method");
        IMethodSymbol second = GetMethod(source, "ConstraintEqualitySample", "Method");

        TypeParameterModel firstTypeParameter = MethodModel.From(first).TypeParameters[0];
        TypeParameterModel secondTypeParameter = MethodModel.From(second).TypeParameters[0];

        await Assert.That(firstTypeParameter).IsEqualTo(secondTypeParameter);
        await Assert
            .That(firstTypeParameter.GetHashCode())
            .IsEqualTo(secondTypeParameter.GetHashCode());
    }

    [Test]
    public async Task Should_reject_indexers()
    {
        const string source = """
            public class IndexerHost
            {
                public int this[int index]
                {
                    get => index;
                    set { }
                }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "IndexerHost");
        IPropertySymbol indexer = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Single(p => p.IsIndexer);

        await Assert.That(() => PropertyModel.From(indexer)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_capture_an_explicit_interface_implementation()
    {
        const string source = """
            public interface IWorker
            {
                void DoWork();
            }

            public class Worker : IWorker
            {
                void IWorker.DoWork() { }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Worker");
        IMethodSymbol method = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Single(m => m.ExplicitInterfaceImplementations.Length > 0);

        MethodModel model = MethodModel.From(method);

        await Assert.That(model.ExplicitInterface!.FullyQualifiedName).IsEqualTo("global::IWorker");
        await Assert.That(model.Name).IsEqualTo("DoWork");
        await Assert.That(model.ExplicitInterfaceMemberName).IsEqualTo("DoWork");
    }

    [Test]
    public async Task Should_capture_an_explicit_interface_property_implementation_with_an_unqualified_name()
    {
        const string source = """
            public interface IFoo
            {
                int Bar { get; }
            }

            public class Foo : IFoo
            {
                int IFoo.Bar => 1;
                public int Plain { get; set; }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Foo");
        IPropertySymbol explicitProperty = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Single(p => p.ExplicitInterfaceImplementations.Length > 0);

        PropertyModel model = PropertyModel.From(explicitProperty);
        PropertyModel plain = PropertyModel.From(
            type.GetMembers("Plain").OfType<IPropertySymbol>().Single()
        );

        await Assert.That(model.Name).IsEqualTo("Bar");
        await Assert.That(model.ExplicitInterface!.FullyQualifiedName).IsEqualTo("global::IFoo");
        await Assert.That(model.ExplicitInterfaceMemberName).IsEqualTo("Bar");
        await Assert.That(plain.ExplicitInterface).IsNull();
        await Assert.That(plain.ExplicitInterfaceMemberName).IsNull();
    }

    [Test]
    public async Task Should_capture_volatile_fields()
    {
        const string source = """
            public class Host
            {
                public volatile int Flag;
                public int Plain;
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Host");

        FieldModel flag = FieldModel.From(type.GetMembers("Flag").OfType<IFieldSymbol>().Single());
        FieldModel plain = FieldModel.From(
            type.GetMembers("Plain").OfType<IFieldSymbol>().Single()
        );

        await Assert.That(flag.IsVolatile).IsTrue();
        await Assert.That(plain.IsVolatile).IsFalse();
    }

    [Test]
    public async Task Should_capture_the_ref_kind_of_ref_returning_properties()
    {
        const string source = """
            public class Host
            {
                private int _slot;
                public ref int Slot => ref _slot;
                public ref readonly int View => ref _slot;
                public int Plain { get; set; }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Host");

        PropertyModel slot = PropertyModel.From(
            type.GetMembers("Slot").OfType<IPropertySymbol>().Single()
        );
        PropertyModel view = PropertyModel.From(
            type.GetMembers("View").OfType<IPropertySymbol>().Single()
        );
        PropertyModel plain = PropertyModel.From(
            type.GetMembers("Plain").OfType<IPropertySymbol>().Single()
        );

        await Assert.That(slot.ReturnRefKind).IsEqualTo(ReturnRefKind.Ref);
        await Assert.That(view.ReturnRefKind).IsEqualTo(ReturnRefKind.RefReadOnly);
        await Assert.That(plain.ReturnRefKind).IsEqualTo(ReturnRefKind.None);
    }

    [Test]
    public async Task Should_capture_method_kind_and_readonly_modifier()
    {
        const string source = """
            public struct ReadOnlyMethodHost
            {
                public readonly int Compute() => 1;
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "ReadOnlyMethodHost");
        IMethodSymbol method = type.GetMembers("Compute").OfType<IMethodSymbol>().Single();

        MethodModel model = MethodModel.From(method);

        await Assert.That(model.IsReadOnly).IsTrue();
        await Assert.That(model.MethodKind).IsEqualTo(MethodKind.Ordinary);
    }

    [Test]
    public async Task Should_capture_whether_a_partial_method_declares_an_access_modifier()
    {
        const string source = """
            public partial class PartialHost
            {
                partial void OnX();
                public partial int M();
                void Plain() { }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            source,
            "PartialHost",
            allowErrors: true
        );

        await Assert
            .That(
                MethodModel
                    .From(type.GetMembers("OnX").OfType<IMethodSymbol>().Single())
                    .HasExplicitAccessibility
            )
            .IsFalse();
        await Assert
            .That(
                MethodModel
                    .From(type.GetMembers("M").OfType<IMethodSymbol>().Single())
                    .HasExplicitAccessibility
            )
            .IsTrue();
        await Assert
            .That(
                MethodModel
                    .From(type.GetMembers("Plain").OfType<IMethodSymbol>().Single())
                    .HasExplicitAccessibility
            )
            .IsTrue();
    }

    [Test]
    public async Task Should_keep_explicit_accessibility_true_for_interface_members()
    {
        const string source = """
            public interface IHost
            {
                void M();
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "IHost");

        MethodModel model = MethodModel.From(type.GetMembers("M").OfType<IMethodSymbol>().Single());

        await Assert.That(model.HasExplicitAccessibility).IsTrue();
        await Assert.That(model.Accessibility).IsEqualTo(Accessibility.Public);
    }

    [Test]
    public async Task Should_not_treat_models_differing_only_in_HasExplicitAccessibility_as_equal()
    {
        IMethodSymbol symbol = GetMethod(
            "public class EqHost { public void M() { } }",
            "EqHost",
            "M"
        );

        MethodModel explicitModel = MethodModel.From(symbol);
        MethodModel implicitModel = explicitModel with { HasExplicitAccessibility = false };

        await Assert.That(explicitModel).IsNotEqualTo(implicitModel);
    }

    private static PropertyModel GetProperty(
        string source,
        string typeMetadataName,
        string propertyName
    )
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            source,
            typeMetadataName,
            allowErrors: true
        );

        return PropertyModel.From(type.GetMembers(propertyName).OfType<IPropertySymbol>().Single());
    }

    [Test]
    public async Task Should_capture_partial_property_flag_and_whether_it_declares_an_access_modifier()
    {
        const string source = """
            public partial class PartialPropHost
            {
                public partial int X { get; set; }
                partial string Y { get; }
                public int Plain { get; set; }
            }
            """;

        PropertyModel x = GetProperty(source, "PartialPropHost", "X");
        PropertyModel y = GetProperty(source, "PartialPropHost", "Y");
        PropertyModel plain = GetProperty(source, "PartialPropHost", "Plain");

        await Assert.That(x.IsPartial).IsTrue();
        await Assert.That(x.HasExplicitAccessibility).IsTrue();
        await Assert.That(y.IsPartial).IsTrue();
        await Assert.That(y.HasExplicitAccessibility).IsFalse();
        await Assert.That(plain.IsPartial).IsFalse();
        await Assert.That(plain.HasExplicitAccessibility).IsTrue();
    }

    [Test]
    public async Task Should_keep_explicit_accessibility_true_for_interface_properties()
    {
        PropertyModel model = GetProperty(
            "public interface IPropHost { int P { get; } }",
            "IPropHost",
            "P"
        );

        await Assert.That(model.HasExplicitAccessibility).IsTrue();
        await Assert.That(model.Accessibility).IsEqualTo(Accessibility.Public);
    }

    [Test]
    public async Task Should_derive_property_IsReadOnly_from_the_readonly_modifier_not_the_missing_setter()
    {
        const string source = """
            public struct MutableHost
            {
                public int GetOnly { get; }
                public readonly int Marked { get { return 1; } }
                public int Mixed { readonly get { return 1; } set { } }
            }

            public readonly struct ReadOnlyHost
            {
                public int Value { get { return 1; } }
            }
            """;

        await Assert.That(GetProperty(source, "MutableHost", "GetOnly").IsReadOnly).IsFalse();
        await Assert.That(GetProperty(source, "MutableHost", "Marked").IsReadOnly).IsTrue();
        await Assert.That(GetProperty(source, "MutableHost", "Mixed").IsReadOnly).IsFalse();
        await Assert.That(GetProperty(source, "ReadOnlyHost", "Value").IsReadOnly).IsFalse();
    }

    [Test]
    public async Task Should_not_treat_property_models_differing_only_in_HasExplicitAccessibility_as_equal()
    {
        PropertyModel explicitModel = GetProperty(
            "public class PEqHost { public int P { get; } }",
            "PEqHost",
            "P"
        );
        PropertyModel implicitModel = explicitModel with { HasExplicitAccessibility = false };

        await Assert.That(explicitModel).IsNotEqualTo(implicitModel);
    }

    [Test]
    public async Task Should_not_treat_field_models_differing_only_in_IsVolatile_as_equal()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            "public class VolHost { public volatile int F; }",
            "VolHost"
        );
        FieldModel volatileModel = FieldModel.From(
            type.GetMembers("F").OfType<IFieldSymbol>().Single()
        );
        FieldModel plainModel = volatileModel with { IsVolatile = false };

        await Assert.That(volatileModel).IsNotEqualTo(plainModel);
    }

    [Test]
    public async Task Should_not_treat_property_models_differing_only_in_ReturnRefKind_as_equal()
    {
        PropertyModel plainModel = GetProperty(
            "public class RefEqHost { public int P { get; } }",
            "RefEqHost",
            "P"
        );
        PropertyModel refModel = plainModel with { ReturnRefKind = ReturnRefKind.Ref };

        await Assert.That(plainModel).IsNotEqualTo(refModel);
    }

    [Test]
    public async Task Should_not_treat_method_models_differing_only_in_ExplicitInterface_as_equal()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            "public interface IEq { void M(); } public class ExplEqHost : IEq { void IEq.M() { } }",
            "ExplEqHost"
        );
        MethodModel explicitModel = MethodModel.From(
            type.GetMembers()
                .OfType<IMethodSymbol>()
                .Single(m => m.ExplicitInterfaceImplementations.Length > 0)
        );
        MethodModel plainModel = explicitModel with { ExplicitInterface = null };

        await Assert.That(explicitModel).IsNotEqualTo(plainModel);
    }

    [Test]
    public async Task Should_not_treat_property_models_differing_only_in_ExplicitInterface_as_equal()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            "public interface IPEq { int P { get; } } public class ExplPEqHost : IPEq { int IPEq.P => 1; }",
            "ExplPEqHost"
        );
        PropertyModel explicitModel = PropertyModel.From(
            type.GetMembers()
                .OfType<IPropertySymbol>()
                .Single(p => p.ExplicitInterfaceImplementations.Length > 0)
        );
        PropertyModel plainModel = explicitModel with { ExplicitInterface = null };

        await Assert.That(explicitModel).IsNotEqualTo(plainModel);
    }

    private static IMethodSymbol GetMethod(
        string source,
        string typeMetadataName,
        string methodName
    )
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, typeMetadataName);

        return type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }
}
