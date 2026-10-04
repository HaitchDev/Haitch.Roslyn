using System.Linq;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Writing;

public class MethodKindRejectionTests
{
    private static MethodModel ModelOf(string member, MethodKind kind)
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            $"public class C {{ {member} }}",
            "C"
        );
        IMethodSymbol symbol = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Single(m => m.MethodKind == kind);

        return MethodModel.From(symbol);
    }

    private static TypeModel Sample()
    {
        return TypeModel.From(
            CompilationHelper.GetNamedTypeSymbol("public partial class Sample { }", "Sample")
        );
    }

    [Test]
    [Arguments("public C(int x) { }", MethodKind.Constructor)]
    [Arguments("static C() { }", MethodKind.StaticConstructor)]
    [Arguments("~C() { }", MethodKind.Destructor)]
    [Arguments("public static C operator +(C a, C b) => a;", MethodKind.UserDefinedOperator)]
    [Arguments("public static implicit operator int(C a) => 1;", MethodKind.Conversion)]
    public async Task Should_reject_a_non_ordinary_method_kind_in_WriteMethodSignature(
        string member,
        MethodKind kind
    )
    {
        MethodModel model = ModelOf(member, kind);
        SourceWriter writer = new();

        ArgumentException? thrown = Assert.Throws<ArgumentException>(() =>
            writer.WriteMethodSignature(model)
        );

        await Assert.That(thrown!.Message).Contains(kind.ToString());
    }

    [Test]
    [Arguments("public C(int x) { }", MethodKind.Constructor)]
    [Arguments("static C() { }", MethodKind.StaticConstructor)]
    [Arguments("~C() { }", MethodKind.Destructor)]
    [Arguments("public static C operator +(C a, C b) => a;", MethodKind.UserDefinedOperator)]
    [Arguments("public static implicit operator int(C a) => 1;", MethodKind.Conversion)]
    public async Task Should_reject_a_non_ordinary_method_kind_in_TypeScope_Method(
        string member,
        MethodKind kind
    )
    {
        MethodModel model = ModelOf(member, kind);
        SourceWriter writer = new();
        ArgumentException? thrown = null;

        using (var file = writer.File())
        {
            using var type = file.Type(Sample());
            try
            {
                type.Method(model).Dispose();
            }
            catch (ArgumentException ex)
            {
                thrown = ex;
            }
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains(kind.ToString());
        await Assert
            .That(thrown.Message)
            .Contains("only Ordinary and ExplicitInterfaceImplementation methods can be written");
    }

    [Test]
    public async Task Should_still_write_an_ordinary_method()
    {
        MethodModel model = ModelOf("public int Ordinary() => 1;", MethodKind.Ordinary);
        SourceWriter writer = new();

        writer.WriteMethodSignature(model);

        await Assert.That(writer.ToString()).Contains("Ordinary()");
    }

    [Test]
    public async Task Should_still_write_an_explicit_interface_implementation()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            "public class E : System.IDisposable { void System.IDisposable.Dispose() { } }",
            "E"
        );
        MethodModel model = MethodModel.From(
            type.GetMembers()
                .OfType<IMethodSymbol>()
                .Single(m => m.MethodKind == MethodKind.ExplicitInterfaceImplementation)
        );
        SourceWriter writer = new();

        writer.WriteMethodSignature(model);

        await Assert.That(writer.ToString()).Contains("System.IDisposable.Dispose()");
    }
}
