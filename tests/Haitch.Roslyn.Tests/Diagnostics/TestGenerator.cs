using System;
using Haitch.Roslyn.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Diagnostics;

// Minimal generator exercising ResultPipelineExtensions: a class named "Bad*" fails with a
// diagnostic, everything else succeeds and gets its name emitted as generated source. Used to
// prove diagnostic reporting, value-only output, and incremental caching of the value step.
internal sealed class TestGenerator : IIncrementalGenerator
{
    public const string ValuesStepName = "TestGenerator.Values";

    // Test-only descriptor; RS2008 (analyzer release tracking) does not apply to a fixture that is never shipped as a real analyzer rule.
#pragma warning disable RS2008
    public static readonly DiagnosticDescriptor BadNameDescriptor = new(
        "TEST001",
        "Bad class name",
        "Class '{0}' must not start with 'Bad'",
        "Test",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<Result<string>> results = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => Transform((ClassDeclarationSyntax)ctx.Node));

        IncrementalValuesProvider<string> values = results.ReportDiagnostics(context, ValuesStepName);

        context.RegisterSourceOutput(values,
            static (spc, value) => spc.AddSource($"{value}.g.cs", $"// generated for {value}\n"));
    }

    private static Result<string> Transform(ClassDeclarationSyntax classDeclaration)
    {
        string name = classDeclaration.Identifier.Text;

        return name.StartsWith("Bad", StringComparison.Ordinal)
            ? Result<string>.Failure(DiagnosticInfo.Create(BadNameDescriptor, classDeclaration.GetLocation(), name))
            : Result<string>.Success(name);
    }
}