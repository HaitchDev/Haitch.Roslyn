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
        IOperation? argument = ArgumentFor("Rules.Length(min: 1, max: 2)", "max");

        await Assert.That(argument?.ConstantValue.Value).IsEqualTo(2);
    }

    [Test]
    public async Task Should_find_a_positional_argument()
    {
        IOperation? argument = ArgumentFor("Rules.Length(1, 2)", "max");

        await Assert.That(argument?.ConstantValue.Value).IsEqualTo(2);
    }

    [Test]
    public async Task Should_find_a_named_argument_written_out_of_order()
    {
        IOperation? argument = ArgumentFor("Rules.Length(max: 2, min: 1)", "min");

        await Assert.That(argument?.ConstantValue.Value).IsEqualTo(1);
    }

    [Test]
    public async Task Should_find_an_extension_methods_receiver_by_its_this_parameter()
    {
        IOperation? argument = ArgumentFor("\"abc\".Matches(\"a+\")", "text");

        await Assert.That(argument?.ConstantValue.Value).IsEqualTo("abc");
    }

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
