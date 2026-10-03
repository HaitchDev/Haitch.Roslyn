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
                        return freshObject ? (object)new FreshModel(name) : name;
                    }
                )
                .WithTrackingName("model");
            context.RegisterSourceOutput(
                tracked,
                static (ctx, m) => ctx.AddSource(m + ".g.cs", $"class G{m} {{ }}")
            );
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

        await Assert
            .That(ex.Message)
            .Contains(
                "Step 'model' run 1 output 0 was Modified after an unchanged compilation clone"
            );
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
            context.RegisterSourceOutput(
                tracked,
                static (ctx, m) => ctx.AddSource(m + ".g.cs", $"class G{m} {{ }}")
            );
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

    private sealed class NamespaceCountGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var tracked = context
                .CompilationProvider.Select(
                    static (c, _) => c.GlobalNamespace.GetNamespaceMembers().Count()
                )
                .WithTrackingName("model");
            context.RegisterSourceOutput(
                tracked,
                static (ctx, n) => ctx.AddSource("Count.g.cs", $"// {n}")
            );
        }
    }

    private static readonly CacheabilityOptions UnrelatedEdit = new()
    {
        UnrelatedEditSourceIndex = 1,
    };

    [Test]
    public async Task Should_pass_the_unrelated_edit_when_models_are_value_equal()
    {
        var result = GeneratorHarness.AssertCacheable(
            new NamesGenerator(false),
            ["class A { }\n", "class B { }\n"],
            ["model"],
            UnrelatedEdit
        );

        await Assert.That(result.Sources.Keys).Contains("B.g.cs");
    }

    [Test]
    public async Task Should_pass_a_compilation_dependent_step_without_the_unrelated_edit()
    {
        var result = GeneratorHarness.AssertCacheable(
            new NamespaceCountGenerator(),
            ["class A { }\n", "class B { }\n"],
            "model"
        );

        await Assert.That(result.Sources.Keys).Contains("Count.g.cs");
    }

    [Test]
    public async Task Should_fail_the_unrelated_edit_when_a_model_captures_the_compilation_namespace_count()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(
                new NamespaceCountGenerator(),
                ["class A { }\n", "class B { }\n"],
                ["model"],
                UnrelatedEdit
            )
        );

        await Assert
            .That(ex.Message)
            .IsEqualTo(
                "Step 'model' run 3 output 0 was Modified after an unrelated edit in Source1.cs; expected Cached or Unchanged."
            );
    }

    [Test]
    public async Task Should_fail_when_unrelated_edit_index_is_zero()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(
                new NamesGenerator(false),
                ["class A { }\n", "class B { }\n"],
                ["model"],
                new CacheabilityOptions { UnrelatedEditSourceIndex = 0 }
            )
        );

        await Assert
            .That(ex.Message)
            .IsEqualTo(
                "UnrelatedEditSourceIndex 0 is out of range; it must be between 1 and 1 (Source0 takes the trivia edit)."
            );
    }

    [Test]
    public async Task Should_fail_when_unrelated_edit_index_is_past_the_last_source()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(
                new NamesGenerator(false),
                ["class A { }\n", "class B { }\n"],
                ["model"],
                new CacheabilityOptions { UnrelatedEditSourceIndex = 2 }
            )
        );

        await Assert
            .That(ex.Message)
            .IsEqualTo(
                "UnrelatedEditSourceIndex 2 is out of range; it must be between 1 and 1 (Source0 takes the trivia edit)."
            );
    }

    [Test]
    public async Task Should_fail_when_unrelated_edit_index_is_set_with_one_source()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(
                new NamesGenerator(false),
                ["class A { }\n"],
                ["model"],
                new CacheabilityOptions { UnrelatedEditSourceIndex = 1 }
            )
        );

        await Assert
            .That(ex.Message)
            .IsEqualTo("UnrelatedEditSourceIndex needs at least two sources");
    }

    [Test]
    public async Task Should_throw_argument_null_when_options_is_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            GeneratorHarness.AssertCacheable(
                new NamesGenerator(false),
                [Input],
                ["model"],
                options: null!
            )
        );

        await Task.CompletedTask;
    }

    private static readonly CacheabilityOptions Strict = new()
    {
        RequireRecomputationAfterTriviaEdit = true,
    };

    [Test]
    public async Task Should_pass_strict_mode_for_a_two_source_per_item_step()
    {
        var result = GeneratorHarness.AssertCacheable(
            new NamesGenerator(false),
            ["class A { }\n", "class B { }\n"],
            ["model"],
            Strict
        );

        await Assert.That(result.Sources.Keys).Contains("B.g.cs");
    }

    private sealed class DownstreamGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var tracked = context
                .SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax,
                    static (ctx, _) => ((ClassDeclarationSyntax)ctx.Node).Identifier.Text
                )
                .Select(static (name, _) => name.ToUpperInvariant())
                .WithTrackingName("model");
            context.RegisterSourceOutput(
                tracked,
                static (ctx, m) => ctx.AddSource(m + ".g.cs", $"class G{m} {{ }}")
            );
        }
    }

    [Test]
    public async Task Should_fail_strict_mode_when_every_output_is_cached_after_the_trivia_edit()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(
                new DownstreamGenerator(),
                ["class A { }\n"],
                ["model"],
                Strict
            )
        );

        await Assert.That(ex.Message).IsEqualTo(NoUnchangedMessage);
    }

    private const string NoUnchangedMessage =
        "Step 'model' run 2 had no Unchanged output after a trivia-only edit, so it did not re-run for the edited source; "
        + "RequireRecomputationAfterTriviaEdit needs the per-item model step named "
        + "(an aggregate such as Collect over unchanged items reports Cached).";

    private sealed class ConstantSelectGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var tracked = context
                .SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax,
                    // The node itself changes with the trivia edit, so the Select re-runs and maps it to an equal constant.
                    static (ctx, _) => ctx.Node
                )
                .Select(static (_, _) => "Same")
                .WithTrackingName("model");
            context.RegisterSourceOutput(tracked, static (_, _) => { });
        }
    }

    [Test]
    public async Task Should_pass_strict_mode_when_a_select_maps_every_item_to_a_constant()
    {
        var result = GeneratorHarness.AssertCacheable(
            new ConstantSelectGenerator(),
            ["class A { }\n", "class B { }\n"],
            ["model"],
            Strict
        );

        await Assert.That(result.Sources).IsEmpty();
    }

    private sealed class CollectGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var tracked = context
                .SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax,
                    static (ctx, _) => ((ClassDeclarationSyntax)ctx.Node).Identifier.Text
                )
                .Collect()
                .WithTrackingName("model");
            context.RegisterSourceOutput(tracked, static (_, _) => { });
        }
    }

    [Test]
    public async Task Should_fail_strict_mode_when_only_a_collect_step_is_named()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(
                new CollectGenerator(),
                ["class A { }\n"],
                ["model"],
                Strict
            )
        );

        await Assert.That(ex.Message).IsEqualTo(NoUnchangedMessage);
    }
}
