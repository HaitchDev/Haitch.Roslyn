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
    public void Should_report_the_value_step_as_cached_or_unchanged_after_an_unrelated_edit_in_another_file()
    {
        // The harness appends its trivia edit to the first source, so the marked type goes first.
        // The unrelated edit goes to Source2.cs, not Source0.cs: SyntaxInfo carries a location that would
        // shift (and so change the cached value) if the edit were made above Widget in the same file.
        GeneratorHarness.AssertCacheable(
            new AttributeProviderTestGenerator(),
            [
                """
                [Sample.Mark]
                public partial class Widget { }
                """,
                MarkAttributeSource,
                "public class Unrelated { }",
            ],
            [AttributeProviderTestGenerator.ValuesStepName],
            options: new CacheabilityOptions { UnrelatedEditSourceIndex = 2 });
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
    public async Task Should_modify_the_step_when_a_member_of_the_second_part_changes_but_not_for_trivia()
    {
        CSharpCompilation compilation = CreateCompilation(
        [
            CSharpSyntaxTree.ParseText(MarkAttributeSource, path: "Source0.cs"),
            CSharpSyntaxTree.ParseText("[Sample.Mark] public partial class Widget { }", path: "Source1.cs"),
            CSharpSyntaxTree.ParseText("public partial class Widget { public int First; }", path: "Source2.cs"),
        ]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new AttributeProviderTestGenerator(includeMembers: true).AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        SyntaxTree tree2 = compilation.SyntaxTrees.Single(tree => tree.FilePath == "Source2.cs");
        Compilation trivia = compilation.ReplaceSyntaxTree(
            tree2,
            CSharpSyntaxTree.ParseText("public partial class Widget { public int First; } // note",
                path: "Source2.cs"));
        driver = driver.RunGenerators(trivia);

        await Assert.That(GetValuesStepReasons(driver)).IsNotEmpty();
        await Assert.That(GetValuesStepReasons(driver).All(reason =>
            reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged)).IsTrue();

        SyntaxTree triviaTree2 = trivia.SyntaxTrees.Single(tree => tree.FilePath == "Source2.cs");
        Compilation edited = trivia.ReplaceSyntaxTree(
            triviaTree2,
            CSharpSyntaxTree.ParseText("public partial class Widget { public int Renamed; }", path: "Source2.cs"));
        driver = driver.RunGenerators(edited);

        await Assert.That(GetValuesStepReasons(driver).Any(reason => reason == IncrementalStepRunReason.Modified))
            .IsTrue();
    }

    [Test]
    public async Task Should_skip_indexers_when_members_are_included()
    {
        var results = CollectResultsFromFiles(
            MarkAttributeSource,
            "Sample.MarkAttribute",
            [
                "[Sample.Mark] public partial class Widget { public int this[int i] => i; public int Count { get; set; } }"
            ],
            includeMembers: true);

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].Type.Properties.AsSpan().ToArray().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "Count" });
    }

    [Test]
    public async Task Should_return_one_item_with_members_of_both_parts_when_both_are_marked()
    {
        var results = CollectResultsFromFiles(
            MarkAttributeSource,
            "Sample.MarkAttribute",
            [
                "[Sample.Mark] public partial class Widget { public int First; }",
                "[Sample.Mark] public partial class Widget { public int Second; }",
            ],
            includeMembers: true);

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].Type.Fields.AsSpan().ToArray().Select(field => field.Name).Order().ToArray())
            .IsEquivalentTo(new[] { "First", "Second" });
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
        CollectResultsFromFiles(
            string attributeSource,
            string metadataName,
            string[] sources,
            bool includeMembers = false)
    {
        CSharpCompilation compilation = CreateCompilation(
            new[] { CSharpSyntaxTree.ParseText(attributeSource, path: "Source0.cs") }
                .Concat(sources.Select((source, index) =>
                    CSharpSyntaxTree.ParseText(source, path: $"Source{index + 1}.cs"))));

        var generator = new CollectingAttributeProviderTestGenerator(metadataName, includeMembers);
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

        private readonly bool _includeMembers;

        public AttributeProviderTestGenerator(bool includeMembers = false)
        {
            _includeMembers = includeMembers;
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<(TypeModel Type, SyntaxInfo Syntax, EquatableArray<AttributeModel> Attributes)>
                results = context.SyntaxProvider.ForTypesWithAttribute("Sample.MarkAttribute", ValuesStepName,
                    _includeMembers);

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
        private readonly bool _includeMembers;

        public CollectingAttributeProviderTestGenerator(
            string metadataName = "Sample.MarkAttribute",
            bool includeMembers = false)
        {
            _metadataName = metadataName;
            _includeMembers = includeMembers;
        }

        public List<(TypeModel Type, SyntaxInfo Syntax, EquatableArray<AttributeModel> Attributes)> Collected { get; } =
            [];

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<(TypeModel Type, SyntaxInfo Syntax, EquatableArray<AttributeModel> Attributes)>
                results = context.SyntaxProvider.ForTypesWithAttribute(_metadataName, "Values", _includeMembers);

            context.RegisterSourceOutput(results, (_, value) => Collected.Add(value));
        }
    }

    [Test]
    public async Task Should_include_members_of_every_part_when_requested()
    {
        var results = CollectResultsFromFiles(
            MarkAttributeSource,
            "Sample.MarkAttribute",
            [
                "[Sample.Mark] public partial class Widget { public int First; public string Name { get; set; } = \"\"; public void Run() { } }",
                "public partial class Widget { public int Second; public int Count { get; set; } public void Stop() { } }",
            ],
            includeMembers: true);

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].Type.Fields.AsSpan().ToArray().Select(field => field.Name).Order().ToArray())
            .IsEquivalentTo(new[] { "First", "Second" });
        await Assert.That(results[0].Type.Properties.AsSpan().ToArray().Select(property => property.Name).Order()
                .ToArray())
            .IsEquivalentTo(new[] { "Count", "Name" });
        await Assert.That(results[0].Type.Methods.AsSpan().ToArray().Select(method => method.Name).Order().ToArray())
            .IsEquivalentTo(new[] { "Run", "Stop" });
    }

    [Test]
    public async Task Should_leave_members_empty_by_default()
    {
        var results = CollectResultsFromFiles(
            MarkAttributeSource,
            "[Sample.Mark] public partial class Widget { public int First; public int Count { get; set; } public void Run() { } }");

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].Type.Fields.Count).IsEqualTo(0);
        await Assert.That(results[0].Type.Properties.Count).IsEqualTo(0);
        await Assert.That(results[0].Type.Methods.Count).IsEqualTo(0);
    }

    [Test]
    public void Should_stay_cacheable_with_members_included()
    {
        GeneratorHarness.AssertCacheable(
            new AttributeProviderTestGenerator(includeMembers: true),
            [
                """
                [Sample.Mark]
                public partial class Widget { public int Value; public void Run() { } }
                """,
                MarkAttributeSource,
            ],
            AttributeProviderTestGenerator.ValuesStepName);
    }
}
