using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haitch.Roslyn.Tests.Testing;

public class GeneratorHarnessOptionsTests
{
    private sealed class OptionsGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var texts = context
                .AdditionalTextsProvider.Combine(context.AnalyzerConfigOptionsProvider)
                .Select(
                    static (pair, ct) =>
                    {
                        var (text, options) = pair;
                        var perFile = options.GetOptions(text);
                        perFile.TryGetValue("build_metadata.AdditionalFiles.Kind", out var kind);
                        return $"{text.Path}|{text.GetText(ct)?.ToString()}|{kind}";
                    }
                )
                .WithTrackingName("Texts");

            var root = context
                .AnalyzerConfigOptionsProvider.Select(
                    static (options, _) =>
                    {
                        options.GlobalOptions.TryGetValue(
                            "build_property.RootNamespace",
                            out var v
                        );
                        return v ?? "<none>";
                    }
                )
                .WithTrackingName("Root");

            var tree = context
                .AnalyzerConfigOptionsProvider.Select(static (options, _) => options)
                .Combine(context.CompilationProvider)
                .Select(
                    static (pair, _) =>
                    {
                        var (options, compilation) = pair;
                        var t = compilation.SyntaxTrees.First();
                        options
                            .GetOptions(t)
                            .TryGetValue("dotnet_diagnostic.X.severity", out var v);
                        return v ?? "<none>";
                    }
                );

            context.RegisterSourceOutput(
                texts.Collect().Combine(root).Combine(tree),
                (ctx, model) =>
                {
                    var ((all, rootNamespace), treeOption) = model;
                    ctx.AddSource(
                        "Options.g.cs",
                        $"// {string.Join(";", all)}\n// root={rootNamespace}\n// tree={treeOption}\nclass Echo {{ }}"
                    );
                }
            );
        }
    }

    private static readonly string[] Sources = ["class Input { }"];

    [Test]
    public async Task Run_passes_additional_texts_global_and_per_file_options()
    {
        var result = GeneratorHarness.Run(
            new OptionsGenerator(),
            new GeneratorHarnessInput
            {
                Sources = Sources,
                AdditionalTexts =
                [
                    new HarnessAdditionalText("a.json", "{ \"a\": 1 }"),
                    new HarnessAdditionalText("b.txt", "bee"),
                ],
                GlobalOptions = new Dictionary<string, string>
                {
                    ["build_property.RootNamespace"] = "My.Root",
                },
                PerFileOptions = new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["a.json"] = new Dictionary<string, string>
                    {
                        ["build_metadata.AdditionalFiles.Kind"] = "Config",
                    },
                    ["Source0.cs"] = new Dictionary<string, string>
                    {
                        ["dotnet_diagnostic.X.severity"] = "warning",
                    },
                },
            }
        );

        var output = result.Sources["Options.g.cs"];
        await Assert.That(output).Contains("a.json|{ \"a\": 1 }|Config");
        await Assert.That(output).Contains("b.txt|bee|");
        await Assert.That(output).DoesNotContain("b.txt|bee|Config");
        await Assert.That(output).Contains("root=My.Root");
        await Assert.That(output).Contains("tree=warning");
    }

    [Test]
    public async Task Option_lookups_are_case_insensitive()
    {
        var result = GeneratorHarness.Run(
            new OptionsGenerator(),
            new GeneratorHarnessInput
            {
                Sources = Sources,
                GlobalOptions = new Dictionary<string, string>
                {
                    ["build_property.rootnamespace"] = "Lower",
                },
            }
        );

        await Assert.That(result.Sources["Options.g.cs"]).Contains("root=Lower");
    }

    [Test]
    public async Task Without_options_the_generator_sees_nothing()
    {
        var result = GeneratorHarness.Run(
            new OptionsGenerator(),
            new GeneratorHarnessInput { Sources = Sources }
        );

        await Assert.That(result.Sources["Options.g.cs"]).Contains("root=<none>");
        await Assert.That(result.Sources["Options.g.cs"]).Contains("tree=<none>");
    }

    [Test]
    public async Task AssertCacheable_with_texts_and_options_passes()
    {
        var result = GeneratorHarness.AssertCacheable(
            new OptionsGenerator(),
            new GeneratorHarnessInput
            {
                Sources = Sources,
                AdditionalTexts = [new HarnessAdditionalText("a.json", "{}")],
                GlobalOptions = new Dictionary<string, string>
                {
                    ["build_property.RootNamespace"] = "My.Root",
                },
                PerFileOptions = new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["a.json"] = new Dictionary<string, string>
                    {
                        ["build_metadata.AdditionalFiles.Kind"] = "Config",
                    },
                },
            },
            ["Texts", "Root"]
        );

        await Assert.That(result.Sources["Options.g.cs"]).Contains("a.json|{}|Config");
    }
}
