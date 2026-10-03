using Haitch.Roslyn.Diagnostics;
using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haitch.Roslyn.Tests.Testing;

public class CacheabilityOptionsProviderTests
{
    private static readonly string[] Sources = ["class Input { }"];

    private static readonly Dictionary<string, string> Global = new()
    {
        ["build_property.RootNamespace"] = "My.Root",
    };

    private sealed class HoldingGenerator : IIncrementalGenerator
    {
        public List<AnalyzerConfigOptionsProvider> Seen { get; } = [];

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var held = context
                .AnalyzerConfigOptionsProvider.Select(
                    (p, _) =>
                    {
                        Seen.Add(p);
                        return p;
                    }
                )
                .WithTrackingName("Held");

            context.RegisterSourceOutput(held, static (_, _) => { });
        }
    }

    private sealed class BuildPropertyGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var value = context
                .AnalyzerConfigOptionsProvider.ForBuildProperty("RootNamespace")
                .WithTrackingName("Value");

            context.RegisterSourceOutput(value, static (_, _) => { });
        }
    }

    // Test-only descriptor; RS2008 (release tracking) does not apply to a fixture that is never shipped.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor Unreadable = new(
        "CPT001",
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
            var files = context
                .ForAdditionalFiles(static _ => true, Unreadable, "Kind")
                .WithTrackingName("Files");

            context.RegisterSourceOutput(
                files,
                static (spc, result) =>
                {
                    if (result.TryGetValue(out var model))
                    {
                        model.TryGetMetadata("Kind", out var kind);
                        spc.AddSource("Kind.g.cs", $"// kind:{kind}");
                    }
                }
            );
        }
    }

    private sealed class ProviderDerivedGenerator : IIncrementalGenerator
    {
        public List<AnalyzerConfigOptionsProvider> Seen { get; } = [];

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var derived = context
                .AnalyzerConfigOptionsProvider.Select(
                    (p, _) =>
                    {
                        Seen.Add(p);
                        p.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value);
                        return value ?? "";
                    }
                )
                .WithTrackingName("Derived");

            context.RegisterSourceOutput(derived, static (_, _) => { });
        }
    }

    [Test]
    public async Task A_generator_holding_the_options_provider_fails_the_assertion()
    {
        var ex = await Assert
            .That(() =>
                GeneratorHarness.AssertCacheable(
                    new HoldingGenerator(),
                    new GeneratorHarnessInput { Sources = Sources, GlobalOptions = Global },
                    ["Held"]
                )
            )
            .Throws<GeneratorTestException>();

        // The hazard walker also rejects this provider after the reruns; the rerun itself must fail first.
        await Assert.That(ex!.Message).Contains("Step 'Held' run 1 output 0 was Modified");
    }

    [Test]
    public async Task A_build_property_generator_stays_cached_with_global_options()
    {
        GeneratorHarness.AssertCacheable(
            new BuildPropertyGenerator(),
            new GeneratorHarnessInput { Sources = Sources, GlobalOptions = Global },
            ["Value"]
        );

        await Task.CompletedTask;
    }

    [Test]
    public async Task An_additional_file_generator_stays_cached_with_global_options()
    {
        var result = GeneratorHarness.AssertCacheable(
            new FileGenerator(),
            new GeneratorHarnessInput
            {
                Sources = Sources,
                GlobalOptions = Global,
                AdditionalTexts = [new HarnessAdditionalText("a.json", "{}")],
                PerFileOptions = new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["a.json"] = new Dictionary<string, string>
                    {
                        ["build_metadata.AdditionalFiles.Kind"] = "x",
                    },
                },
            },
            ["Files"]
        );

        var generated = result.RunResult.Results[0].GeneratedSources.Single().SourceText.ToString();
        await Assert.That(generated).Contains("kind:x");
    }

    [Test]
    public async Task Every_rerun_path_swaps_the_provider()
    {
        var generator = new ProviderDerivedGenerator();

        GeneratorHarness.AssertCacheable(
            generator,
            new GeneratorHarnessInput
            {
                Sources = ["class Input { }", "class Other { }"],
                GlobalOptions = Global,
            },
            ["Derived"],
            new CacheabilityOptions { UnrelatedEditSourceIndex = 1 }
        );

        // First run, clone, trivia edit, unrelated edit.
        var distinct = generator.Seen.Distinct(ReferenceEqualityComparer.Instance).Count();
        await Assert.That(generator.Seen.Count).IsEqualTo(4);
        await Assert.That(distinct).IsEqualTo(4);
    }

    [Test]
    public async Task Each_rerun_sees_a_new_provider_instance()
    {
        var generator = new HoldingGenerator();

        try
        {
            GeneratorHarness.AssertCacheable(
                generator,
                new GeneratorHarnessInput { Sources = Sources, GlobalOptions = Global },
                ["Held"]
            );
        }
        catch (GeneratorTestException)
        {
            // The held provider is expected to fail the assertion; the captured instances are what this test inspects.
        }

        await Assert.That(generator.Seen.Count).IsGreaterThanOrEqualTo(2);
        await Assert
            .That(generator.Seen.Skip(1).Any(p => ReferenceEquals(p, generator.Seen[0])))
            .IsFalse();
    }
}
