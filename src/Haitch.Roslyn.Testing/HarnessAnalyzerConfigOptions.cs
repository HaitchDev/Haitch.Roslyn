using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Haitch.Roslyn.Testing;

internal sealed class HarnessAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values)
    : AnalyzerConfigOptions
{
    // The compiler compares option keys case-insensitively, so two keys that differ only by case
    // would silently shadow each other; refusing them keeps the test's intent unambiguous.
    private readonly Dictionary<string, string> _values = Build(values);

    private static Dictionary<string, string> Build(IReadOnlyDictionary<string, string> values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var firstKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (!result.TryAdd(key, value))
            {
                throw new GeneratorTestException(
                    $"Option keys '{firstKeys[key]}' and '{key}' differ only by case; analyzer-config keys are case-insensitive, so give each option once."
                );
            }

            firstKeys[key] = key;
        }

        return result;
    }

    public static readonly HarnessAnalyzerConfigOptions Empty = new(
        new Dictionary<string, string>()
    );

    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
        _values.TryGetValue(key, out value);
}

internal sealed class HarnessAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly Dictionary<string, HarnessAnalyzerConfigOptions> _perFile;

    public HarnessAnalyzerConfigOptionsProvider(
        IReadOnlyDictionary<string, string>? global,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? perFile
    )
    {
        GlobalOptions = global is null
            ? HarnessAnalyzerConfigOptions.Empty
            : new HarnessAnalyzerConfigOptions(global);
        _perFile = new Dictionary<string, HarnessAnalyzerConfigOptions>(StringComparer.Ordinal);
        foreach (
            var (path, options) in perFile
                ?? new Dictionary<string, IReadOnlyDictionary<string, string>>()
        )
        {
            _perFile[path] = new HarnessAnalyzerConfigOptions(options);
        }
    }

    public override AnalyzerConfigOptions GlobalOptions { get; }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => ForPath(tree.FilePath);

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        ForPath(textFile.Path);

    private AnalyzerConfigOptions ForPath(string path) =>
        _perFile.TryGetValue(path, out var options) ? options : HarnessAnalyzerConfigOptions.Empty;
}
