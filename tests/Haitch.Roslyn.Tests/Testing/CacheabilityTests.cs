using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Testing;

public class CacheabilityTests
{
    private const string Input = "class A { }\nclass B { }\n";

    private sealed class NamesGenerator(bool freshObject) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // The model is built in the transform itself: a fresh object behind a value-equal step would be shielded
            // by that step's Unchanged result and never rerun.
            var tracked = context
                .SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax,
                    (ctx, _) =>
                    {
                        var name = ((ClassDeclarationSyntax)ctx.Node).Identifier.Text;
                        return freshObject
                            ? (object)new FreshModel(name)
                            : name;
                    }
                )
                .WithTrackingName("model");
            context.RegisterSourceOutput(tracked, static (ctx, m) => ctx.AddSource(m + ".g.cs", $"class G{m} {{ }}"));
        }
    }

    private sealed class FreshModel(string name)
    {
        public string Name { get; } = name;

        public override string ToString() => Name;
    }

    [Test]
    public async Task Should_pass_and_return_first_run_result_when_models_are_value_equal()
    {
        var result = GeneratorHarness.AssertCacheable(new NamesGenerator(false), [Input], "model");

        await Assert.That(result.Sources.ContainsKey("A.g.cs")).IsTrue();
    }

    [Test]
    public async Task Should_fail_naming_step_output_and_reason_when_outputs_are_fresh_objects()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(new NamesGenerator(true), [Input], "model")
        );

        await Assert.That(ex.Message).Contains("Step 'model' run 1 output 0 was Modified after an unchanged compilation clone");
    }

    [Test]
    public async Task Should_fail_listing_existing_step_names_when_step_name_is_unknown()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(new NamesGenerator(false), [Input], "nope")
        );

        await Assert.That(ex.Message).Contains("nope");
        await Assert.That(ex.Message).Contains("model");
    }

    private sealed class ThrowOnRerunGenerator : IIncrementalGenerator
    {
        private int _calls;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var tracked = context
                .SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax,
                    (ctx, _) =>
                    {
                        if (Interlocked.Increment(ref _calls) > 2)
                        {
                            throw new InvalidOperationException("boom-on-rerun");
                        }

                        return ((ClassDeclarationSyntax)ctx.Node).Identifier.Text;
                    }
                )
                .WithTrackingName("model");
            context.RegisterSourceOutput(tracked, static (ctx, m) => ctx.AddSource(m + ".g.cs", $"class G{m} {{ }}"));
        }
    }

    [Test]
    public async Task Should_fail_with_the_exception_when_generator_throws_on_a_rerun()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(new ThrowOnRerunGenerator(), [Input], "model")
        );

        await Assert.That(ex.Message).Contains("boom-on-rerun");
    }

    [Test]
    public async Task Should_fail_when_no_step_names_are_given()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(new NamesGenerator(false), [Input])
        );

        await Assert.That(ex.Message).Contains("at least one tracked step name");
    }

    [Test]
    public async Task Should_fail_when_no_sources_are_given()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(new NamesGenerator(false), [], "model")
        );

        await Assert.That(ex.Message).Contains("AssertCacheable needs at least one source");
    }

    [Test]
    public async Task Should_pass_with_several_source_trees()
    {
        var result = GeneratorHarness.AssertCacheable(
            new NamesGenerator(false),
            ["class A { }\n", "class B { }\n"],
            "model"
        );

        await Assert.That(result.Sources.Keys).Contains("B.g.cs");
    }

    [Test]
    public async Task Should_apply_references_and_parse_options_through_the_overload()
    {
        var lib = CSharpCompilation.Create(
            "Lib",
            [CSharpSyntaxTree.ParseText("public class LibType { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        using var stream = new MemoryStream();
        lib.Emit(stream);
        stream.Position = 0;
        var libReference = MetadataReference.CreateFromStream(stream);

        var result = GeneratorHarness.AssertCacheable(
            new NamesGenerator(false),
            ["#if FLAG\nclass Uses : LibType { }\n#endif\n"],
            ["model"],
            [libReference],
            CSharpParseOptions.Default.WithPreprocessorSymbols("FLAG")
        );

        await Assert.That(result.Sources.Keys).Contains("Uses.g.cs");
    }
}
