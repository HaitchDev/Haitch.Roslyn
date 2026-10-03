using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Testing;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Generators;

public class BuildPropertyTests
{
    private sealed class PropertyGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var single = context
                .AnalyzerConfigOptionsProvider.ForBuildProperty("RootNamespace")
                .WithTrackingName("Single");

            var many = context
                .AnalyzerConfigOptionsProvider.ForBuildProperties("RootNamespace", "Blank", "Other")
                .WithTrackingName("Many");

            context.RegisterSourceOutput(
                single.Combine(many),
                static (ctx, pair) =>
                {
                    var (one, props) = pair;
                    var found = props.TryGet("Other", out var other);
                    ctx.AddSource(
                        "Props.g.cs",
                        $"// single={one ?? "<null>"}\n// other={other ?? "<null>"}\n// found={found}\n// root={props["RootNamespace"] ?? "<null>"}\n// blank={props["Blank"] ?? "<null>"}\n// unknown={props["Nope"] ?? "<null>"}\nclass Echo {{ }}"
                    );
                }
            );
        }
    }

    private static readonly string[] Sources = ["class Input { }"];

    private static string Run(Dictionary<string, string>? options)
    {
        var result = GeneratorHarness.Run(
            new PropertyGenerator(),
            new GeneratorHarnessInput { Sources = Sources, GlobalOptions = options }
        );
        return result.Sources["Props.g.cs"];
    }

    [Test]
    public async Task A_set_property_is_read()
    {
        var output = Run(new() { ["build_property.RootNamespace"] = "My.Root" });

        await Assert.That(output).Contains("single=My.Root");
        await Assert.That(output).Contains("root=My.Root");
    }

    [Test]
    public async Task A_missing_property_is_null()
    {
        var output = Run(new() { ["build_property.Other"] = "x" });

        await Assert.That(output).Contains("single=<null>");
        await Assert.That(output).Contains("root=<null>");
        await Assert.That(output).Contains("unknown=<null>");
    }

    [Test]
    public async Task An_empty_or_whitespace_property_is_null()
    {
        var output = Run(
            new() { ["build_property.RootNamespace"] = "   ", ["build_property.Blank"] = "" }
        );

        await Assert.That(output).Contains("single=<null>");
        await Assert.That(output).Contains("blank=<null>");
    }

    [Test]
    public async Task Several_properties_are_read_at_once_with_TryGet()
    {
        var output = Run(
            new()
            {
                ["build_property.RootNamespace"] = "My.Root",
                ["build_property.Other"] = "other-value",
            }
        );

        await Assert.That(output).Contains("found=True");
        await Assert.That(output).Contains("other=other-value");
        await Assert.That(output).Contains("root=My.Root");
    }

    [Test]
    public async Task TryGet_is_false_for_blank_and_unknown_names()
    {
        var output = Run(new() { ["build_property.Other"] = " " });

        await Assert.That(output).Contains("found=False");
    }

    [Test]
    public async Task BuildProperties_with_equal_values_are_equal()
    {
        BuildProperty[] a = [new("A", "1"), new("B", null)];
        BuildProperty[] b = [new("A", "1"), new("B", null)];

        await Assert
            .That(new BuildProperties(a.ToEquatableArray()))
            .IsEqualTo(new BuildProperties(b.ToEquatableArray()));
    }

    [Test]
    public async Task Changing_an_unrelated_source_keeps_the_steps_cached()
    {
        var result = GeneratorHarness.AssertCacheable(
            new PropertyGenerator(),
            new GeneratorHarnessInput
            {
                Sources = Sources,
                GlobalOptions = new Dictionary<string, string>
                {
                    ["build_property.RootNamespace"] = "My.Root",
                },
            },
            ["Single", "Many"]
        );

        await Assert.That(result.Sources["Props.g.cs"]).Contains("single=My.Root");
        await Assert.That(result.Sources["Props.g.cs"]).Contains("root=My.Root");
    }

    [Test]
    public async Task A_value_with_surrounding_whitespace_is_trimmed()
    {
        var output = Run(
            new()
            {
                ["build_property.RootNamespace"] = " App ",
                ["build_property.Other"] = "\tx\r\n",
            }
        );

        await Assert.That(output).Contains("single=App\n");
        await Assert.That(output).Contains("root=App\n");
        await Assert.That(output).Contains("other=x\n");
    }
}
