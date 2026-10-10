using Haitch.Roslyn.Semantics;
using Haitch.Roslyn.Tests.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Semantics;

public class InvocationExtensionsTests
{
    [Test]
    public async Task Should_find_a_named_argument()
    {
        object? value = ConstantArgumentFor("Rules.Length(min: 1, max: 2)", "max");

        await Assert.That(value).IsEqualTo(2);
    }

    [Test]
    public async Task Should_find_a_positional_argument()
    {
        object? value = ConstantArgumentFor("Rules.Length(1, 2)", "max");

        await Assert.That(value).IsEqualTo(2);
    }

    [Test]
    public async Task Should_find_a_named_argument_written_out_of_order()
    {
        object? value = ConstantArgumentFor("Rules.Length(max: 2, min: 1)", "min");

        await Assert.That(value).IsEqualTo(1);
    }

    [Test]
    public async Task Should_find_an_extension_methods_receiver_by_its_this_parameter()
    {
        object? value = ConstantArgumentFor("\"abc\".Matches(\"a+\")", "text");

        await Assert.That(value).IsEqualTo("abc");
    }

    private static object? ConstantArgumentFor(string call, string parameter) =>
        ArgumentFor(call, parameter)?.ConstantValue.Value;

    private static IOperation? ArgumentFor(string call, string parameter)
    {
        CSharpCompilation compilation = CompilationHelper.Compile(
            $$"""
            static class Rules
            {
                public static void Length(int min, int max = 10) { }

                public static void Matches(this string text, string pattern) { }
            }

            class Caller
            {
                void Call()
                {
                    {{call}};
                }
            }
            """
        );
        SyntaxTree tree = compilation.SyntaxTrees.Single();
        InvocationExpressionSyntax invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();

        return invocation.ArgumentFor(
            compilation.GetSemanticModel(tree),
            parameter,
            CancellationToken.None
        );
    }
}
