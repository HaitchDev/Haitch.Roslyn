using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Testing;

// Slice 23 demo: an additional file and a build property reach the generator, the input has a syntax
// error, and the test asserts one generator diagnostic and the output against an expected file.
public class HarnessDemoTests
{
    // Test-only descriptor; RS2008 (release tracking) does not apply to a fixture that is never shipped.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor Info = new(
        "DEMO001",
        "Greeting generated",
        "Generated greeting from '{0}'",
        "Demo",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );
#pragma warning restore RS2008

    private sealed class DemoGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var greeting = context
                .AdditionalTextsProvider.Where(static t => t.Path == "greeting.txt")
                .Select(static (t, ct) => (t.Path, Text: t.GetText(ct)?.ToString().Trim() ?? ""))
                .Collect();

            var root = context.AnalyzerConfigOptionsProvider.Select(
                static (options, _) =>
                {
                    options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var v);
                    return v ?? "Global";
                }
            );

            context.RegisterSourceOutput(
                greeting.Combine(root),
                static (spc, model) =>
                {
                    var (files, rootNamespace) = model;
                    foreach (var (path, text) in files)
                    {
                        spc.AddSource(
                            "HarnessDemo.g.cs",
                            $"// Generated for {rootNamespace}\nnamespace {rootNamespace};\n\npublic static class Greeting\n{{\n    public const string Text = \"{text}\";\n}}\n"
                        );
                        spc.ReportDiagnostic(Diagnostic.Create(Info, Location.None, path));
                    }
                }
            );
        }
    }

    [Test]
    [NotInParallel("HAITCH_ACCEPT")]
    public async Task Generator_reads_file_and_property_despite_input_syntax_error()
    {
        var result = GeneratorHarness.Run(
            new DemoGenerator(),
            new GeneratorHarnessInput
            {
                Sources = ["class Broken { void M( }"],
                AdditionalTexts = [new HarnessAdditionalText("greeting.txt", "hello from file\n")],
                GlobalOptions = new Dictionary<string, string>
                {
                    ["build_property.RootNamespace"] = "My.Demo",
                },
                AllowInputErrors = true,
            }
        );

        result.AssertDiagnostic(
            "DEMO001",
            DiagnosticSeverity.Info,
            messageContains: "greeting.txt"
        );
        result.AssertSourceFile("HarnessDemo.g.cs", "Expected/HarnessDemo.g.cs.txt");

        await Assert.That(result.Diagnostics.Length).IsEqualTo(1);
        await Assert.That(result.InputDiagnostics.Length).IsGreaterThan(0);
    }
}
