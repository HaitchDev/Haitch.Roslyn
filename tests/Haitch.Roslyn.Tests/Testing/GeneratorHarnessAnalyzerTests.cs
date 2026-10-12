using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Testing;

public class GeneratorHarnessAnalyzerTests
{
    private sealed class DelegateGenerator(Action<IncrementalGeneratorInitializationContext> init)
        : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) => init(context);
    }

    private static readonly DelegateGenerator NoOutput = new(_ => { });

    [Test]
    public async Task Should_report_no_analyzer_diagnostics_when_run_without_analyzers()
    {
        var result = GeneratorHarness.Run(NoOutput, "class Input { }");

        await Assert.That(result.AnalyzerDiagnostics.IsDefault).IsFalse();
        await Assert.That(result.AnalyzerDiagnostics.Length).IsEqualTo(0);
    }
}
