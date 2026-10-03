using System;
using Haitch.Roslyn.Diagnostics;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Diagnostics;

public class ResultTests
{
    // Test-only descriptor; RS2008 (analyzer release tracking) does not apply to a fixture that is never shipped as a real analyzer rule.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor Descriptor = new(
        "HR0003",
        "Test diagnostic",
        "Test message '{0}'",
        "Test",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
#pragma warning restore RS2008

    [Test]
    public async Task Should_be_a_success_when_created_from_a_value()
    {
        Result<int> result = Result<int>.Success(42);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Match(value => value, _ => -1)).IsEqualTo(42);
    }

    [Test]
    public async Task Should_be_a_failure_when_created_from_a_diagnostic()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "value");

        Result<int> result = Result<int>.Failure(diagnostic);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Diagnostics.Count).IsEqualTo(1);
        await Assert.That(result.Diagnostics[0]).IsEqualTo(diagnostic);
    }

    [Test]
    public async Task Should_throw_when_created_from_an_empty_diagnostic_array()
    {
        await Assert
            .That(() => Result<int>.Failure(default(EquatableArray<DiagnosticInfo>)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_convert_implicitly_from_a_value()
    {
        Result<int> result = 42;

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Match(value => value, _ => -1)).IsEqualTo(42);
    }

    [Test]
    public async Task Should_convert_implicitly_from_a_diagnostic()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "value");

        Result<int> result = diagnostic;

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Diagnostics[0]).IsEqualTo(diagnostic);
    }

    [Test]
    public async Task Should_map_the_value_of_a_success()
    {
        Result<int> result = Result<int>.Success(2);

        Result<int> mapped = result.Map(value => value * 2);

        await Assert.That(mapped.Match(value => value, _ => -1)).IsEqualTo(4);
    }

    [Test]
    public async Task Should_short_circuit_map_on_a_failure()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "value");
        Result<int> result = Result<int>.Failure(diagnostic);

        Result<string> mapped = result.Map(value => value.ToString());

        await Assert.That(mapped.IsSuccess).IsFalse();
        await Assert.That(mapped.Diagnostics[0]).IsEqualTo(diagnostic);
    }

    [Test]
    public async Task Should_bind_the_value_of_a_success()
    {
        Result<int> result = Result<int>.Success(2);

        Result<int> bound = result.Bind(value => Result<int>.Success(value * 3));

        await Assert.That(bound.Match(value => value, _ => -1)).IsEqualTo(6);
    }

    [Test]
    public async Task Should_short_circuit_bind_on_a_failure()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "value");
        Result<int> result = Result<int>.Failure(diagnostic);

        Result<int> bound = result.Bind(value => Result<int>.Success(value * 3));

        await Assert.That(bound.IsSuccess).IsFalse();
        await Assert.That(bound.Diagnostics[0]).IsEqualTo(diagnostic);
    }

    [Test]
    public async Task Should_match_the_success_branch()
    {
        Result<int> result = Result<int>.Success(5);

        var matched = result.Match(
            value => $"ok:{value}",
            diagnostics => $"error:{diagnostics.Count}"
        );

        await Assert.That(matched).IsEqualTo("ok:5");
    }

    [Test]
    public async Task Should_match_the_failure_branch()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "value");
        Result<int> result = Result<int>.Failure(diagnostic);

        var matched = result.Match(
            value => $"ok:{value}",
            diagnostics => $"error:{diagnostics.Count}"
        );

        await Assert.That(matched).IsEqualTo("error:1");
    }

    [Test]
    public async Task Should_be_equal_for_two_equal_successes()
    {
        Result<int> first = Result<int>.Success(7);
        Result<int> second = Result<int>.Success(7);

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    public async Task Should_be_unequal_for_two_different_successes()
    {
        Result<int> first = Result<int>.Success(7);
        Result<int> second = Result<int>.Success(8);

        await Assert.That(first).IsNotEqualTo(second);
    }

    [Test]
    public async Task Should_be_equal_for_two_equal_failures()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "value");
        Result<int> first = Result<int>.Failure(diagnostic);
        Result<int> second = Result<int>.Failure(diagnostic);

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    public async Task Should_be_unequal_between_a_success_and_a_failure()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "value");
        Result<int> success = Result<int>.Success(1);
        Result<int> failure = Result<int>.Failure(diagnostic);

        await Assert.That(success).IsNotEqualTo(failure);
    }

    [Test]
    public async Task Should_not_be_a_success_by_default()
    {
        Result<int> result = default;

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Should_match_the_failure_branch_with_no_diagnostics_by_default()
    {
        Result<int> result = default;

        var matched = result.Match(
            value => $"ok:{value}",
            diagnostics => $"error:{diagnostics.Count}"
        );

        await Assert.That(matched).IsEqualTo("error:0");
    }

    [Test]
    public async Task Should_throw_when_created_from_a_null_reference_value()
    {
        await Assert.That(() => Result<string>.Success(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Should_get_the_value_of_a_success_via_try_get_value()
    {
        Result<int> result = Result<int>.Success(9);

        var found = result.TryGetValue(out var value);

        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo(9);
    }

    [Test]
    public async Task Should_not_get_a_value_of_a_failure_via_try_get_value()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "value");
        Result<int> result = Result<int>.Failure(diagnostic);

        var found = result.TryGetValue(out var value);

        await Assert.That(found).IsFalse();
        await Assert.That(value).IsEqualTo(default(int));
    }
}
