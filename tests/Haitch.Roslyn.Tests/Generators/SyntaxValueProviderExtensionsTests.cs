using System.Collections.Immutable;
using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Testing;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Generators;

public class SyntaxValueProviderExtensionsTests
{
    // AllowMultiple lets the fixture apply Mark more than once to the same declaration, which
    // several tests below rely on.
    private const string MarkAttributeSource = """
                                               namespace Sample;

                                               [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                                               public class MarkAttribute : System.Attribute { }
                                               """;

    [Test]
    public async Task Should_find_types_marked_with_the_attribute_and_ignore_unmarked_types()
    {
        const string source = """
                              [Sample.Mark]
                              public partial class Widget { }

                              public class Plain { }
                              """;

        GeneratorDriverRunResult result = RunGenerator(MarkAttributeSource, source);

        string generated = ConcatTrees(result);

        await Assert.That(generated).Contains("Widget");
        await Assert.That(generated).DoesNotContain("Plain");
    }

    [Test]
    public async Task Should_return_every_attribute_application_when_the_attribute_allows_multiple()
    {
        const string source = """
                              [Sample.Mark]
                              [Sample.Mark]
                              public partial class Widget { }
                              """;

        var results = CollectResults(MarkAttributeSource, source);

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].Attributes.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Should_return_one_item_with_every_attribute_when_a_type_is_marked_on_two_parts_in_two_files()
    {
        var results = CollectResultsFromFiles(
            MarkAttributeSource,
            "[Sample.Mark] public partial class Widget { }",
            "[Sample.Mark] public partial class Widget { }");

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].Type.Name).IsEqualTo("Widget");
        await Assert.That(results[0].Attributes.Count).IsEqualTo(2);
        await Assert.That(results[0].Syntax.Location!.FilePath).IsEqualTo("Source1.cs");
    }

    [Test]
    public async Task Should_carry_the_syntax_of_the_marked_part_when_only_one_of_two_parts_is_marked()
    {
        var results = CollectResultsFromFiles(
            MarkAttributeSource,
            "public partial class Widget { }",
            "[Sample.Mark] public partial class Widget { }");

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].Attributes.Count).IsEqualTo(1);
        await Assert.That(results[0].Syntax.Location!.FilePath).IsEqualTo("Source2.cs");
    }

    [Test]
    public async Task Should_move_the_item_to_the_next_marked_part_when_the_first_part_loses_the_attribute()
    {
        var results = CollectResultsFromFiles(
            MarkAttributeSource,
            "public partial class Widget { }",
            "[Sample.Mark] public partial class Widget { }",
            "[Sample.Mark] public partial class Widget { }");

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].Attributes.Count).IsEqualTo(2);
        await Assert.That(results[0].Syntax.Location!.FilePath).IsEqualTo("Source2.cs");
    }

    [Test]
    public async Task Should_report_the_value_step_as_cached_or_unchanged_after_an_unrelated_edit_in_another_file()
    {
        // The harness appends its trivia edit to the first source, so the marked type goes first.
        // It also covers the unchanged-clone rerun and the caching-hazard check.
        GeneratorHarnessResult harnessResult = GeneratorHarness.AssertCacheable(
            new AttributeProviderTestGenerator(),
            [
                """
                [Sample.Mark]
                public partial class Widget { }
                """,
                MarkAttributeSource,
                "public class Unrelated { }",
            ],
            AttributeProviderTestGenerator.ValuesStepName);

        GeneratorDriver driver = harnessResult.Driver;
        Compilation compilation = harnessResult.InputCompilation;

        // Unrelated edit lives in Source2.cs, not Source0.cs: SyntaxInfo carries a location that would
        // shift (and so change the cached value) if the edit were made above Widget in the same file.
        // The harness cannot edit a second file, so this rerun stays hand-rolled.
        SyntaxTree treeB = compilation.SyntaxTrees.Single(tree => tree.FilePath == "Source2.cs");
        SyntaxTree updatedTreeB = CSharpSyntaxTree.ParseText(
            "public class Unrelated { public int Value; }", (CSharpParseOptions)treeB.Options, treeB.FilePath);
        Compilation updatedCompilation = compilation.ReplaceSyntaxTree(treeB, updatedTreeB);

        driver = driver.RunGenerators(updatedCompilation);

        ImmutableArray<IncrementalStepRunReason> reasonsAfterUnrelatedEdit = GetValuesStepReasons(driver);

        await Assert.That(reasonsAfterUnrelatedEdit).IsNotEmpty();
        await Assert.That(reasonsAfterUnrelatedEdit.All(reason =>
            reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged)).IsTrue();

        // Trivia-only edit below the marked type, this time in Source0.cs itself: the identifier
        // location SyntaxInfo captures does not move, so this exercises TypeModel/SyntaxInfo
        // value equality directly. The tracked step sits behind the dedupe filter, which reports
        // unchanged upstream values as Cached, so Cached is as strong as this can be asserted;
        // Modified or New would still fail it.
        SyntaxTree treeA = updatedCompilation.SyntaxTrees.Single(tree => tree.FilePath == "Source0.cs");
        SyntaxTree updatedTreeA = CSharpSyntaxTree.ParseText(
            treeA.GetText().ToString() + "// trailing comment, no semantic effect\n",
            (CSharpParseOptions)treeA.Options, treeA.FilePath);
        Compilation updatedCompilation2 = updatedCompilation.ReplaceSyntaxTree(treeA, updatedTreeA);

        driver = driver.RunGenerators(updatedCompilation2);

        ImmutableArray<IncrementalStepRunReason> reasonsAfterTriviaEdit = GetValuesStepReasons(driver);

        await Assert.That(reasonsAfterTriviaEdit).IsNotEmpty();
        await Assert.That(reasonsAfterTriviaEdit.All(reason =>
            reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged)).IsTrue();
    }

    [Test]
    public void Should_stay_cacheable_when_a_type_is_marked_on_two_parts()
    {
        GeneratorHarness.AssertCacheable(
            new AttributeProviderTestGenerator(),
            [
                """
                [Sample.Mark]
                public partial class Widget { }
                """,
                MarkAttributeSource,
                "[Sample.Mark] public partial class Widget { }",
            ],
            AttributeProviderTestGenerator.ValuesStepName);
    }

    [Test]
    public async Task Should_return_one_item_with_both_applications_when_parts_use_different_generic_instantiations()
    {
        const string genericAttributeSource = """
                                              namespace Sample;

                                              [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                                              public class MarkAttribute<T> : System.Attribute { }
                                              """;

        var results = CollectResultsFromFiles(
            genericAttributeSource,
            "Sample.MarkAttribute`1",
            [
                "[Sample.Mark<int>] public partial class Widget { }",
                "[Sample.Mark<string>] public partial class Widget { }",
            ]);

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].Attributes.Count).IsEqualTo(2);
        await Assert.That(results[0].Syntax.Location!.FilePath).IsEqualTo("Source1.cs");
    }

    [Test]
    public async Task Should_move_the_item_between_parts_across_incremental_runs_on_the_same_driver()
    {
        const string part = "[Sample.Mark] public partial class Widget { }";

        CSharpCompilation compilation = CreateCompilation(
        [
            CSharpSyntaxTree.ParseText(MarkAttributeSource, path: "Source0.cs"),
            CSharpSyntaxTree.ParseText(part, path: "Source1.cs"),
            CSharpSyntaxTree.ParseText(part, path: "Source2.cs"),
        ]);

        var generator = new CollectingAttributeProviderTestGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver = driver.RunGenerators(compilation);

        await Assert.That(generator.Collected.Count).IsEqualTo(1);
        await Assert.That(generator.Collected[0].Syntax.Location!.FilePath).IsEqualTo("Source1.cs");

        SyntaxTree tree1 = compilation.SyntaxTrees.Single(tree => tree.FilePath == "Source1.cs");
        Compilation unmarked = compilation.ReplaceSyntaxTree(
            tree1, CSharpSyntaxTree.ParseText("public partial class Widget { }", path: "Source1.cs"));

        generator.Collected.Clear();
        driver = driver.RunGenerators(unmarked);

        await Assert.That(generator.Collected.Count).IsEqualTo(1);
        await Assert.That(generator.Collected[0].Syntax.Location!.FilePath).IsEqualTo("Source2.cs");
        await Assert.That(generator.Collected[0].Attributes.Count).IsEqualTo(1);

        SyntaxTree unmarkedTree1 = unmarked.SyntaxTrees.Single(tree => tree.FilePath == "Source1.cs");
        Compilation remarked = unmarked.ReplaceSyntaxTree(
            unmarkedTree1, CSharpSyntaxTree.ParseText(part, path: "Source1.cs"));

        generator.Collected.Clear();
        driver.RunGenerators(remarked);

        await Assert.That(generator.Collected.Count).IsEqualTo(1);
        await Assert.That(generator.Collected[0].Syntax.Location!.FilePath).IsEqualTo("Source1.cs");
        await Assert.That(generator.Collected[0].Attributes.Count).IsEqualTo(2);
    }

    private static ImmutableArray<IncrementalStepRunReason> GetValuesStepReasons(GeneratorDriver driver)
    {
        GeneratorDriverRunResult result = driver.GetRunResult();
        ImmutableArray<IncrementalGeneratorRunStep> steps =
            result.Results[0].TrackedSteps[AttributeProviderTestGenerator.ValuesStepName];

        return steps
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToImmutableArray();
    }

    private static ImmutableArray<(TypeModel Type, SyntaxInfo Syntax, EquatableArray<AttributeModel> Attributes)>
        CollectResultsFromFiles(string attributeSource, params string[] sources)
    {
        return CollectResultsFromFiles(attributeSource, "Sample.MarkAttribute", sources);
    }

    private static ImmutableArray<(TypeModel Type, SyntaxInfo Syntax, EquatableArray<AttributeModel> Attributes)>
        CollectResultsFromFiles(string attributeSource, string metadataName, string[] sources)
    {
        CSharpCompilation compilation = CreateCompilation(
            new[] { CSharpSyntaxTree.ParseText(attributeSource, path: "Source0.cs") }
                .Concat(sources.Select((source, index) =>
                    CSharpSyntaxTree.ParseText(source, path: $"Source{index + 1}.cs"))));

        var generator = new CollectingAttributeProviderTestGenerator(metadataName);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver.RunGenerators(compilation);

        return generator.Collected.ToImmutableArray();
    }

    private static ImmutableArray<(TypeModel Type, SyntaxInfo Syntax, EquatableArray<AttributeModel> Attributes)>
        CollectResults(string attributeSource, string source)
    {
        CSharpCompilation compilation = CreateCompilation(
        [
            CSharpSyntaxTree.ParseText(attributeSource),
            CSharpSyntaxTree.ParseText(source),
        ]);

        var generator = new CollectingAttributeProviderTestGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver.RunGenerators(compilation);

        return generator.Collected.ToImmutableArray();
    }

    private static GeneratorDriverRunResult RunGenerator(string attributeSource, string source)
    {
        CSharpCompilation compilation = CreateCompilation(
        [
            CSharpSyntaxTree.ParseText(attributeSource),
            CSharpSyntaxTree.ParseText(source),
        ]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new AttributeProviderTestGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);

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

    private static string ConcatTrees(GeneratorDriverRunResult result)
    {
        return string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));
    }

    // Fixture generator exercising SyntaxValueProviderExtensions: emits one source file per type
    // marked with Sample.MarkAttribute, named after the type.
    private sealed class AttributeProviderTestGenerator : IIncrementalGenerator
    {
        public const string ValuesStepName = "AttributeProviderTestGenerator.Values";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<(TypeModel Type, SyntaxInfo Syntax, EquatableArray<AttributeModel> Attributes)>
                results = context.SyntaxProvider.ForTypesWithAttribute("Sample.MarkAttribute", ValuesStepName);

            context.RegisterSourceOutput(results,
                static (spc, value) =>
                {
                    spc.AddSource($"{value.Type.Name}.g.cs", $"// generated for {value.Type.Name}\n");
                });
        }
    }

    // Fixture generator that collects every provider output directly, instead of routing it
    // through AddSource: several tests need the raw item count (e.g. more than one item for the
    // same type), which AddSource cannot express since it requires unique hint names.
    private sealed class CollectingAttributeProviderTestGenerator : IIncrementalGenerator
    {
        private readonly string _metadataName;

        public CollectingAttributeProviderTestGenerator(string metadataName = "Sample.MarkAttribute")
        {
            _metadataName = metadataName;
        }

        public List<(TypeModel Type, SyntaxInfo Syntax, EquatableArray<AttributeModel> Attributes)> Collected { get; } =
            [];

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<(TypeModel Type, SyntaxInfo Syntax, EquatableArray<AttributeModel> Attributes)>
                results = context.SyntaxProvider.ForTypesWithAttribute(_metadataName, "Values");

            context.RegisterSourceOutput(results, (_, value) => Collected.Add(value));
        }
    }
}
