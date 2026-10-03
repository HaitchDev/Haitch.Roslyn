using System.Collections.Generic;
using Haitch.Roslyn.Diagnostics;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Diagnostics;

public class ResultCombineTests
{
    // Test-only descriptor; RS2008 (analyzer release tracking) does not apply to a fixture that is never shipped as a real analyzer rule.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor Descriptor = new(
        "HR0004",
        "Test diagnostic",
        "Test message '{0}'",
        "Test",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
#pragma warning restore RS2008

    [Test]
    public async Task Should_combine_two_successes_into_a_tuple()
    {
        Result<int> first = Result<int>.Success(1);
        Result<string> second = Result<string>.Success("a");

        Result<(int, string)> combined = Result.Combine(first, second);

        await Assert.That(combined.IsSuccess).IsTrue();
        await Assert.That(combined.Match(value => value, _ => (-1, "?"))).IsEqualTo((1, "a"));
    }

    [Test]
    public async Task Should_combine_three_successes_into_a_tuple()
    {
        Result<int> first = Result<int>.Success(1);
        Result<string> second = Result<string>.Success("a");
        Result<bool> third = Result<bool>.Success(true);

        Result<(int, string, bool)> combined = Result.Combine(first, second, third);

        await Assert.That(combined.IsSuccess).IsTrue();
        await Assert
            .That(combined.Match(value => value, _ => (-1, "?", false)))
            .IsEqualTo((1, "a", true));
    }

    [Test]
    public async Task Should_combine_two_results_with_one_failure_keeping_its_diagnostics()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "first");
        Result<int> first = Result<int>.Failure(diagnostic);
        Result<string> second = Result<string>.Success("a");

        Result<(int, string)> combined = Result.Combine(first, second);

        await Assert.That(combined.IsSuccess).IsFalse();
        await Assert.That(combined.Diagnostics.Count).IsEqualTo(1);
        await Assert.That(combined.Diagnostics[0]).IsEqualTo(diagnostic);
    }

    [Test]
    public async Task Should_combine_three_results_with_multiple_failures_keeping_all_diagnostics_in_order()
    {
        var firstDiagnostic = new DiagnosticInfo(Descriptor, null, "first");
        var thirdDiagnostic = new DiagnosticInfo(Descriptor, null, "third");
        Result<int> first = Result<int>.Failure(firstDiagnostic);
        Result<string> second = Result<string>.Success("a");
        Result<bool> third = Result<bool>.Failure(thirdDiagnostic);

        Result<(int, string, bool)> combined = Result.Combine(first, second, third);

        await Assert.That(combined.IsSuccess).IsFalse();
        await Assert.That(combined.Diagnostics.Count).IsEqualTo(2);
        await Assert.That(combined.Diagnostics[0]).IsEqualTo(firstDiagnostic);
        await Assert.That(combined.Diagnostics[1]).IsEqualTo(thirdDiagnostic);
    }

    [Test]
    public async Task Should_collect_all_successes_into_an_equatable_array()
    {
        var results = new List<Result<int>>
        {
            Result<int>.Success(1),
            Result<int>.Success(2),
            Result<int>.Success(3),
        };

        Result<EquatableArray<int>> collected = Result.Collect(results);

        await Assert.That(collected.IsSuccess).IsTrue();
        var values = collected.Match(value => value, _ => default);
        await Assert.That(values.Count).IsEqualTo(3);
        await Assert.That(values[0]).IsEqualTo(1);
        await Assert.That(values[1]).IsEqualTo(2);
        await Assert.That(values[2]).IsEqualTo(3);
    }

    [Test]
    public async Task Should_collect_with_one_failure_keeping_its_diagnostics()
    {
        var diagnostic = new DiagnosticInfo(Descriptor, null, "second");
        var results = new List<Result<int>>
        {
            Result<int>.Success(1),
            Result<int>.Failure(diagnostic),
            Result<int>.Success(3),
        };

        Result<EquatableArray<int>> collected = Result.Collect(results);

        await Assert.That(collected.IsSuccess).IsFalse();
        await Assert.That(collected.Diagnostics.Count).IsEqualTo(1);
        await Assert.That(collected.Diagnostics[0]).IsEqualTo(diagnostic);
    }

    [Test]
    public async Task Should_collect_with_multiple_failures_keeping_all_diagnostics_in_order()
    {
        var firstDiagnostic = new DiagnosticInfo(Descriptor, null, "first");
        var secondDiagnostic = new DiagnosticInfo(Descriptor, null, "second");
        var results = new List<Result<int>>
        {
            Result<int>.Failure(firstDiagnostic),
            Result<int>.Success(2),
            Result<int>.Failure(secondDiagnostic),
        };

        Result<EquatableArray<int>> collected = Result.Collect(results);

        await Assert.That(collected.IsSuccess).IsFalse();
        await Assert.That(collected.Diagnostics.Count).IsEqualTo(2);
        await Assert.That(collected.Diagnostics[0]).IsEqualTo(firstDiagnostic);
        await Assert.That(collected.Diagnostics[1]).IsEqualTo(secondDiagnostic);
    }

    [Test]
    public async Task Should_treat_a_default_result_as_a_failure_when_combining_two()
    {
        Result<int> first = default;
        Result<string> second = Result<string>.Success("a");

        Result<(int, string)> combined = Result.Combine(first, second);

        await Assert.That(combined.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Should_treat_a_default_result_as_a_failure_when_combining_three()
    {
        Result<int> first = default;
        Result<string> second = Result<string>.Success("a");
        Result<bool> third = Result<bool>.Success(true);

        Result<(int, string, bool)> combined = Result.Combine(first, second, third);

        await Assert.That(combined.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Should_treat_a_default_result_as_a_failure_when_collecting()
    {
        var results = new List<Result<int>>
        {
            Result<int>.Success(1),
            default,
            Result<int>.Success(3),
        };

        Result<EquatableArray<int>> collected = Result.Collect(results);

        await Assert.That(collected.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Should_collect_empty_input_into_a_success_with_an_empty_array()
    {
        var results = new List<Result<int>>();

        Result<EquatableArray<int>> collected = Result.Collect(results);

        await Assert.That(collected.IsSuccess).IsTrue();
        var values = collected.Match(value => value, _ => default);
        await Assert.That(values.Count).IsEqualTo(0);
    }
}
