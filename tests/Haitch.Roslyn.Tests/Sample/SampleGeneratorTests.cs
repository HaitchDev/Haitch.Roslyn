using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Testing;

namespace Haitch.Roslyn.Tests.Sample;

public class SampleGeneratorTests
{
    private const string TwoParts = """
                                    using Sample;

                                    namespace App;

                                    [Sample]
                                    public partial class Widget { }
                                    """;

    private const string SecondPart = """
                                      namespace App;

                                      public partial class Widget { public int Size; }
                                      """;

    private const string NotPartial = """
                                      using Sample;

                                      namespace App;

                                      [Sample]
                                      public class Broken { }
                                      """;

    private static readonly TypeModel WidgetModel = new(
        "App",
        "Widget",
        TypeDeclarationKind.Class,
        Microsoft.CodeAnalysis.Accessibility.Public,
        false,
        false,
        false,
        false,
        false,
        false,
        default,
        default,
        default,
        default,
        default,
        default);

    [Test]
    public async Task Should_generate_one_file_for_a_two_part_partial_type()
    {
        var result = GeneratorHarness.Run(new SampleGenerator(), [TwoParts, SecondPart]);

        var hintName = HintName.For(WidgetModel, "Sample");

        await Assert.That(result.Sources.Keys).Contains(hintName);
        await Assert.That(result.Sources.Keys.Count(key => key.Contains("Widget"))).IsEqualTo(1);
        await Assert.That(result.Sources[hintName]).Contains("public const string TypeName = \"Widget\";");
        await Assert.That(result.Sources[hintName]).Contains("public override string ToString()");
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task Should_report_a_diagnostic_and_no_file_for_a_non_partial_type()
    {
        var result = GeneratorHarness.Run(new SampleGenerator(), [NotPartial]);

        await Assert.That(result.Diagnostics.Select(d => d.Id)).IsEquivalentTo([SampleGenerator.NotPartial.Id]);
        await Assert.That(result.Sources.Keys.Any(key => key.Contains("Broken"))).IsFalse();

        var position = result.Diagnostics.Single().Location.GetLineSpan().StartLinePosition;
        await Assert.That(position.Line).IsEqualTo(5);
        await Assert.That(position.Character).IsEqualTo("public class ".Length);
    }

    [Test]
    public async Task Should_report_a_diagnostic_and_no_file_for_a_static_partial_type()
    {
        var result = GeneratorHarness.Run(
            new SampleGenerator(),
            ["[Sample.Sample] static partial class Helpers { }"]);

        await Assert.That(result.Diagnostics.Select(d => d.Id)).IsEquivalentTo([SampleGenerator.StaticType.Id]);
        await Assert.That(result.Sources.Keys.Any(key => key.Contains("Helpers"))).IsFalse();
    }

    [Test]
    public async Task Should_generate_for_a_generic_type_nested_in_a_global_namespace_partial_type()
    {
        var result = GeneratorHarness.Run(
            new SampleGenerator(),
            ["partial class Outer { [Sample.Sample] public partial class Inner<T> { } }"]);

        await Assert.That(result.Sources.Keys).Contains("Outer+Inner`1.Sample.g.cs");
        await Assert.That(result.Sources.Keys.Count(key => key.Contains("Outer"))).IsEqualTo(1);
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public void Should_be_cacheable_on_the_diagnostics_path()
    {
        GeneratorHarness.AssertCacheable(
            new SampleGenerator(),
            [NotPartial],
            SampleGenerator.ModelStepName,
            SampleGenerator.ValidatedStepName,
            $"{SampleGenerator.ValidatedStepName}.Diagnostics");
    }

    [Test]
    public void Should_be_cacheable_across_unchanged_and_trivia_edits()
    {
        GeneratorHarness.AssertCacheable(
            new SampleGenerator(),
            [TwoParts, SecondPart],
            SampleGenerator.ModelStepName,
            SampleGenerator.ValidatedStepName);
    }
}
