using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Testing;

/// <summary>Everything the harness feeds a generator; new inputs are added as further optional properties.</summary>
public sealed class GeneratorHarnessInput
{
    /// <summary>C# source texts forming the input compilation.</summary>
    public required IReadOnlyList<string> Sources { get; init; }

    /// <summary>Extra references; the running runtime's platform assemblies are always included.</summary>
    public IReadOnlyList<MetadataReference>? AdditionalReferences { get; init; }

    /// <summary>Parse options; defaults to the newest language version Roslyn knows.</summary>
    public CSharpParseOptions? ParseOptions { get; init; }

    /// <summary>Additional files (for example <c>.json</c> or <c>.resx</c>) the generator can read; default none.</summary>
    public IReadOnlyList<HarnessAdditionalText>? AdditionalTexts { get; init; }

    /// <summary>
    /// Global analyzer-config options such as <c>build_property.RootNamespace</c>. Keys are passed through verbatim;
    /// lookups are case-insensitive like the compiler's.
    /// </summary>
    public IReadOnlyDictionary<string, string>? GlobalOptions { get; init; }

    /// <summary>
    /// Per-file analyzer-config options keyed by path, for example <c>build_metadata.AdditionalFiles.Kind</c>.
    /// The path of a source tree is <c>Source{i}.cs</c> (zero-based, in <see cref="Sources"/> order); the path of an
    /// additional text is its <see cref="HarnessAdditionalText.Path"/>. Keys are passed through verbatim; lookups
    /// are case-insensitive.
    /// </summary>
    public IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string>
    >? PerFileOptions { get; init; }

    /// <summary>
    /// When <see langword="true"/>, errors the input compilation already reports before generation (syntax errors, missing
    /// types, option errors) no longer fail the run; they are exposed on <see cref="GeneratorHarnessResult.InputDiagnostics"/>.
    /// Any error that is new after generation still throws, wherever it is located.
    /// </summary>
    public bool AllowInputErrors { get; init; }
}

/// <summary>An additional file handed to the generator.</summary>
/// <param name="Path">The path the generator sees; also the key for <see cref="GeneratorHarnessInput.PerFileOptions"/>.</param>
/// <param name="Text">The file's content.</param>
public sealed record HarnessAdditionalText(string Path, string Text);
