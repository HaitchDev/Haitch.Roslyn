using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Testing;

public class UnreadableAdditionalTextTests
{
    // Test-only descriptor; RS2008 (release tracking) does not apply to a fixture that is never shipped.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor Unreadable = new(
        "UAT001",
        "Unreadable additional file",
        "Cannot read '{0}'",
        "Test",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
#pragma warning restore RS2008

    private sealed class FileGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var files = context.ForAdditionalFiles(
                static path => path.EndsWith(".json", StringComparison.Ordinal),
                Unreadable
            );

            context.RegisterSourceOutput(
                files.Collect(),
                static (ctx, results) =>
                {
                    var lines = new List<string>();
                    foreach (var result in results)
                    {
                        if (result.TryGetValue(out var file))
                        {
                            lines.Add($"// {file.Path}|{file.Content}");
                        }
                        else
                        {
                            foreach (var diagnostic in result.Diagnostics)
                            {
                                ctx.ReportDiagnostic(diagnostic.ToDiagnostic());
                            }
                        }
                    }

                    ctx.AddSource("Files.g.cs", string.Join("\n", lines));
                }
            );
        }
    }

    [Test]
    public async Task The_unreadable_text_has_its_path_and_no_text()
    {
        var text = new UnreadableAdditionalText("x.txt");

        await Assert.That(text.Path).IsEqualTo("x.txt");
        await Assert.That(text.GetText()).IsNull();
    }

    [Test]
    public async Task A_null_path_is_rejected()
    {
        await Assert
            .That(() => new UnreadableAdditionalText(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task An_unreadable_text_is_reported_while_a_readable_one_still_generates()
    {
        var result = GeneratorHarness.Run(
            new FileGenerator(),
            new GeneratorHarnessInput
            {
                Sources = ["class Input { }"],
                AdditionalTexts =
                [
                    new HarnessAdditionalText("good.json", "{ }"),
                    new UnreadableAdditionalText("broken.json"),
                ],
            }
        );

        result.AssertDiagnostic("UAT001", messageContains: "broken.json");
        await Assert.That(result.Sources["Files.g.cs"]).Contains("// good.json|{ }");
    }
}
