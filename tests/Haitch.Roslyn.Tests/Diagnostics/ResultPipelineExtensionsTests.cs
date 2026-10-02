using System.Collections.Immutable;
using Haitch.Roslyn.Diagnostics;
using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Diagnostics;

public class ResultPipelineExtensionsTests
{
    [Test]
    public async Task Should_report_the_expected_diagnostic_for_a_failing_input()
    {
        GeneratorDriverRunResult result = RunGenerator("class BadWidget { }");

        await Assert.That(result.Diagnostics.Length).IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Id).IsEqualTo("TEST001");
        await Assert.That(result.Diagnostics[0].GetMessage()).Contains("BadWidget");
    }

    [Test]
    public async Task Should_generate_output_only_for_successful_inputs()
    {
        GeneratorDriverRunResult result = RunGenerator("class Widget { } class BadWidget { }");

        await Assert.That(result.GeneratedTrees.Length).IsEqualTo(1);
        string generated = result.GeneratedTrees[0].ToString();
        await Assert.That(generated).Contains("Widget");
        await Assert.That(generated).DoesNotContain("BadWidget");
    }

    [Test]
    public async Task Should_report_the_value_step_as_unchanged_for_an_entry_with_a_trivia_only_edit()
    {
        // The harness appends a trailing comment to the first source (Widget) and requires the
        // value step to stay cached or unchanged, plus the caching-hazard check.
        GeneratorHarnessResult harnessResult = GeneratorHarness.AssertCacheable(
            new TestGenerator(),
            ["class Widget { }", "class OtherWidget { }"],
            TestGenerator.ValuesStepName);

        GeneratorDriver driver = harnessResult.Driver;
        Compilation compilation = harnessResult.InputCompilation;

        // Trivia-only edit to Widget itself: the syntax node changes (new trailing comment) but the
        // transformed value ("Widget") stays equal, so the value step must report Unchanged. Kept
        // hand-rolled because the harness accepts Cached as well.
        SyntaxTree treeA = compilation.SyntaxTrees.Single(tree => tree.FilePath == "Source0.cs");
        SyntaxTree updatedTreeA = CSharpSyntaxTree.ParseText(
            "class Widget { } // comment", (CSharpParseOptions)treeA.Options, treeA.FilePath);
        Compilation updatedCompilation = compilation.ReplaceSyntaxTree(treeA, updatedTreeA);

        driver = driver.RunGenerators(updatedCompilation);

        GeneratorDriverRunResult result = driver.GetRunResult();
        ImmutableArray<IncrementalGeneratorRunStep>
            steps = result.Results[0].TrackedSteps[TestGenerator.ValuesStepName];

        var widgetOutputs = steps
            .SelectMany(step => step.Outputs)
            .Where(output => ((Result<string>)output.Value).TryGetValue(out string? value) && value == "Widget")
            .ToImmutableArray();

        await Assert.That(widgetOutputs.Length).IsGreaterThan(0);

        foreach (var output in widgetOutputs)
        {
            await Assert.That(output.Reason).IsEqualTo(IncrementalStepRunReason.Unchanged);
        }
    }

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        CSharpCompilation compilation = CreateCompilation([CSharpSyntaxTree.ParseText(source)]);
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(new TestGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        return driver.GetRunResult();
    }

    private static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees)
    {
        return CSharpCompilation.Create(
            "Tests",
            trees,
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
