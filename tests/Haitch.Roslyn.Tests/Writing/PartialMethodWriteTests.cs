using System.Linq;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Writing;

public class PartialMethodWriteTests
{
    [Test]
    [Arguments("partial void M();", "partial void M() { }")]
    [Arguments("public partial int M();", "public partial int M() => 1;")]
    public async Task Should_keep_partial_on_the_implementation_part(
        string definition,
        string implementation
    )
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            $"public partial class C {{ {definition} {implementation} }}",
            "C"
        );
        IMethodSymbol symbol = type.GetMembers("M").OfType<IMethodSymbol>().Single();
        IMethodSymbol implementationPart = symbol.PartialImplementationPart ?? symbol;

        MethodModel model = MethodModel.From(implementationPart);

        SourceWriter writer = new();
        writer.WriteMethodSignature(model);

        await Assert.That(writer.ToString()).Contains("partial ");
    }

    [Test]
    public async Task Should_keep_the_definition_part_unchanged()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            "public partial class C { public partial int M(); public partial int M() => 1; }",
            "C"
        );
        IMethodSymbol symbol = type.GetMembers("M").OfType<IMethodSymbol>().Single();

        MethodModel model = MethodModel.From(symbol);

        SourceWriter writer = new();
        writer.WriteMethodSignature(model);

        await Assert.That(model.IsPartial).IsTrue();
        await Assert.That(model.IsPartialDefinition).IsTrue();
        await Assert.That(writer.ToString()).Contains("public partial int M();");
    }

    [Test]
    public async Task Should_not_write_partial_for_a_non_partial_method()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            "public class C { public int M() => 1; }",
            "C"
        );
        MethodModel model = MethodModel.From(type.GetMembers("M").OfType<IMethodSymbol>().Single());

        SourceWriter writer = new();
        writer.WriteMethodSignature(model);

        await Assert.That(model.IsPartial).IsFalse();
        await Assert.That(writer.ToString()).DoesNotContain("partial");
    }

    [Test]
    [Arguments("partial void M();", "partial void M() { }", "")]
    [Arguments("public partial int M();", "public partial int M() => 1;", "return 1;")]
    public async Task Should_emit_an_implementation_that_compiles_next_to_the_definition(
        string definition,
        string implementation,
        string bodyLine
    )
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            $"public partial class C {{ {definition} {implementation} }}",
            "C"
        );
        IMethodSymbol symbol = type.GetMembers("M").OfType<IMethodSymbol>().Single();
        MethodModel model = MethodModel.From(symbol.PartialImplementationPart ?? symbol);

        SourceWriter writer = new();
        using (var file = writer.File())
        using (var scope = file.Type(TypeModel.From(type)))
        using (var body = scope.Method(model))
        {
            if (bodyLine.Length > 0)
            {
                body.Line(bodyLine);
            }
        }

        var compilation = CompilationHelper.Compile(
            $"#nullable enable\npublic partial class C {{ {definition} }}\n" + writer.ToString()
        );
        var problems = compilation
            .GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToArray();

        await Assert.That(problems).IsEmpty();
    }
}
