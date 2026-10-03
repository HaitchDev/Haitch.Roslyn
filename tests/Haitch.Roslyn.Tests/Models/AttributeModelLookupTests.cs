using Haitch.Roslyn.Models;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Models;

public class AttributeModelLookupTests
{
    private const string ArgumentSource = """
        using System;

        class SampleAttribute : Attribute
        {
            public SampleAttribute(string first, params int[] rest) { }
            public string? Name { get; set; }
        }

        [Sample("a", 1, 2, Name = "n")]
        class Target { }
        """;

    private const string NameSource = """
        using System;

        class GlobalMarkAttribute : Attribute { }

        namespace Ns
        {
            class PlainAttribute : Attribute { }
            class Foo<T> : Attribute { }
            class Outer<T>
            {
                public class Inner<U> : Attribute { }
            }
            class Holder
            {
                public class NestedAttribute : Attribute { }
            }
        }

        [GlobalMark]
        [Ns.Plain]
        [Ns.Foo<int>]
        [Ns.Holder.Nested]
        [Ns.Outer<int>.Inner<string>]
        class Target { }
        """;

    private static readonly EquatableArray<AttributeModel> Attributes = GetAttributes(NameSource);

    [Test]
    public async Task Should_find_a_named_argument()
    {
        AttributeModel model = GetAttributes(ArgumentSource)[0];

        await Assert.That(model.TryGetNamedArgument("Name", out ConstantValue value)).IsTrue();
        await Assert.That(value.TryGetString(out string text)).IsTrue();
        await Assert.That(text).IsEqualTo("n");
    }

    [Test]
    public async Task Should_miss_a_named_argument_that_is_absent()
    {
        AttributeModel model = GetAttributes(ArgumentSource)[0];

        await Assert.That(model.TryGetNamedArgument("Other", out _)).IsFalse();
    }

    [Test]
    public async Task Should_match_named_argument_names_ordinally()
    {
        AttributeModel model = GetAttributes(ArgumentSource)[0];

        await Assert.That(model.TryGetNamedArgument("name", out _)).IsFalse();
        await Assert.That(model.TryGetNamedArgument("NAME", out _)).IsFalse();
    }

    [Test]
    public async Task Should_read_a_constructor_argument_by_index()
    {
        AttributeModel model = GetAttributes(ArgumentSource)[0];

        await Assert.That(model.TryGetConstructorArgument(0, out ConstantValue value)).IsTrue();
        await Assert.That(value.TryGetString(out string text)).IsTrue();
        await Assert.That(text).IsEqualTo("a");
    }

    [Test]
    public async Task Should_read_a_params_argument_as_one_array()
    {
        AttributeModel model = GetAttributes(ArgumentSource)[0];

        await Assert.That(model.TryGetConstructorArgument(1, out ConstantValue value)).IsTrue();
        await Assert.That(value.TryGetArray(out EquatableArray<ConstantValue> items)).IsTrue();
        await Assert.That(items.Count).IsEqualTo(2);
        await Assert.That(model.TryGetConstructorArgument(2, out _)).IsFalse();
    }

    [Test]
    public async Task Should_miss_a_constructor_index_out_of_range_without_throwing()
    {
        AttributeModel model = GetAttributes(ArgumentSource)[0];

        await Assert.That(model.TryGetConstructorArgument(5, out _)).IsFalse();
        await Assert.That(model.TryGetConstructorArgument(-1, out _)).IsFalse();
    }

    [Test]
    public async Task Should_name_a_plain_attribute()
    {
        await Assert.That(Attributes[1].MetadataName).IsEqualTo("Ns.PlainAttribute");
    }

    [Test]
    public async Task Should_name_a_nested_attribute_with_a_plus()
    {
        await Assert.That(Attributes[3].MetadataName).IsEqualTo("Ns.Holder+NestedAttribute");
    }

    [Test]
    public async Task Should_name_a_generic_attribute_with_its_arity()
    {
        await Assert.That(Attributes[2].MetadataName).IsEqualTo("Ns.Foo`1");
    }

    [Test]
    public async Task Should_name_a_generic_attribute_nested_in_a_generic()
    {
        await Assert.That(Attributes[4].MetadataName).IsEqualTo("Ns.Outer`1+Inner`1");
    }

    [Test]
    public async Task Should_name_a_global_namespace_attribute_without_a_prefix()
    {
        await Assert.That(Attributes[0].MetadataName).IsEqualTo("GlobalMarkAttribute");
    }

    [Test]
    public async Task Should_find_a_plain_attribute()
    {
        AttributeModel? found = Attributes.Find("Ns.PlainAttribute");

        await Assert.That(found).IsNotNull();
        await Assert.That(found).IsEqualTo(Attributes[1]);
    }

    [Test]
    public async Task Should_find_a_nested_attribute()
    {
        await Assert.That(Attributes.Find("Ns.Holder+NestedAttribute")).IsEqualTo(Attributes[3]);
    }

    [Test]
    public async Task Should_find_a_generic_attribute()
    {
        await Assert.That(Attributes.Find("Ns.Foo`1")).IsEqualTo(Attributes[2]);
    }

    [Test]
    public async Task Should_return_null_on_a_miss()
    {
        await Assert.That(Attributes.Find("Ns.Missing")).IsNull();
        await Assert.That(Attributes.Find("Ns.Plain")).IsNull();
        await Assert.That(Attributes.Find("ns.plainattribute")).IsNull();
    }

    [Test]
    public async Task Should_not_match_a_hand_built_model_without_a_metadata_name()
    {
        TypeRef type = Attributes[1].AttributeType;
        var handBuilt = new AttributeModel(type, default, default);
        EquatableArray<AttributeModel> array = new[] { handBuilt }.ToEquatableArray();

        await Assert.That(handBuilt.MetadataName).IsNull();
        await Assert.That(array.Find("Ns.PlainAttribute")).IsNull();
    }

    private static EquatableArray<AttributeModel> GetAttributes(string source)
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Target");

        return type.GetAttributes()
            .Select(AttributeModel.From)
            .Select(model => model!)
            .ToEquatableArray();
    }
}
