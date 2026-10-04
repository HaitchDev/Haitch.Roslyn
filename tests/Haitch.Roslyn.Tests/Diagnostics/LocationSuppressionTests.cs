using System.Collections.Immutable;
using System.Linq;
using Haitch.Roslyn.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Haitch.Roslyn.Tests.Diagnostics;

#pragma warning disable RS2008, RS1037, RS1001, RS1036
public class LocationSuppressionTests
{
    private const string Source = """
        #pragma warning disable LSP001
        class Sample { }
        #pragma warning restore LSP001
        """;

    private static readonly DiagnosticDescriptor Descriptor = new(
        "LSP001",
        "Test",
        "Reported on {0}",
        "Test",
        DiagnosticSeverity.Warning,
        true
    );

    [Test]
    public async Task Should_round_trip_to_a_source_tree_location()
    {
        (CSharpCompilation compilation, Location original) = Parse();

        Location roundTripped = LocationInfo.From(original)!.ToLocation(compilation);

        await Assert.That(roundTripped.Kind).IsEqualTo(original.Kind);
        await Assert.That(roundTripped.SourceTree).IsEqualTo(original.SourceTree);
        await Assert.That(roundTripped.SourceSpan).IsEqualTo(original.SourceSpan);
    }

    [Test]
    public async Task Should_fall_back_to_an_unbound_location_when_no_tree_matches()
    {
        (CSharpCompilation compilation, Location original) = Parse();
        LocationInfo info = LocationInfo.From(original)! with { FilePath = "other.cs" };

        Location location = info.ToLocation(compilation);

        await Assert.That(location.SourceTree).IsNull();
        await Assert.That(location.GetLineSpan().Path).IsEqualTo("other.cs");
    }

    [Test]
    public async Task Should_fall_back_to_an_unbound_location_when_several_trees_match()
    {
        (CSharpCompilation compilation, Location original) = Parse();
        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(Source, path: "sample.cs")
        );

        Location location = LocationInfo.From(original)!.ToLocation(compilation);

        await Assert.That(location.SourceTree).IsNull();
    }

    [Test]
    public async Task Should_fall_back_to_an_unbound_location_when_the_span_runs_past_the_tree()
    {
        (CSharpCompilation compilation, Location original) = Parse();
        LocationInfo info = LocationInfo.From(original)! with
        {
            Span = new TextSpan(Source.Length - 2, 10),
        };

        Location location = info.ToLocation(compilation);

        await Assert.That(location.SourceTree).IsNull();
    }

    [Test]
    public async Task Should_let_a_pragma_suppress_a_diagnostic_reported_at_the_round_tripped_location()
    {
        (CSharpCompilation compilation, Location original) = Parse();
        Location roundTripped = LocationInfo.From(original)!.ToLocation(compilation);

        int atOriginal = await Count(compilation, original);
        int atRoundTripped = await Count(compilation, roundTripped);

        await Assert.That(atOriginal).IsEqualTo(0);
        await Assert.That(atRoundTripped).IsEqualTo(0);
    }

    private static async Task<int> Count(CSharpCompilation compilation, Location location)
    {
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers([
            new ReportAtAnalyzer(location),
        ]);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAllDiagnosticsAsync();

        return diagnostics.Count(d => d.Id == "LSP001");
    }

    private static (CSharpCompilation, Location) Parse()
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(Source, path: "sample.cs");
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]
        );
        var declaration = (ClassDeclarationSyntax)
            tree.GetRoot().DescendantNodes().First(n => n is ClassDeclarationSyntax);

        return (compilation, declaration.Identifier.GetLocation());
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class ReportAtAnalyzer(Location location) : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationAction(c =>
                c.ReportDiagnostic(Diagnostic.Create(Descriptor, location, "Sample"))
            );
        }
    }
}
