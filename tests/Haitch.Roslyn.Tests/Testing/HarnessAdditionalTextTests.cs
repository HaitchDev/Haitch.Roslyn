using System.Threading;
using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Haitch.Roslyn.Tests.Testing;

public class HarnessAdditionalTextTests
{
    private sealed class CustomText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(text);
    }

    private sealed class EchoGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var echoes = context
                .AdditionalTextsProvider.Combine(context.AnalyzerConfigOptionsProvider)
                .Select(
                    static (pair, ct) =>
                    {
                        var (file, options) = pair;
                        options
                            .GetOptions(file)
                            .TryGetValue("build_metadata.AdditionalFiles.Kind", out var kind);
                        return $"{file.Path}|{file.GetText(ct)}|{kind}";
                    }
                )
                .WithTrackingName("Echo");

            context.RegisterSourceOutput(
                echoes,
                static (spc, line) => spc.AddSource("Echo.g.cs", $"// {line}")
            );
        }
    }

    private static GeneratorHarnessInput CustomInput() =>
        new()
        {
            Sources = ["class Input { }"],
            AdditionalTexts = [new CustomText("custom/x.txt", "hello")],
            PerFileOptions = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["custom/x.txt"] = new Dictionary<string, string>
                {
                    ["build_metadata.AdditionalFiles.Kind"] = "k",
                },
            },
        };

    [Test]
    public async Task The_harness_text_is_an_AdditionalText_with_its_path_and_text()
    {
        object text = new HarnessAdditionalText("dir/a.json", "{ }");

        await Assert.That(text).IsAssignableTo<AdditionalText>();
        var additional = (AdditionalText)text;
        await Assert.That(additional.Path).IsEqualTo("dir/a.json");
        await Assert.That(additional.GetText()!.ToString()).IsEqualTo("{ }");
    }

    [Test]
    public async Task A_custom_AdditionalText_reaches_the_generator_with_its_options()
    {
        var result = GeneratorHarness.Run(new EchoGenerator(), CustomInput());

        await Assert.That(result.Sources["Echo.g.cs"]).Contains("// custom/x.txt|hello|k");
    }

    [Test]
    public async Task AssertCacheable_accepts_a_custom_AdditionalText()
    {
        var result = GeneratorHarness.AssertCacheable(new EchoGenerator(), CustomInput(), ["Echo"]);

        await Assert.That(result.Sources).ContainsKey("Echo.g.cs");
    }

    [Test]
    public async Task A_null_path_is_rejected()
    {
        await Assert
            .That(() => new HarnessAdditionalText(null!, "x"))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task A_null_text_is_rejected()
    {
        await Assert
            .That(() => new HarnessAdditionalText("a.json", null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task A_null_element_in_AdditionalTexts_fails_naming_its_index()
    {
        var input = new GeneratorHarnessInput
        {
            Sources = ["class Input { }"],
            AdditionalTexts = [new HarnessAdditionalText("a.json", "{ }"), null!],
        };

        var exception = await Assert
            .That(() => GeneratorHarness.Run(new EchoGenerator(), input))
            .Throws<GeneratorTestException>();
        await Assert.That(exception!.Message).Contains("AdditionalTexts[1]");
    }
}
