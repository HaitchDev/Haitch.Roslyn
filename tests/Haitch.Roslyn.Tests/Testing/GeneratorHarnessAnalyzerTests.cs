using System.Collections.Immutable;
using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haitch.Roslyn.Tests.Testing;

public class GeneratorHarnessAnalyzerTests
{
    private sealed class DelegateGenerator(Action<IncrementalGeneratorInitializationContext> init)
        : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) => init(context);
    }

    private static readonly DelegateGenerator NoOutput = new(_ => { });

#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor ClassFound = new(
        "HRA001",
        "Class found",
        "Class '{0}' found",
        "Test",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
#pragma warning restore RS2008

    // Unattributed and passed to the harness directly: the attribute would make this a shipped analyzer (RS1036).
#pragma warning disable RS1001
    private sealed class ClassAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1001
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [ClassFound];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(
                GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics
            );
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                ctx =>
                {
                    var declaration = (ClassDeclarationSyntax)ctx.Node;
                    ctx.ReportDiagnostic(
                        Diagnostic.Create(
                            ClassFound,
                            declaration.Identifier.GetLocation(),
                            declaration.Identifier.Text
                        )
                    );
                },
                SyntaxKind.ClassDeclaration
            );
        }
    }

    private static GeneratorHarnessResult RunWithClassAnalyzer(
        IIncrementalGenerator generator,
        GeneratorHarnessInput input
    ) => GeneratorHarness.Run(generator, input, [new ClassAnalyzer()]);

    [Test]
    public async Task Should_report_an_analyzer_warning_at_the_class_it_flags()
    {
        const string source = "class Input { }";
        var result = RunWithClassAnalyzer(
            NoOutput,
            new GeneratorHarnessInput { Sources = [source] }
        );

        await Assert.That(result.AnalyzerDiagnostics.Length).IsEqualTo(1);
        var diagnostic = result.AnalyzerDiagnostics[0];
        await Assert.That(diagnostic.Id).IsEqualTo("HRA001");
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo("Source0.cs");
        await Assert
            .That(diagnostic.Location.SourceSpan.Start)
            .IsEqualTo(source.IndexOf("Input", StringComparison.Ordinal));
        await Assert.That(diagnostic.Location.SourceSpan.Length).IsEqualTo("Input".Length);
    }

    [Test]
    public async Task Should_leave_out_a_warning_whose_severity_the_input_config_sets_to_none()
    {
        var result = RunWithClassAnalyzer(
            NoOutput,
            new GeneratorHarnessInput
            {
                Sources = ["class Input { }"],
                PerFileOptions = new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["Source0.cs"] = new Dictionary<string, string>
                    {
                        ["dotnet_diagnostic.HRA001.severity"] = "none",
                    },
                },
            }
        );

        await Assert.That(result.AnalyzerDiagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Should_leave_out_a_warning_a_pragma_disables()
    {
        var result = RunWithClassAnalyzer(
            NoOutput,
            new GeneratorHarnessInput
            {
                Sources =
                [
                    "#pragma warning disable HRA001\nclass Input { }\n#pragma warning restore HRA001",
                ],
            }
        );

        await Assert.That(result.AnalyzerDiagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Should_leave_out_a_warning_a_suppress_message_attribute_suppresses()
    {
        var result = RunWithClassAnalyzer(
            NoOutput,
            new GeneratorHarnessInput
            {
                Sources =
                [
                    "[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Test\", \"HRA001\")]\nclass Input { }",
                ],
            }
        );

        await Assert.That(result.AnalyzerDiagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Should_report_an_analyzer_warning_in_a_generated_source()
    {
        var generator = new DelegateGenerator(context =>
            context.RegisterPostInitializationOutput(ctx =>
                ctx.AddSource("Generated.g.cs", "class Generated { }")
            )
        );

        var result = RunWithClassAnalyzer(
            generator,
            new GeneratorHarnessInput { Sources = ["namespace Empty;"] }
        );

        await Assert.That(result.AnalyzerDiagnostics.Length).IsEqualTo(1);
        await Assert
            .That(result.AnalyzerDiagnostics[0].Location.GetLineSpan().Path)
            .EndsWith("Generated.g.cs");
    }

#pragma warning disable RS1001
    private sealed class ThrowingAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1001
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [ClassFound];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                _ => throw new InvalidOperationException("analyzer boom"),
                SyntaxKind.ClassDeclaration
            );
        }
    }

    [Test]
    public async Task Should_fail_the_run_when_an_analyzer_throws()
    {
        var exception = await Assert
            .That(() =>
                GeneratorHarness.Run(
                    NoOutput,
                    new GeneratorHarnessInput { Sources = ["class Input { }"] },
                    [new ThrowingAnalyzer()]
                )
            )
            .Throws<GeneratorTestException>();

        await Assert.That(exception!.Message).Contains("analyzer boom");
    }

    [Test]
    public async Task Should_report_no_analyzer_diagnostics_when_run_without_analyzers()
    {
        var result = GeneratorHarness.Run(NoOutput, "class Input { }");

        await Assert.That(result.AnalyzerDiagnostics.IsDefault).IsFalse();
        await Assert.That(result.AnalyzerDiagnostics.Length).IsEqualTo(0);
    }
}
