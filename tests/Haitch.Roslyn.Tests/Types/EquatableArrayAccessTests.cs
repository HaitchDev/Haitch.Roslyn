using System.Collections.Immutable;
using System.Linq;
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
}