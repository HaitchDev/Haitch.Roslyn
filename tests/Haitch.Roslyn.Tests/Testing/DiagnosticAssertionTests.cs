using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Testing;

public class DiagnosticAssertionTests
{
    // Test-only descriptors; RS2008 (release tracking) does not apply to a fixture that is never shipped.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor Warn = new(
        "TST001",
        "Warn",
        "Found class '{0}'",
        "Test",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor Other = new(
        "TST002",
        "Other",
        "Other for '{0}'",
        "Test",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );
#pragma warning restore RS2008

    // Every class gets TST001; a class named "Dup" gets it twice and one named "Both" also gets TST002.
    private sealed class ReportingGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var classes = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) =>
                    node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax,
                static (ctx, _) =>
                    (Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax)ctx.Node
            );

            context.RegisterSourceOutput(
                classes,
                static (spc, decl) =>
                {
                    var name = decl.Identifier.Text;
                    var location = decl.Identifier.GetLocation();
                    spc.ReportDiagnostic(Diagnostic.Create(Warn, location, name));
                    if (name == "Dup")
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(Warn, location, name));
                    }
                    if (name == "Both")
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(Other, location, name));
                    }
                }
            );
        }
    }

    private sealed class SilentGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) { }
    }

    private static GeneratorHarnessResult Run(
        IIncrementalGenerator generator,
        params string[] sources
    ) => GeneratorHarness.Run(generator, new GeneratorHarnessInput { Sources = sources });

    private const string One = "\n\n  class Foo { }";

    [Test]
    public async Task AssertNoDiagnostics_Passes_WhenNone()
    {
        var result = Run(new SilentGenerator(), One);

        await Assert.That(result.AssertNoDiagnostics()).IsSameReferenceAs(result);
    }

    [Test]
    public async Task AssertNoDiagnostics_Throws_ListingActual()
    {
        var result = Run(new ReportingGenerator(), One);

        var ex = Assert.Throws<GeneratorTestException>(() => result.AssertNoDiagnostics());

        await Assert.That(ex.Message).Contains("TST001 Warning Source0.cs(3,9): Found class 'Foo'");
    }

    [Test]
    public async Task AssertDiagnostic_Matches_AllFilters()
    {
        var result = Run(new ReportingGenerator(), One);

        var diagnostic = result.AssertDiagnostic(
            "TST001",
            DiagnosticSeverity.Warning,
            line: 3,
            column: 9,
            file: "Source0.cs",
            messageContains: "Foo"
        );

        await Assert.That(diagnostic.Id).IsEqualTo("TST001");
    }

    [Test]
    public async Task AssertDiagnostic_WrongId_Throws_ListingActual()
    {
        var result = Run(new ReportingGenerator(), One);

        var ex = Assert.Throws<GeneratorTestException>(() => result.AssertDiagnostic("NOPE"));

        await Assert.That(ex.Message).Contains("NOPE");
        await Assert.That(ex.Message).Contains("TST001 Warning Source0.cs(3,9)");
    }

    [Test]
    public async Task AssertDiagnostic_WrongSeverity_Throws()
    {
        var result = Run(new ReportingGenerator(), One);

        var ex = Assert.Throws<GeneratorTestException>(() =>
            result.AssertDiagnostic("TST001", DiagnosticSeverity.Error)
        );

        await Assert.That(ex.Message).Contains("TST001 Warning");
    }

    [Test]
    public async Task AssertDiagnostic_WrongLine_Throws()
    {
        var result = Run(new ReportingGenerator(), One);

        Assert.Throws<GeneratorTestException>(() => result.AssertDiagnostic("TST001", line: 4));
        await Assert.That(result.AssertDiagnostic("TST001", line: 3)).IsNotNull();
    }

    [Test]
    public async Task AssertDiagnostic_WrongColumn_Throws()
    {
        var result = Run(new ReportingGenerator(), One);

        Assert.Throws<GeneratorTestException>(() => result.AssertDiagnostic("TST001", column: 1));
        await Assert.That(result.AssertDiagnostic("TST001", column: 9)).IsNotNull();
    }

    [Test]
    public async Task AssertDiagnostic_WrongFile_Throws()
    {
        var result = Run(new ReportingGenerator(), One);

        Assert.Throws<GeneratorTestException>(() =>
            result.AssertDiagnostic("TST001", file: "Source1.cs")
        );
        await Assert.That(result.AssertDiagnostic("TST001", file: "Source0.cs")).IsNotNull();
    }

    [Test]
    public async Task AssertDiagnostic_MessageSubstringMissing_Throws()
    {
        var result = Run(new ReportingGenerator(), One);

        Assert.Throws<GeneratorTestException>(() =>
            result.AssertDiagnostic("TST001", messageContains: "Bar")
        );
        await Assert.That(result.AssertDiagnostic("TST001", messageContains: "Foo")).IsNotNull();
    }

    [Test]
    public async Task AssertDiagnostic_TwoMatches_Throws_ButFilterCanNarrow()
    {
        var result = Run(new ReportingGenerator(), "class Dup { }", "\nclass Foo { }");

        var ex = Assert.Throws<GeneratorTestException>(() => result.AssertDiagnostic("TST001"));
        await Assert.That(ex.Message).Contains("Source0.cs");

        var narrowed = result.AssertDiagnostic("TST001", file: "Source1.cs");
        await Assert.That(narrowed.Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(1);
    }

    [Test]
    public async Task AssertDiagnostic_DuplicateOnSameClass_Throws()
    {
        var result = Run(new ReportingGenerator(), "class Dup { }");

        Assert.Throws<GeneratorTestException>(() => result.AssertDiagnostic("TST001"));
        await Assert.That(result.Diagnostics.Length).IsEqualTo(2);
    }

    [Test]
    public async Task AssertDiagnostic_IgnoresOtherIds_AndSupportsChaining()
    {
        var result = Run(new ReportingGenerator(), "class Both { }");

        result.AssertDiagnostic("TST001");
        var other = result.AssertDiagnostic("TST002", DiagnosticSeverity.Info);

        await Assert.That(other.GetMessage()).Contains("Other for 'Both'");
    }
}
