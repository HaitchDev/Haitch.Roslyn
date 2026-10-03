namespace Haitch.Roslyn.Testing;

/// <summary>Extra scenarios and strictness for <see cref="GeneratorHarness.AssertCacheable(Microsoft.CodeAnalysis.IIncrementalGenerator, GeneratorHarnessInput, IEnumerable{string}, CacheabilityOptions?)"/>.</summary>
public sealed class CacheabilityOptions
{
    /// <summary>
    /// Index of a source to receive an unrelated edit in a third run (after the trivia edit): <c>namespace HarnessUnrelatedEdit { }</c>
    /// is appended to it. "Unrelated" means it changes the compilation but no tracked step should read it, so a step
    /// whose model depends on the whole compilation fails. Must be at least 1 (Source0 takes the trivia edit) and less
    /// than the number of sources; <see langword="null"/> skips the run.
    /// </summary>
    public int? UnrelatedEditSourceIndex { get; init; }

    /// <summary>
    /// When <see langword="true"/>, after the trivia edit to Source0 at least one output of each named step must be
    /// <c>Unchanged</c> (the step re-ran for the edited source and produced an equal value), on top of every output
    /// being <c>Cached</c> or <c>Unchanged</c>. Name the per-item model step: an aggregate such as <c>Collect</c> over
    /// unchanged items reports <c>Cached</c> and fails this check.
    /// </summary>
    public bool RequireRecomputationAfterTriviaEdit { get; init; }
}
