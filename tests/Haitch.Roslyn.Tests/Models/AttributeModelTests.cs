using Haitch.Roslyn.Models;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Models;

public class AttributeModelTests
{
    [Test]
    public async Task Should_capture_an_attribute_with_no_arguments()
    {
        const string source = """
            using System;

            class SampleAttribute : Attribute { }

            [Sample]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target");

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();
        await Assert
            .That(model!.AttributeType.FullyQualifiedName)
            .IsEqualTo("global::SampleAttribute");
        await Assert.That(model.ConstructorArguments.Count).IsEqualTo(0);
        await Assert.That(model.NamedArguments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Should_capture_primitive_constructor_arguments()
    {
        const string source = """
            using System;

            class SampleAttribute : Attribute
            {
                public SampleAttribute(int number, bool flag, double amount) { }
            }

            [Sample(42, true, 1.5)]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target");

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();
        await Assert.That(model!.ConstructorArguments.Count).IsEqualTo(3);
        await Assert.That(model.ConstructorArguments[0]).IsEqualTo(ConstantValue.ForPrimitive(42));
        await Assert
            .That(model.ConstructorArguments[1])
            .IsEqualTo(ConstantValue.ForPrimitive(true));
        await Assert.That(model.ConstructorArguments[2]).IsEqualTo(ConstantValue.ForPrimitive(1.5));
    }

    [Test]
    public async Task Should_capture_a_string_constructor_argument()
    {
        const string source = """
            using System;

            class SampleAttribute : Attribute
            {
                public SampleAttribute(string text) { }
            }

            [Sample("hello")]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target");

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();
        await Assert
            .That(model!.ConstructorArguments[0])
            .IsEqualTo(ConstantValue.ForString("hello"));
        await Assert.That(model.ConstructorArguments[0].Kind).IsEqualTo(ConstantValueKind.String);
    }

    [Test]
    public async Task Should_capture_an_enum_constructor_argument()
    {
        const string source = """
            using System;

            enum Color { Red, Green, Blue }

            class SampleAttribute : Attribute
            {
                public SampleAttribute(Color color) { }
            }

            [Sample(Color.Green)]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target");

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();

        ConstantValue argument = model!.ConstructorArguments[0];

        await Assert.That(argument.Kind).IsEqualTo(ConstantValueKind.Enum);
        await Assert.That(argument.Type!.FullyQualifiedName).IsEqualTo("global::Color");
        await Assert.That(argument.Value).IsEqualTo(1);
    }

    [Test]
    public async Task Should_capture_a_typeof_constructor_argument()
    {
        const string source = """
            using System;

            class SampleAttribute : Attribute
            {
                public SampleAttribute(Type type) { }
            }

            [Sample(typeof(string))]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target");

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();

        ConstantValue argument = model!.ConstructorArguments[0];

        await Assert.That(argument.Kind).IsEqualTo(ConstantValueKind.Type);
        await Assert.That(argument.Type!.FullyQualifiedName).IsEqualTo("string");
    }

    [Test]
    public async Task Should_capture_an_array_constructor_argument()
    {
        const string source = """
            using System;

            class SampleAttribute : Attribute
            {
                public SampleAttribute(int[] numbers) { }
            }

            [Sample(new int[] { 1, 2, 3 })]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target");

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();

        ConstantValue argument = model!.ConstructorArguments[0];

        await Assert.That(argument.Kind).IsEqualTo(ConstantValueKind.Array);
        await Assert.That(argument.Type!.FullyQualifiedName).IsEqualTo("int[]");
        await Assert.That(argument.Elements.Count).IsEqualTo(3);
        await Assert.That(argument.Elements[0]).IsEqualTo(ConstantValue.ForPrimitive(1));
        await Assert.That(argument.Elements[1]).IsEqualTo(ConstantValue.ForPrimitive(2));
        await Assert.That(argument.Elements[2]).IsEqualTo(ConstantValue.ForPrimitive(3));
    }

    [Test]
    public async Task Should_capture_named_arguments()
    {
        const string source = """
            using System;

            class SampleAttribute : Attribute
            {
                public string? Named { get; set; }
            }

            [Sample(Named = "value")]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target");

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();
        await Assert.That(model!.NamedArguments.Count).IsEqualTo(1);
        await Assert.That(model.NamedArguments[0].Name).IsEqualTo("Named");
        await Assert
            .That(model.NamedArguments[0].Value)
            .IsEqualTo(ConstantValue.ForString("value"));
    }

    [Test]
    public async Task Should_capture_a_null_argument_with_its_declared_type()
    {
        const string source = """
            using System;

            class SampleAttribute : Attribute
            {
                public SampleAttribute(string? text) { }
            }

            [Sample(null)]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target");

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();

        ConstantValue argument = model!.ConstructorArguments[0];

        await Assert.That(argument.Kind).IsEqualTo(ConstantValueKind.Null);
        await Assert.That(argument.Type!.FullyQualifiedName).IsEqualTo("string");
    }

    [Test]
    public async Task Should_capture_an_error_constructor_argument_without_throwing()
    {
        const string source = """
            using System;

            class NotConstant
            {
                public static int Value = 5;
            }

            class SampleAttribute : Attribute
            {
                public SampleAttribute(int number) { }
            }

            [Sample(NotConstant.Value)]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target", allowErrors: true);

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();
        await Assert.That(model!.ConstructorArguments[0]).IsEqualTo(ConstantValue.Error);
        await Assert.That(model.ConstructorArguments[0].Kind).IsEqualTo(ConstantValueKind.Error);
    }

    [Test]
    public async Task Should_capture_an_error_element_inside_an_array_constructor_argument_without_throwing()
    {
        const string source = """
            using System;

            class NotConstant
            {
                public static int Value = 5;
            }

            class SampleAttribute : Attribute
            {
                public SampleAttribute(int[] numbers) { }
            }

            [Sample(new int[] { 1, NotConstant.Value, 3 })]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target", allowErrors: true);

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNotNull();

        ConstantValue argument = model!.ConstructorArguments[0];

        await Assert.That(argument.Kind).IsEqualTo(ConstantValueKind.Array);
        await Assert.That(argument.Elements[1]).IsEqualTo(ConstantValue.Error);
    }

    [Test]
    public async Task Should_return_null_when_the_attribute_type_is_unresolved()
    {
        const string source = """
            [Bogus]
            class Target { }
            """;

        AttributeData attributeData = GetAttribute(source, "Target", allowErrors: true);

        AttributeModel? model = AttributeModel.From(attributeData);

        await Assert.That(model).IsNull();
    }

    [Test]
    public async Task Should_be_equal_for_captures_from_two_separate_compilations_of_the_same_source()
    {
        const string source = """
            using System;

            enum Color { Red, Green, Blue }

            class SampleAttribute : Attribute
            {
                public SampleAttribute(int number, string text, Color color, Type type, int[] numbers, string? missing) { }

                public bool Named { get; set; }
            }

            [Sample(1, "a", Color.Green, typeof(string), new int[] { 1, 2 }, null, Named = true)]
            class Target { }
            """;

        AttributeData first = GetAttribute(source, "Target");
        AttributeData second = GetAttribute(source, "Target");

        AttributeModel? firstModel = AttributeModel.From(first);
        AttributeModel? secondModel = AttributeModel.From(second);

        await Assert.That(firstModel).IsEqualTo(secondModel);
        await Assert.That(firstModel!.GetHashCode()).IsEqualTo(secondModel!.GetHashCode());
    }

    [Test]
    public async Task Should_not_be_equal_when_constructor_argument_order_differs()
    {
        const string firstSource = """
            using System;

            class SampleAttribute : Attribute
            {
                public SampleAttribute(int first, int second) { }
            }

            [Sample(1, 2)]
            class Target { }
            """;

        const string secondSource = """
            using System;

            class SampleAttribute : Attribute
            {
                public SampleAttribute(int first, int second) { }
            }

            [Sample(2, 1)]
            class Target { }
            """;

        AttributeData first = GetAttribute(firstSource, "Target");
        AttributeData second = GetAttribute(secondSource, "Target");

        AttributeModel? firstModel = AttributeModel.From(first);
        AttributeModel? secondModel = AttributeModel.From(second);

        await Assert.That<AttributeModel?>(firstModel).IsNotEqualTo(secondModel);
    }

    [Test]
    public async Task Should_not_be_equal_when_only_the_metadata_name_differs()
    {
        const string source = """
            using System;

            class SampleAttribute : Attribute { }

            [Sample]
            class Target { }
            """;

        AttributeModel model = AttributeModel.From(GetAttribute(source, "Target"))!;
        AttributeModel other = model with { MetadataName = "Other.SampleAttribute" };

        await Assert.That(model).IsNotEqualTo(other);
    }

    private static AttributeData GetAttribute(
        string source,
        string metadataName,
        bool allowErrors = false
    )
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            source,
            metadataName,
            allowErrors
        );

        return type.GetAttributes().Single();
    }
}
