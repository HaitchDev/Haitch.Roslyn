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

    private static IOperation? ArgumentFor(string call, string parameter)
    {
        CSharpCompilation compilation = CompilationHelper.Compile(
            $$"""
            static class Rules
            {
                public static void Length(int min, int max = 10) { }
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
