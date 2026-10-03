using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Haitch.Roslyn.Tests.Generators;

// Each instance is a distinct object with the same contents as an equal one, which is what the IDE
// hands the generator when it replaces the provider; the harness always reuses a single instance.
internal sealed class DictionaryOptionsProvider(
    IReadOnlyDictionary<string, string> global,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? perFile = null
) : AnalyzerConfigOptionsProvider
{
    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
            values.TryGetValue(key, out value);
    }

    private static readonly Options Empty = new(new Dictionary<string, string>());

    public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(global);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        perFile is not null && perFile.TryGetValue(textFile.Path, out var values)
            ? new Options(values)
            : Empty;

    private sealed class Text(string path, string content) : AdditionalText
    {
        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content);
    }

    // Runs once, swaps in a second provider with the same contents, reruns, and returns the tracked
    // steps that were recomputed to a different value (Modified/New) instead of Unchanged/Cached.
    public static (int StepCount, List<string> Unstable) RerunWithEqualProvider(
        IIncrementalGenerator generator,
        IReadOnlyDictionary<string, string> global,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? perFile,
        IReadOnlyList<(string Path, string Content)> texts,
        params string[] stepNames
    )
    {
        var compilation = CSharpCompilation.Create(
            "Rerun",
            [CSharpSyntaxTree.ParseText("class Input { }")]
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: texts.Select(t => (AdditionalText)new Text(t.Path, t.Content)),
            optionsProvider: new DictionaryOptionsProvider(global, perFile),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );

        driver = driver.RunGenerators(compilation);
        driver = driver.WithUpdatedAnalyzerConfigOptions(
            new DictionaryOptionsProvider(
                new Dictionary<string, string>(global),
                perFile?.ToDictionary(p => p.Key, p => p.Value)
            )
        );
        driver = driver.RunGenerators(compilation);

        var tracked = driver.GetRunResult().Results[0].TrackedSteps;
        var count = 0;
        var unstable = new List<string>();

        foreach (var name in stepNames)
        {
            foreach (var step in tracked[name])
            {
                foreach (var output in step.Outputs)
                {
                    count++;
                    if (
                        output.Reason
                        is not (
                            IncrementalStepRunReason.Unchanged
                            or IncrementalStepRunReason.Cached
                        )
                    )
                    {
                        unstable.Add($"{name}: {output.Reason}");
                    }
                }
            }
        }

        return (count, unstable);
    }
}
