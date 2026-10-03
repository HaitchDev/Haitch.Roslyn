using System.Collections;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Haitch.Roslyn.Types;

namespace Haitch.Roslyn.Tests.Types;

public class EquatableArrayAccessTests
{
    [Test]
    public async Task Should_report_count()
    {
        var array = new EquatableArray<int>([1, 2, 3]);

        await Assert.That(array.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Should_report_zero_count_for_default()
    {
        EquatableArray<int> array = default;

        await Assert.That(array.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Should_report_is_empty_for_default_and_empty()
    {
        EquatableArray<int> first = default;
        var second = new EquatableArray<int>([]);

        await Assert.That(first.IsEmpty).IsTrue();
        await Assert.That(second.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Should_report_is_not_empty_when_populated()
    {
        var array = new EquatableArray<int>([1]);

        await Assert.That(array.IsEmpty).IsFalse();
    }

    [Test]
    public async Task Should_allow_indexer_access()
    {
        var array = new EquatableArray<int>([1, 2, 3]);

        await Assert.That(array[0]).IsEqualTo(1);
        await Assert.That(array[1]).IsEqualTo(2);
        await Assert.That(array[2]).IsEqualTo(3);
    }

    [Test]
    public async Task Should_enumerate_all_items_in_order()
    {
        var array = new EquatableArray<int>([1, 2, 3]);
        List<int> items = [];

        foreach (var item in array)
        {
            items.Add(item);
        }

        await Assert.That(items.SequenceEqual([1, 2, 3])).IsTrue();
    }

    [Test]
    public async Task Should_return_false_from_move_next_on_default_enumerator()
    {
        var enumerator = default(EquatableArray<int>.Enumerator);

        await Assert.That(enumerator.MoveNext()).IsFalse();
    }

    [Test]
    public async Task Should_throw_when_index_out_of_range()
    {
        var array = new EquatableArray<int>([1, 2, 3]);

        await Assert.That(() => array[3]).Throws<IndexOutOfRangeException>();
    }

    [Test]
    public async Task Should_expose_as_span_with_same_content()
    {
        var array = new EquatableArray<int>([1, 2, 3]);

        var span = array.AsSpan();

        await Assert.That(span.ToArray().SequenceEqual([1, 2, 3])).IsTrue();
    }

    [Test]
    public async Task Should_convert_array_to_equatable_array()
    {
        int[] source = [1, 2, 3];

        var array = source.ToEquatableArray();

        await Assert.That(array.Equals(new EquatableArray<int>([1, 2, 3]))).IsTrue();
    }

    [Test]
    public async Task Should_not_change_when_source_array_is_mutated_after_conversion()
    {
        int[] source = [1, 2, 3];
        var array = source.ToEquatableArray();

        source[0] = 99;

        await Assert.That(array.Equals(new EquatableArray<int>([1, 2, 3]))).IsTrue();
    }

    [Test]
    public async Task Should_convert_immutable_array_to_equatable_array()
    {
        var source = ImmutableArray.Create(1, 2, 3);

        var array = source.ToEquatableArray();

        await Assert.That(array.Equals(new EquatableArray<int>([1, 2, 3]))).IsTrue();
    }

    [Test]
    public async Task Should_convert_default_immutable_array_to_empty_equatable_array()
    {
        ImmutableArray<int> source = default;

        var array = source.ToEquatableArray();

        await Assert.That(array.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Should_convert_enumerable_to_equatable_array()
    {
        IEnumerable<int> source = new List<int> { 1, 2, 3 };

        var array = source.ToEquatableArray();

        await Assert.That(array.Equals(new EquatableArray<int>([1, 2, 3]))).IsTrue();
    }

    [Test]
    public async Task Should_pass_populated_array_as_read_only_list()
    {
        var array = new EquatableArray<int>([1, 2, 3]);

        var items = Materialize(array);

        await Assert.That(items.SequenceEqual([1, 2, 3])).IsTrue();
    }

    [Test]
    public async Task Should_pass_default_array_as_empty_read_only_list()
    {
        EquatableArray<int> array = default;

        var items = Materialize(array);

        await Assert.That(items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Should_expose_count_and_indexer_through_read_only_list()
    {
        IReadOnlyList<int> list = new EquatableArray<int>([4, 5, 6]);

        await Assert.That(list.Count).IsEqualTo(3);
        await Assert.That(list[2]).IsEqualTo(6);
    }

    [Test]
    public async Task Should_support_linq_select_and_count()
    {
        var array = new EquatableArray<int>([1, 2, 3]);

        var doubled = array.Select(x => x * 2).ToList();

        await Assert.That(doubled.SequenceEqual([2, 4, 6])).IsTrue();
        await Assert.That(array.Count()).IsEqualTo(3);
    }

    [Test]
    public async Task Should_enumerate_default_as_empty_through_enumerable()
    {
        EquatableArray<int> array = default;
        IEnumerable<int> enumerable = array;

        var items = enumerable.ToList();

        await Assert.That(items.Count).IsEqualTo(0);
        await Assert.That(array.Select(x => x).Count()).IsEqualTo(0);
    }

    [Test]
    public async Task Should_enumerate_through_non_generic_enumerable()
    {
        IEnumerable enumerable = new EquatableArray<int>([1, 2]);
        List<object> items = [];

        foreach (var item in enumerable)
        {
            items.Add(item);
        }

        await Assert.That(items.Count).IsEqualTo(2);
        await Assert.That(items[1]).IsEqualTo(2);
    }

    [Test]
    public async Task Should_return_same_backing_array_when_converting_equatable_array()
    {
        var source = new EquatableArray<int>([1, 2, 3]);

        var result = source.ToEquatableArray();

        await Assert
            .That(
                Unsafe.AreSame(
                    in MemoryMarshal.GetReference(source.AsSpan()),
                    in MemoryMarshal.GetReference(result.AsSpan())
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Should_throw_when_indexing_default_through_read_only_list()
    {
        IReadOnlyList<int> list = default(EquatableArray<int>);

        await Assert.That(() => list[0]).Throws<IndexOutOfRangeException>();
    }

    [Test]
    public async Task Should_build_from_collection_expression()
    {
        EquatableArray<int> values = [1, 2, 3];

        await Assert.That(values.Equals(new EquatableArray<int>([1, 2, 3]))).IsTrue();
    }

    [Test]
    public async Task Should_build_empty_collection_expression_equal_to_default_with_equal_hash()
    {
        EquatableArray<int> values = [];
        EquatableArray<int> defaulted = default;

        await Assert.That(values.Equals(defaulted)).IsTrue();
        await Assert.That(values.GetHashCode()).IsEqualTo(defaulted.GetHashCode());
    }

    [Test]
    public async Task Should_bind_empty_collection_expression_to_collection_builder()
    {
        EquatableArray<int> values = [];
        var field = typeof(EquatableArray<int>).GetField(
            "_array",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );

        await Assert.That(field).IsNotNull().Because("EquatableArray<T> must have a private _array field");

        // Create returns default (null backing array); the parameterless constructor would not.
        await Assert.That(field!.GetValue(values)).IsNull();
    }

    [Test]
    public async Task Should_build_from_collection_expression_with_enumerable_spread()
    {
        EquatableArray<int> values = [.. new List<int> { 1, 2 }, 3];

        await Assert.That(values.Equals(new EquatableArray<int>([1, 2, 3]))).IsTrue();
    }

    [Test]
    public async Task Should_build_from_collection_expression_with_spread()
    {
        EquatableArray<int> existing = [1, 2, 3];

        EquatableArray<int> values = [.. existing, 4];

        await Assert.That(values.Equals(new EquatableArray<int>([1, 2, 3, 4]))).IsTrue();
    }

    [Test]
    public async Task Should_copy_span_when_building_from_collection_expression()
    {
        int[] source = [1, 2, 3];

        var values = EquatableArray.Create<int>(source);
        source[0] = 99;

        await Assert.That(values.Equals(new EquatableArray<int>([1, 2, 3]))).IsTrue();
    }

    private static List<int> Materialize(IReadOnlyList<int> list)
    {
        List<int> result = [];
        for (var i = 0; i < list.Count; i++)
        {
            result.Add(list[i]);
        }

        return result;
    }
}
