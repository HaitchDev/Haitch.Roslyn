using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Testing;

/// <summary>The outcome of one <see cref="GeneratorHarness.Run(IIncrementalGenerator, GeneratorHarnessInput)"/>.</summary>
public sealed class GeneratorHarnessResult
{
    internal GeneratorHarnessResult(
        IReadOnlyDictionary<string, string> sources,
        ImmutableArray<Diagnostic> diagnostics,
        Compilation compilation,
        GeneratorDriver driver,
        Compilation inputCompilation,
        GeneratorDriverRunResult runResult,
        ImmutableArray<Diagnostic> inputDiagnostics
    )
    {
        Sources = sources;
        Diagnostics = diagnostics;
        Compilation = compilation;
        InputCompilation = inputCompilation;
        Driver = driver;
        RunResult = runResult;
        InputDiagnostics = inputDiagnostics;
    }

    /// <summary>Generated source text keyed by hint name.</summary>
    public IReadOnlyDictionary<string, string> Sources { get; }

    /// <summary>Diagnostics reported by the generator itself, not compiler diagnostics.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Error diagnostics of the input compilation, kept apart from <see cref="Diagnostics"/>; empty unless
    /// <see cref="GeneratorHarnessInput.AllowInputErrors"/> let errors through.
    /// </summary>
    public ImmutableArray<Diagnostic> InputDiagnostics { get; }

    /// <summary>The compilation after the generator's sources were added.</summary>
    public Compilation Compilation { get; }

    /// <summary>The compilation before generation, for rerunning <see cref="Driver"/> without duplicating generated trees.</summary>
    public Compilation InputCompilation { get; }

    /// <summary>The driver after the run, so a caller can rerun it against a changed compilation.</summary>
    public GeneratorDriver Driver { get; }

    /// <summary>The driver's run result, including tracked incremental steps.</summary>
    public GeneratorDriverRunResult RunResult { get; }
}
