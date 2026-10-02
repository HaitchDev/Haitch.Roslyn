using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Testing;

public class GeneratorHarnessTests
{
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor Descriptor = new(
        "HR9001",
        "Fixture",
        "Fixture message",
        "Test",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
#pragma warning restore RS2008

    private sealed class FixtureGenerator(string hintName, string source, bool report = false) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(
                context.CompilationProvider,
                (ctx, _) =>
                {
                    ctx.AddSource(hintName, source);
                    if (report)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, Location.None));
                    }
                }
            );
    }

    private sealed class DelegateGenerator(Action<IncrementalGeneratorInitializationContext> init)
        : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) => init(context);
    }

    [Test]
    public async Task Should_return_generated_source_under_its_hint_name()
    {
        var result = GeneratorHarness.Run(
            new FixtureGenerator("Hello.g.cs", "class Hello { }"),
            ["class Input { }"]
        );

        await Assert.That(result.Sources["Hello.g.cs"]).IsEqualTo("class Hello { }");
        await Assert.That(result.Diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Should_return_generator_reported_diagnostics_without_throwing()
    {
        var result = GeneratorHarness.Run(new FixtureGenerator("Hello.g.cs", "class Hello { }", report: true), []);

        await Assert.That(result.Diagnostics.Length).IsEqualTo(1);
        await Assert.That(result.Diagnostics[0].Id).IsEqualTo("HR9001");
    }

    [Test]
    public async Task Should_expose_the_driver_and_run_result()
    {
        var result = GeneratorHarness.Run(new FixtureGenerator("Hello.g.cs", "class Hello { }"), []);

        await Assert.That(result.Driver).IsNotNull();
        await Assert.That(result.RunResult.GeneratedTrees.Length).IsEqualTo(1);
        await Assert.That(result.Compilation.SyntaxTrees.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Should_throw_with_error_id_file_and_line_when_output_does_not_compile()
    {
        var generator = new FixtureGenerator("Broken.g.cs", "class Broken\n{\n    int x = ;\n}");

        var ex = Assert.Throws<GeneratorTestException>(() => GeneratorHarness.Run(generator, []));

        await Assert.That(ex.Message).Contains("CS1525");
        await Assert.That(ex.Message).Contains("Broken.g.cs:3:");
    }

    [Test]
    public async Task Should_label_generated_output_errors()
    {
        var generator = new FixtureGenerator("Broken.g.cs", "class Broken { int x = ; }");

        var ex = Assert.Throws<GeneratorTestException>(() => GeneratorHarness.Run(generator, []));

        await Assert.That(ex.Message).Contains("Generated output errors");
        await Assert.That(ex.Message).DoesNotContain("Input source errors");
    }

    [Test]
    public async Task Should_label_input_source_errors_separately()
    {
        var generator = new FixtureGenerator("Hello.g.cs", "class Hello { }");

        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.Run(generator, ["class Input\n{\n    int x = ;\n}"])
        );

        await Assert.That(ex.Message).Contains("Input source errors");
        await Assert.That(ex.Message).Contains("Source0.cs:3:");
        await Assert.That(ex.Message).DoesNotContain("Generated output errors");
    }

    [Test]
    public async Task Should_accept_input_that_uses_a_post_initialization_attribute()
    {
        var generator = new DelegateGenerator(context =>
            context.RegisterPostInitializationOutput(ctx =>
                ctx.AddSource("Marker.g.cs", "internal sealed class MarkerAttribute : System.Attribute { }")
            )
        );

        var result = GeneratorHarness.Run(generator, ["[Marker] class Input { }"]);

        await Assert.That(result.Sources.Keys).Contains("Marker.g.cs");
    }

    [Test]
    public async Task Should_still_throw_for_an_input_error_that_generation_does_not_resolve()
    {
        var generator = new DelegateGenerator(context =>
            context.RegisterPostInitializationOutput(ctx =>
                ctx.AddSource("Marker.g.cs", "internal sealed class MarkerAttribute : System.Attribute { }")
            )
        );

        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.Run(generator, ["[Typo] class Input { }"])
        );

        await Assert.That(ex.Message).Contains("Input source errors");
        await Assert.That(ex.Message).Contains("CS0246");
    }

    [Test]
    public async Task Should_throw_when_the_generator_throws()
    {
        var generator = new DelegateGenerator(context =>
            context.RegisterSourceOutput(
                context.CompilationProvider,
                (_, _) => throw new InvalidOperationException("boom from generator")
            )
        );

        var ex = Assert.Throws<GeneratorTestException>(() => GeneratorHarness.Run(generator, []));

        await Assert.That(ex.Message).Contains("InvalidOperationException");
        await Assert.That(ex.Message).Contains("boom from generator");
    }

    [Test]
    public async Task Should_expose_the_input_compilation_without_generated_trees()
    {
        var result = GeneratorHarness.Run(new FixtureGenerator("Hello.g.cs", "class Hello { }"), ["class Input { }"]);

        await Assert.That(result.InputCompilation.SyntaxTrees.Count()).IsEqualTo(1);
        await Assert.That(result.Compilation.SyntaxTrees.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task Should_order_sources_by_hint_name()
    {
        var generator = new DelegateGenerator(context =>
            context.RegisterSourceOutput(
                context.CompilationProvider,
                (ctx, _) =>
                {
                    ctx.AddSource("b.g.cs", "class B { }");
                    ctx.AddSource("C.g.cs", "class C { }");
                    ctx.AddSource("a.g.cs", "class A { }");
                }
            )
        );

        var result = GeneratorHarness.Run(generator, []);

        await Assert.That(string.Join(",", result.Sources.Keys)).IsEqualTo("C.g.cs,a.g.cs,b.g.cs");
    }

    [Test]
    public async Task Should_track_incremental_steps()
    {
        var generator = new DelegateGenerator(context =>
        {
            var named = context.CompilationProvider.Select((c, _) => c.AssemblyName).WithTrackingName("x");
            context.RegisterSourceOutput(named, (ctx, _) => ctx.AddSource("Hello.g.cs", "class Hello { }"));
        });

        var result = GeneratorHarness.Run(generator, []);

        await Assert.That(result.RunResult.Results[0].TrackedSteps.ContainsKey("x")).IsTrue();
    }

    [Test]
    public async Task Should_apply_additional_references_and_parse_options()
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

        const string input = "#if FLAG\nclass Uses : LibType { }\n#endif";
        var options = CSharpParseOptions.Default.WithPreprocessorSymbols("FLAG");

        var result = GeneratorHarness.Run(
            new FixtureGenerator("Hello.g.cs", "class Hello { }"),
            [input],
            [libReference],
            options
        );

        await Assert.That(result.InputCompilation.GetTypeByMetadataName("Uses")).IsNotNull();
    }
}
