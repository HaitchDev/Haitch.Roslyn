using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.CSharp15.Tests;

public class CSharp15SmokeTests
{
    private const string Sources = """
        #nullable enable
        public record Cat(string Name);
        public record Dog(string Name);
        public union Pet(Cat, Dog);
        public closed record Shape;
        """;

    [Test]
    public async Task UnionAndClosedRecord_CompileAtPreview_WithZeroDiagnostics()
    {
        var result = GeneratorHarness.Run(
            new NoOpGenerator(),
            [Sources, CSharp15Polyfills.Source],
            parseOptions: CSharp15.ParseOptions
        );

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.Compilation.GetDiagnostics()).IsEmpty();
    }

    [Test]
    public async Task UnionAndClosedRecord_FailAtCSharp14()
    {
        var csharp14 = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14);

        var exception = await Assert
            .That(() =>
                GeneratorHarness.Run(
                    new NoOpGenerator(),
                    [Sources, CSharp15Polyfills.Source],
                    parseOptions: csharp14
                )
            )
            .Throws<GeneratorTestException>();

        // `closed` gets the feature diagnostic; `union` is not a keyword before C# 15 so it parses as a type name.
        await Assert
            .That(exception?.Message)
            .Contains("CS8652: The feature 'closed classes'")
            .And.Contains("CS0246");
    }

    private sealed class NoOpGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) { }
    }
}
