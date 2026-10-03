using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Testing;

public class GeneratorHarnessInputErrorTests
{
    private const string BrokenInput = "class A : Missing { void M( }";

    private sealed class EmitGenerator(string body) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var names = context
                .CompilationProvider.Select(static (c, _) => c.SyntaxTrees.Count())
                .WithTrackingName("Count");
            context.RegisterSourceOutput(names, (ctx, _) => ctx.AddSource("Out.g.cs", body));
        }
    }

    [Test]
    public async Task AllowInputErrors_ExposesSyntaxAndMissingTypeErrors()
    {
        var result = GeneratorHarness.Run(
            new EmitGenerator("class Ok { }"),
            new GeneratorHarnessInput { Sources = [BrokenInput], AllowInputErrors = true }
        );

        var ids = result.InputDiagnostics.Select(d => d.Id).ToList();
        await Assert.That(ids).Contains("CS1026");
        await Assert.That(ids).Contains("CS0246");
        await Assert.That(result.Sources.ContainsKey("Out.g.cs")).IsTrue();
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task AllowInputErrors_StillThrowsForBrokenGeneratedCode()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = GeneratorHarness.Run(
                new EmitGenerator("class Bad { Nope x; }"),
                new GeneratorHarnessInput { Sources = [BrokenInput], AllowInputErrors = true }
            )
        );
        await Assert.That(ex.Message).Contains("Generated output errors");
        await Assert.That(ex.Message).Contains("Out.g.cs");
        await Assert.That(ex.Message).DoesNotContain("Input source errors");
    }

    [Test]
    public async Task Default_StillThrowsOnInputErrors()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = GeneratorHarness.Run(
                new EmitGenerator("class Ok { }"),
                new GeneratorHarnessInput { Sources = [BrokenInput] }
            )
        );
        await Assert.That(ex.Message).Contains("Input source errors:");
        await Assert.That(ex.Message).Contains("Source0.cs");
    }

    [Test]
    public async Task ValidInput_HasEmptyInputDiagnostics()
    {
        var result = GeneratorHarness.Run(
            new EmitGenerator("class Ok { }"),
            new GeneratorHarnessInput { Sources = ["class A { }"], AllowInputErrors = true }
        );

        await Assert.That(result.InputDiagnostics).IsEmpty();
    }

    [Test]
    public async Task AssertCacheable_HonoursAllowInputErrors()
    {
        var result = GeneratorHarness.AssertCacheable(
            new EmitGenerator("class Ok { }"),
            new GeneratorHarnessInput { Sources = [BrokenInput], AllowInputErrors = true },
            ["Count"]
        );

        await Assert.That(result.InputDiagnostics).IsNotEmpty();
    }
}
