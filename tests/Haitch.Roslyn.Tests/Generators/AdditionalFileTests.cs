using Haitch.Roslyn.Diagnostics;
using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Testing;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Generators;

public class AdditionalFileTests
{
    // Test-only descriptor; RS2008 (release tracking) does not apply to a fixture that is never shipped.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor Unreadable = new(
        "ADF001",
        "Unreadable additional file",
        "Cannot read '{0}'",
        "Test",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    private sealed class FileGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var files = context
                .ForAdditionalFiles(
                    static path => path.EndsWith(".json", StringComparison.Ordinal),
                    Unreadable,
                    "Kind",
                    "Empty",
                    "Missing"
                )
                .WithTrackingName("Files");

            context.RegisterSourceOutput(
                files.Collect(),
                static (ctx, results) =>
                {
                    var lines = new List<string>();
                    foreach (var result in results)
                    {
                        if (result.TryGetValue(out var file))
                        {
                            file.TryGetMetadata("Kind", out var kind);
                            file.TryGetMetadata("kind", out var kindLower);
                            lines.Add(
                                $"// {file.Path}|{file.Content}|kind={kind ?? "<null>"}|kindLower={kindLower ?? "<null>"}|empty={file["Empty"] ?? "<null>"}|missing={file["Missing"] ?? "<null>"}"
                            );
                        }
                        else
                        {
                            foreach (var diagnostic in result.Diagnostics)
                            {
                                ctx.ReportDiagnostic(diagnostic.ToDiagnostic());
                            }
                        }
                    }

                    ctx.AddSource("Files.g.cs", string.Join("\n", lines) + "\nclass Echo { }");
                }
            );
        }
    }

    private static readonly string[] Sources = ["class Input { }", "class Other { }"];

    private static GeneratorHarnessInput Input(
        IReadOnlyList<HarnessAdditionalText> texts,
        Dictionary<string, IReadOnlyDictionary<string, string>>? perFile = null
    ) =>
        new()
        {
            Sources = Sources,
            AdditionalTexts = texts,
            PerFileOptions = perFile,
        };

    [Test]
    public async Task Files_are_filtered_by_the_path_predicate()
    {
        var result = GeneratorHarness.Run(
            new FileGenerator(),
            Input([new("a.json", "A"), new("b.txt", "B"), new("c.json", "C")])
        );

        var output = result.Sources["Files.g.cs"];
        await Assert.That(output).Contains("// a.json|A|");
        await Assert.That(output).Contains("// c.json|C|");
        await Assert.That(output).DoesNotContain("b.txt");
    }

    [Test]
    public async Task Path_and_content_round_trip()
    {
        var result = GeneratorHarness.Run(
            new FileGenerator(),
            Input([new("dir/a b.json", "{ \"x\": 1 }")])
        );

        await Assert.That(result.Sources["Files.g.cs"]).Contains("// dir/a b.json|{ \"x\": 1 }|");
    }

    [Test]
    public async Task Requested_metadata_is_present_and_missing_or_empty_is_null()
    {
        var result = GeneratorHarness.Run(
            new FileGenerator(),
            Input(
                [new("a.json", "A")],
                new()
                {
                    ["a.json"] = new Dictionary<string, string>
                    {
                        ["build_metadata.AdditionalFiles.Kind"] = "Config",
                        ["build_metadata.AdditionalFiles.Empty"] = "",
                        ["build_metadata.AdditionalFiles.NotRequested"] = "x",
                    },
                }
            )
        );

        var output = result.Sources["Files.g.cs"];
        await Assert.That(output).Contains("kind=Config|");
        await Assert.That(output).Contains("empty=<null>|");
        await Assert.That(output).Contains("missing=<null>");
    }

    [Test]
    public async Task Metadata_names_compare_case_insensitively()
    {
        var result = GeneratorHarness.Run(
            new FileGenerator(),
            Input(
                [new("a.json", "A")],
                new()
                {
                    ["a.json"] = new Dictionary<string, string>
                    {
                        ["build_metadata.AdditionalFiles.Kind"] = "Config",
                    },
                }
            )
        );

        await Assert.That(result.Sources["Files.g.cs"]).Contains("kindLower=Config|");
    }

    [Test]
    public async Task Metadata_belongs_to_the_file_it_was_set_on()
    {
        var result = GeneratorHarness.Run(
            new FileGenerator(),
            Input(
                [new("a.json", "A"), new("b.json", "B")],
                new()
                {
                    ["a.json"] = new Dictionary<string, string>
                    {
                        ["build_metadata.AdditionalFiles.Kind"] = "OnlyA",
                    },
                }
            )
        );

        var output = result.Sources["Files.g.cs"];
        await Assert.That(output).Contains("// a.json|A|kind=OnlyA|");
        await Assert.That(output).Contains("// b.json|B|kind=<null>|");
    }

    [Test]
    public async Task An_unreadable_text_fails_with_the_callers_descriptor_and_the_path()
    {
        var result = GeneratorHarness.Run(
            new FileGenerator(),
            new GeneratorHarnessInput
            {
                Sources = Sources,
                AdditionalTexts = [new UnreadableAdditionalText("broken.json")],
            }
        );

        await Assert.That(result.Diagnostics).HasSingleItem();
        result.AssertDiagnostic("ADF001", messageContains: "broken.json", file: "broken.json");
    }

    [Test]
    public void A_filtered_out_text_is_never_read()
    {
        var result = GeneratorHarness.Run(
            new FileGenerator(),
            new GeneratorHarnessInput
            {
                Sources = Sources,
                AdditionalTexts = [new UnreadableAdditionalText("broken.txt")],
            }
        );

        result.AssertNoDiagnostics();
    }

    [Test]
    public async Task Models_with_equal_values_are_equal()
    {
        BuildProperty[] a = [new("Kind", "x")];
        BuildProperty[] b = [new("Kind", "x")];

        await Assert
            .That(new AdditionalFileModel("p", "c", a.ToEquatableArray()))
            .IsEqualTo(new AdditionalFileModel("p", "c", b.ToEquatableArray()));
    }

    [Test]
    public async Task Changing_an_unrelated_source_keeps_the_step_cached()
    {
        var result = GeneratorHarness.AssertCacheable(
            new FileGenerator(),
            Input(
                [new("a.json", "A"), new("b.json", "B"), new("c.txt", "C")],
                new()
                {
                    ["a.json"] = new Dictionary<string, string>
                    {
                        ["build_metadata.AdditionalFiles.Kind"] = "Config",
                    },
                }
            ),
            ["Files"],
            new CacheabilityOptions { UnrelatedEditSourceIndex = 1 }
        );

        await Assert.That(result.Sources["Files.g.cs"]).Contains("// a.json|A|kind=Config|");
        await Assert.That(result.Sources["Files.g.cs"]).Contains("// b.json|B|kind=<null>|");
    }

    [Test]
    public async Task Metadata_with_surrounding_whitespace_is_trimmed()
    {
        var result = GeneratorHarness.Run(
            new FileGenerator(),
            Input(
                [new("a.json", "A")],
                new()
                {
                    ["a.json"] = new Dictionary<string, string>
                    {
                        ["build_metadata.AdditionalFiles.Kind"] = " Config ",
                    },
                }
            )
        );

        await Assert.That(result.Sources["Files.g.cs"]).Contains("kind=Config|");
    }
}
