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
    public async Task Should_suppress_a_pipeline_diagnostic_under_a_pragma_disable()
    {
        GeneratorDriverRunResult result = RunGenerator(
            "#pragma warning disable TEST001\nclass BadWidget { }\n#pragma warning restore TEST001"
        );

        await Assert.That(result.Diagnostics.Length).IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Id).IsEqualTo("TEST001");
        await Assert.That(result.Diagnostics[0].IsSuppressed).IsTrue();
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
    public void Should_report_the_value_step_as_unchanged_for_an_entry_with_a_trivia_only_edit()
    {
        // The trivia edit changes Widget's syntax but not its value, so the step must re-run and report
        // Unchanged; OtherWidget (another tree) stays Cached, which the strict check accepts alongside it.
        GeneratorHarness.AssertCacheable(
            new TestGenerator(),
            new GeneratorHarnessInput { Sources = ["class Widget { }", "class OtherWidget { }"] },
            [TestGenerator.ValuesStepName],
            options: new CacheabilityOptions { RequireRecomputationAfterTriviaEdit = true }
        );
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
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }
}
