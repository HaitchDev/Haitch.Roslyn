using Haitch.Roslyn.Models;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Models;

public class ConstantValueAccessorTests
{
    public enum IntColor
    {
        Red = 1,
        Green = 2,
    }

    public enum ByteSize : byte
    {
        Small = 3,
        Large = 9,
    }

    private const int StringIndex = 0;
    private const int BooleanIndex = 1;
    private const int Int32Index = 2;
    private const int Int64Index = 3;
    private const int DoubleIndex = 4;
    private const int IntEnumIndex = 5;
    private const int ByteEnumIndex = 6;
    private const int TypeIndex = 7;
    private const int IntArrayIndex = 8;
    private const int StringArrayIndex = 9;
    private const int MixedArrayIndex = 10;
    private const int NullIndex = 11;

    private const string Source = """
        using System;

        enum Color { Red = 1, Green = 2 }
        enum Size : byte { Small = 3, Large = 9 }

        class SampleAttribute : Attribute
        {
            public SampleAttribute(
                string s, bool b, int i, long l, double d, Color c, Size size, Type t,
                int[] ints, string?[] strings, object[] mixed, string? nothing) { }
        }

        [Sample("text", true, 42, 5L, 1.5, Color.Green, Size.Large, typeof(string),
            new[] { 1, 2 }, new string?[] { "a", null }, new object[] { "a", 1 }, null)]
        class Target { }
        """;

    private static readonly ConstantValue[] Values = GetValues(Source, "Target");

    [Test]
    public async Task Should_read_a_string_and_miss_on_other_kinds()
    {
        await Assert.That(Values[StringIndex].TryGetString(out string value)).IsTrue();
        await Assert.That(value).IsEqualTo("text");
        await Assert.That(Values[Int32Index].TryGetString(out _)).IsFalse();
    }

    [Test]
    public async Task Should_read_a_boolean_and_miss_on_other_kinds()
    {
        await Assert.That(Values[BooleanIndex].TryGetBoolean(out bool value)).IsTrue();
        await Assert.That(value).IsTrue();
        await Assert.That(Values[Int32Index].TryGetBoolean(out _)).IsFalse();
    }

    [Test]
    public async Task Should_read_an_int32_and_miss_on_other_kinds()
    {
        await Assert.That(Values[Int32Index].TryGetInt32(out int value)).IsTrue();
        await Assert.That(value).IsEqualTo(42);
        await Assert.That(Values[StringIndex].TryGetInt32(out _)).IsFalse();
    }

    [Test]
    public async Task Should_not_widen_a_long_to_int32()
    {
        await Assert.That(Values[Int64Index].TryGetInt32(out _)).IsFalse();
    }

    [Test]
    public async Task Should_not_widen_an_int32_to_int64()
    {
        await Assert.That(Values[Int32Index].TryGetInt64(out _)).IsFalse();
    }

    [Test]
    public async Task Should_read_an_int64_and_miss_on_other_kinds()
    {
        await Assert.That(Values[Int64Index].TryGetInt64(out long value)).IsTrue();
        await Assert.That(value).IsEqualTo(5L);
        await Assert.That(Values[StringIndex].TryGetInt64(out _)).IsFalse();
    }

    [Test]
    public async Task Should_read_a_double_and_miss_on_other_kinds()
    {
        await Assert.That(Values[DoubleIndex].TryGetDouble(out double value)).IsTrue();
        await Assert.That(value).IsEqualTo(1.5);
        await Assert.That(Values[Int32Index].TryGetDouble(out _)).IsFalse();
    }

    [Test]
    public async Task Should_read_an_int_backed_enum()
    {
        await Assert.That(Values[IntEnumIndex].TryGetEnum(out IntColor value)).IsTrue();
        await Assert.That(value).IsEqualTo(IntColor.Green);
    }

    [Test]
    public async Task Should_read_a_byte_backed_enum()
    {
        await Assert.That(Values[ByteEnumIndex].TryGetEnum(out ByteSize value)).IsTrue();
        await Assert.That(value).IsEqualTo(ByteSize.Large);
    }

    [Test]
    public async Task Should_miss_an_enum_whose_value_is_not_integral()
    {
        TypeRef type = new(
            "global::E",
            NullableAnnotation.NotAnnotated,
            SpecialType.None,
            TypeKind.Enum,
            true
        );
        ConstantValue value = ConstantValue.ForEnum(type, "not a number");

        await Assert.That(value.TryGetEnum(out IntColor _)).IsFalse();
    }

    [Test]
    public async Task Should_miss_an_enum_on_a_plain_int()
    {
        await Assert.That(Values[Int32Index].TryGetEnum(out IntColor _)).IsFalse();
    }

    [Test]
    public async Task Should_read_a_type_as_a_type_ref()
    {
        await Assert.That(Values[TypeIndex].TryGetType(out TypeRef value)).IsTrue();
        await Assert.That(value.FullyQualifiedName).IsEqualTo("string");
        await Assert.That(Values[StringIndex].TryGetType(out _)).IsFalse();
    }

    [Test]
    public async Task Should_read_an_array_of_constant_values()
    {
        await Assert
            .That(Values[IntArrayIndex].TryGetArray(out EquatableArray<ConstantValue> value))
            .IsTrue();
        await Assert.That(value.Count).IsEqualTo(2);
        await Assert.That(value[1].TryGetInt32(out int second)).IsTrue();
        await Assert.That(second).IsEqualTo(2);
        await Assert.That(Values[StringIndex].TryGetArray(out _)).IsFalse();
    }

    [Test]
    public async Task Should_read_a_string_array_with_a_null_element()
    {
        await Assert
            .That(Values[StringArrayIndex].TryGetStringArray(out EquatableArray<string?> value))
            .IsTrue();
        await Assert.That(value.Count).IsEqualTo(2);
        await Assert.That(value[0]).IsEqualTo("a");
        await Assert.That(value[1]).IsNull();
    }

    [Test]
    public async Task Should_miss_a_string_array_on_non_string_elements()
    {
        await Assert.That(Values[IntArrayIndex].TryGetStringArray(out _)).IsFalse();
        await Assert.That(Values[MixedArrayIndex].TryGetStringArray(out _)).IsFalse();
        await Assert.That(Values[StringIndex].TryGetStringArray(out _)).IsFalse();
    }

    [Test]
    public async Task Should_report_null_and_miss_every_accessor_for_null()
    {
        ConstantValue value = Values[NullIndex];

        await Assert.That(value.IsNull).IsTrue();
        await Assert.That(Values[StringIndex].IsNull).IsFalse();
        await AssertEveryAccessorMisses(value);
    }

    [Test]
    public async Task Should_miss_every_accessor_for_error()
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

        ConstantValue value = GetValues(source, "Target", allowErrors: true).Single();

        await Assert.That(value.Kind).IsEqualTo(ConstantValueKind.Error);
        await Assert.That(value.IsNull).IsFalse();
        await AssertEveryAccessorMisses(value);
    }

    private static async Task AssertEveryAccessorMisses(ConstantValue value)
    {
        await Assert.That(value.TryGetString(out _)).IsFalse();
        await Assert.That(value.TryGetBoolean(out _)).IsFalse();
        await Assert.That(value.TryGetInt32(out _)).IsFalse();
        await Assert.That(value.TryGetInt64(out _)).IsFalse();
        await Assert.That(value.TryGetDouble(out _)).IsFalse();
        await Assert.That(value.TryGetEnum(out IntColor _)).IsFalse();
        await Assert.That(value.TryGetType(out _)).IsFalse();
        await Assert.That(value.TryGetArray(out _)).IsFalse();
        await Assert.That(value.TryGetStringArray(out _)).IsFalse();
    }

    private static ConstantValue[] GetValues(
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

        return type.GetAttributes()
            .Single()
            .ConstructorArguments.Select(ConstantValue.From)
            .ToArray();
    }
}
