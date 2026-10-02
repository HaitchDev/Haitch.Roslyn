using Haitch.Roslyn.Types;

namespace Haitch.Roslyn.Tests.Types;

public class EquatableArrayTests
{
    [Test]
    public async Task Should_be_able_to_create_an_empty_array()
    {
        EquatableArray<int> first = new();
        EquatableArray<int> second = new();

        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task Should_be_equal_when_contents_match()
    {
        var first = new EquatableArray<int>([1, 2, 3]);
        var second = new EquatableArray<int>([1, 2, 3]);

        await Assert.That(first.Equals(second)).IsTrue();
        await Assert.That(first == second).IsTrue();
        await Assert.That(first != second).IsFalse();
    }

    [Test]
    public async Task Should_not_be_equal_when_order_differs()
    {
        var first = new EquatableArray<int>([1, 2, 3]);
        var second = new EquatableArray<int>([3, 2, 1]);

        await Assert.That(first.Equals(second)).IsFalse();
        await Assert.That(first == second).IsFalse();
        await Assert.That(first != second).IsTrue();
    }

    [Test]
    public async Task Should_not_be_equal_when_length_differs()
    {
        var first = new EquatableArray<int>([1, 2, 3]);
        var second = new EquatableArray<int>([1, 2]);

        await Assert.That(first.Equals(second)).IsFalse();
    }

    [Test]
    public async Task Should_treat_default_as_equal_to_empty()
    {
        EquatableArray<int> first = default;
        var second = new EquatableArray<int>([]);

        await Assert.That(first.Equals(second)).IsTrue();
        await Assert.That(first == second).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    public async Task Should_not_change_when_source_array_is_mutated_after_construction()
    {
        var source = new[] { 1, 2, 3 };
        var array = new EquatableArray<int>(source);

        source[0] = 99;

        await Assert.That(array.Equals(new EquatableArray<int>([1, 2, 3]))).IsTrue();
    }

    [Test]
    public async Task Should_treat_null_elements_as_equal()
    {
        var first = new EquatableArray<string>([null!, "a"]);
        var second = new EquatableArray<string>([null!, "a"]);

        await Assert.That(first.Equals(second)).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    public async Task Should_be_equal_via_object_equals_when_contents_match()
    {
        var first = new EquatableArray<int>([1, 2, 3]);
        object second = new EquatableArray<int>([1, 2, 3]);

        await Assert.That(first.Equals(second)).IsTrue();
    }

    [Test]
    public async Task Should_not_be_equal_via_object_equals_to_different_type()
    {
        var first = new EquatableArray<int>([1, 2, 3]);
        object second = "not an equatable array";

        await Assert.That(first.Equals(second)).IsFalse();
    }

    [Test]
    public async Task Should_have_equal_hash_codes_when_contents_match()
    {
        var first = new EquatableArray<int>([1, 2, 3]);
        var second = new EquatableArray<int>([1, 2, 3]);

        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }
}