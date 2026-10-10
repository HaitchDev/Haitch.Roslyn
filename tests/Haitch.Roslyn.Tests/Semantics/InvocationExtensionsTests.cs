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

    [Test]
    public async Task Should_not_find_an_omitted_optional_argument()
    {
        IOperation? argument = ArgumentFor("Rules.Length(1)", "max");

        await Assert.That(argument).IsNull();
    }

    [Test]
    public async Task Should_not_find_an_argument_of_a_call_that_does_not_bind()
    {
        IOperation? argument = ArgumentFor("Rules.Missing(max: 2)", "max");

        await Assert.That(argument).IsNull();
    }

    [Test]
    public async Task Should_resolve_a_method_on_a_non_generic_type()
    {
        string? name = ResolvedNameIn("Rules.Length(1)", "Rules");

        await Assert.That(name).IsEqualTo("Length");
    }

    [Test]
    public async Task Should_resolve_a_method_on_a_constructed_generic_type_by_its_definition()
    {
        string? name = ResolvedNameIn("new Ns.Box<int>().Put(1)", "Ns.Box`1");

        await Assert.That(name).IsEqualTo("Put");
    }

    [Test]
    public async Task Should_resolve_an_extension_method_on_a_receiver_to_its_declaration()
    {
        IMethodSymbol? method = ResolveMethodIn("\"abc\".Matches(\"a+\")", "Rules");

        await Assert.That(method?.MethodKind).IsEqualTo(MethodKind.Ordinary);
    }

    [Test]
    public async Task Should_not_resolve_a_method_on_another_type()
    {
        IMethodSymbol? method = ResolveMethodIn("Rules.Length(1)", "Caller");

        await Assert.That(method).IsNull();
    }

    private static string? ResolvedNameIn(string call, string containingType) =>
        ResolveMethodIn(call, containingType)?.Name;

    private static IMethodSymbol? ResolveMethodIn(string call, string containingType)
    {
        (InvocationExpressionSyntax invocation, SemanticModel model) = Bind(call);

        return invocation.ResolveMethodIn(model, containingType, CancellationToken.None);
    }

    private static object? ConstantArgumentFor(string call, string parameter) =>
        ArgumentFor(call, parameter)?.ConstantValue.Value;

    private static IOperation? ArgumentFor(string call, string parameter)
    {
        (InvocationExpressionSyntax invocation, SemanticModel model) = Bind(call);

        return invocation.ArgumentFor(model, parameter, CancellationToken.None);
    }

    private static (InvocationExpressionSyntax Invocation, SemanticModel Model) Bind(string call)
    {
        CSharpCompilation compilation = CompilationHelper.Compile(
            $$"""
            static class Rules
            {
                public static void Length(int min, int max = 10) { }

                public static void Matches(this string text, string pattern) { }
            }

            namespace Ns
            {
                class Box<T>
                {
                    public void Put(T item) { }
                }
            }

            class Caller
            {
                void Call()
                {
                    {{call}};
                }
            }
            """,
            allowErrors: true
        );
        SyntaxTree tree = compilation.SyntaxTrees.Single();
        InvocationExpressionSyntax invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();

        return (invocation, compilation.GetSemanticModel(tree));
    }
}
