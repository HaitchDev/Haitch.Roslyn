using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

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

    /// <summary>
    /// Additional files (for example <c>.json</c> or <c>.resx</c>) the generator can read; default none. Any
    /// <see cref="AdditionalText"/> works, so a test can supply its own implementation, for example
    /// <see cref="UnreadableAdditionalText"/>; <see cref="HarnessAdditionalText"/> covers the plain case.
    /// The instances reach the driver as given. Elements must not be <see langword="null"/>. Duplicate paths are
    /// allowed and share one <see cref="PerFileOptions"/> entry, as in Roslyn.
    /// </summary>
    public IReadOnlyList<AdditionalText>? AdditionalTexts { get; init; }

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

/// <summary>An in-memory additional file with a fixed path and text.</summary>
/// <param name="path">The path the generator sees; also the key for <see cref="GeneratorHarnessInput.PerFileOptions"/>.</param>
/// <param name="text">The file's content.</param>
public sealed class HarnessAdditionalText(string path, string text) : AdditionalText
{
    private readonly SourceText _text = SourceText.From(text);

    /// <summary>The path the generator sees; also the key for <see cref="GeneratorHarnessInput.PerFileOptions"/>.</summary>
    public override string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>The file's content.</summary>
    public string Text { get; } = text;

    /// <inheritdoc />
    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}
