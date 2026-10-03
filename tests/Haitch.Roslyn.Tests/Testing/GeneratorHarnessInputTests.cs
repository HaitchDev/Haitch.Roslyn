using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Testing;

public class GeneratorHarnessInputTests
{
    private sealed class EchoGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var models = context
                .SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) =>
                        node
                            is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax
                            {
                                Identifier.Text: "Input"
                            },
                    static (ctx, _) =>
                        ((CSharpParseOptions)ctx.Node.SyntaxTree.Options).LanguageVersion.ToString()
                )
                .WithTrackingName("Models");

            context.RegisterSourceOutput(
                models,
                (ctx, version) => ctx.AddSource("Echo.g.cs", $"// {version}\nclass Echo {{ }}")
            );
        }
    }

    private static readonly string[] Sources = ["class Input { }"];

    [Test]
    public async Task Run_with_input_matches_the_sources_overload()
    {
        var old = GeneratorHarness.Run(new EchoGenerator(), Sources);
        var result = GeneratorHarness.Run(
            new EchoGenerator(),
            new GeneratorHarnessInput { Sources = Sources }
        );

        await Assert.That(result.Sources).IsEquivalentTo(old.Sources);
        await Assert.That(result.Sources.ContainsKey("Echo.g.cs")).IsTrue();
    }

    [Test]
    public async Task Run_with_input_honours_parse_options()
    {
        var result = GeneratorHarness.Run(
            new EchoGenerator(),
            new GeneratorHarnessInput
            {
                Sources = Sources,
                ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(
                    LanguageVersion.CSharp9
                ),
            }
        );

        await Assert.That(result.Sources["Echo.g.cs"]).Contains("CSharp9");
    }

    [Test]
    public async Task Run_with_input_honours_additional_references()
    {
        var library = CSharpCompilation.Create(
            "ExtraLib",
            [CSharpSyntaxTree.ParseText("public class FromExtra { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        using var stream = new MemoryStream();
        library.Emit(stream);
        var reference = MetadataReference.CreateFromImage(stream.ToArray());

        var result = GeneratorHarness.Run(
            new EchoGenerator(),
            new GeneratorHarnessInput
            {
                Sources = ["class Input : FromExtra { }"],
                AdditionalReferences = [reference],
            }
        );

        await Assert.That(result.Sources.ContainsKey("Echo.g.cs")).IsTrue();
    }

    [Test]
    public async Task AssertCacheable_with_input_matches_the_sources_overload()
    {
        var old = GeneratorHarness.AssertCacheable(new EchoGenerator(), Sources, "Models");
        var result = GeneratorHarness.AssertCacheable(
            new EchoGenerator(),
            new GeneratorHarnessInput { Sources = Sources },
            ["Models"]
        );

        await Assert.That(result.Sources).IsEquivalentTo(old.Sources);
    }

    [Test]
    public async Task AssertCacheable_with_input_applies_options_and_parse_options()
    {
        var input = new GeneratorHarnessInput
        {
            Sources = ["class Input { }", "class Other { }"],
            ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9),
        };

        var result = GeneratorHarness.AssertCacheable(
            new EchoGenerator(),
            input,
            ["Models"],
            new CacheabilityOptions { UnrelatedEditSourceIndex = 1 }
        );

        await Assert.That(result.Sources["Echo.g.cs"]).Contains("CSharp9");
        await Assert
            .That(() =>
                GeneratorHarness.AssertCacheable(
                    new EchoGenerator(),
                    input,
                    ["Models"],
                    new CacheabilityOptions { UnrelatedEditSourceIndex = 5 }
                )
            )
            .Throws<GeneratorTestException>();
    }

    [Test]
    public async Task AssertCacheable_with_input_rejects_unknown_step_names()
    {
        await Assert
            .That(() =>
                GeneratorHarness.AssertCacheable(
                    new EchoGenerator(),
                    new GeneratorHarnessInput { Sources = Sources },
                    ["Nope"]
                )
            )
            .Throws<GeneratorTestException>();
    }
}
