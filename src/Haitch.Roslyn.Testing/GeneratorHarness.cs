using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Testing;

/// <summary>Runs an incremental generator over in-memory sources with step tracking enabled.</summary>
public static class GeneratorHarness
{
    /// <summary>
    /// Runs <paramref name="generator"/> over <paramref name="sources"/>.
    /// </summary>
    /// <param name="generator">The generator under test.</param>
    /// <param name="sources">C# source texts forming the input compilation.</param>
    /// <param name="additionalReferences">Extra references; the running runtime's platform assemblies are always included.</param>
    /// <param name="parseOptions">Parse options; defaults to the newest language version Roslyn knows.</param>
    /// <remarks>The compilation is nullable-enabled; its references are the test host's trusted platform assemblies plus <paramref name="additionalReferences"/>.</remarks>
    /// <returns>The generated sources by hint name and the generator's own diagnostics.</returns>
    /// <exception cref="GeneratorTestException">The output compilation has error-severity diagnostics.</exception>
    public static GeneratorHarnessResult Run(
        IIncrementalGenerator generator,
        IEnumerable<string> sources,
        IEnumerable<MetadataReference>? additionalReferences = null,
        CSharpParseOptions? parseOptions = null
    )
    {
        parseOptions ??= CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        var trees = sources
            .Select((text, i) => CSharpSyntaxTree.ParseText(text, parseOptions, path: $"Source{i}.cs"))
            .ToList();

        var references = PlatformReferences.Value.Concat(additionalReferences ?? []).ToList();

        var compilation = CSharpCompilation.Create(
            "HarnessCompilation",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable)
        );

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true)
        );

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var runResult = driver.GetRunResult();

        var failures = runResult.Results.Where(r => r.Exception is not null).ToList();
        if (failures.Count > 0)
        {
            throw new GeneratorTestException(
                "Generator threw:" + string.Concat(failures.Select(f => Environment.NewLine + f.Exception))
            );
        }

        // Checked after generation so input that references generated types (post-initialization
        // markers) resolves; errors are grouped by whether their tree was part of the input.
        var inputTrees = compilation.SyntaxTrees.ToHashSet();
        var allErrors = Errors(output);
        var inputErrors = allErrors.Where(e => e.Location.SourceTree is not { } tree || inputTrees.Contains(tree))
            .ToList();
        var outputErrors = allErrors.Where(e => !inputErrors.Contains(e)).ToList();
        if (inputErrors.Count > 0 || outputErrors.Count > 0)
        {
            var message = new System.Text.StringBuilder();
            AppendErrors(message, "Input source errors:", inputErrors);
            AppendErrors(message, "Generated output errors:", outputErrors);
            throw new GeneratorTestException(message.ToString().TrimEnd());
        }

        var generated = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var result in runResult.Results)
        {
            foreach (var source in result.GeneratedSources)
            {
                generated[source.HintName] = source.SourceText.ToString();
            }
        }

        return new GeneratorHarnessResult(generated, runResult.Diagnostics, output, driver, compilation, runResult);
    }

    /// <inheritdoc cref="AssertCacheable(IIncrementalGenerator, IEnumerable{string}, IEnumerable{string}, IEnumerable{MetadataReference}?, CSharpParseOptions?)"/>
    public static GeneratorHarnessResult AssertCacheable(
        IIncrementalGenerator generator,
        IEnumerable<string> sources,
        params string[] trackedStepNames
    ) => AssertCacheable(generator, sources, (IEnumerable<string>)trackedStepNames);

    /// <summary>
    /// Runs <paramref name="generator"/>, then reruns it on a cloned compilation (run 1) and on a compilation whose
    /// first source gained a trailing comment (run 2), and requires every output of the named steps to be cached or unchanged.
    /// An output that holds a caching hazard (see <see cref="CachingHazardWalker.Find"/>) also fails.
    /// </summary>
    /// <param name="generator">The generator under test.</param>
    /// <param name="sources">C# source texts forming the input compilation; at least one.</param>
    /// <param name="trackedStepNames">
    /// Names given with <c>WithTrackingName</c>; at least one. Name model steps only: steps that combine with the
    /// compilation or output syntax nodes are legitimately modified by the trivia edit. A name that never ran is a failure.
    /// </param>
    /// <param name="additionalReferences">Extra references; the running runtime's platform assemblies are always included.</param>
    /// <param name="parseOptions">Parse options; defaults to the newest language version Roslyn knows.</param>
    /// <returns>The first run's result.</returns>
    /// <exception cref="GeneratorTestException">
    /// No sources or step names were given, a step is unknown, the generator threw on a rerun, a step has an output
    /// that was recomputed to a different value, or a step output holds a caching hazard.
    /// </exception>
    public static GeneratorHarnessResult AssertCacheable(
        IIncrementalGenerator generator,
        IEnumerable<string> sources,
        IEnumerable<string> trackedStepNames,
        IEnumerable<MetadataReference>? additionalReferences = null,
        CSharpParseOptions? parseOptions = null
    ) => AssertCacheable(generator, sources, trackedStepNames, new CacheabilityOptions(), additionalReferences,
        parseOptions);

    /// <summary>
    /// As the other overloads, with the extra scenarios and strictness of <paramref name="options"/>.
    /// </summary>
    /// <param name="generator">The generator under test.</param>
    /// <param name="sources">C# source texts forming the input compilation; at least one.</param>
    /// <param name="trackedStepNames">Names given with <c>WithTrackingName</c>; at least one.</param>
    /// <param name="options">Extra scenarios and strictness.</param>
    /// <param name="additionalReferences">Extra references; the running runtime's platform assemblies are always included.</param>
    /// <param name="parseOptions">Parse options; defaults to the newest language version Roslyn knows.</param>
    /// <returns>The first run's result.</returns>
    /// <exception cref="GeneratorTestException">
    /// As the other overloads; also <see cref="CacheabilityOptions.UnrelatedEditSourceIndex"/> is out of range, a step
    /// output is <c>Modified</c> or <c>New</c> after the unrelated edit (run 3), or
    /// <see cref="CacheabilityOptions.RequireRecomputationAfterTriviaEdit"/> finds no <c>Unchanged</c> output of a named step after the trivia edit.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static GeneratorHarnessResult AssertCacheable(
        IIncrementalGenerator generator,
        IEnumerable<string> sources,
        IEnumerable<string> trackedStepNames,
        CacheabilityOptions options,
        IEnumerable<MetadataReference>? additionalReferences = null,
        CSharpParseOptions? parseOptions = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        var sourceList = sources.ToList();
        if (sourceList.Count == 0)
        {
            throw new GeneratorTestException("AssertCacheable needs at least one source");
        }

        var names = trackedStepNames.ToList();
        if (names.Count == 0)
        {
            throw new GeneratorTestException("AssertCacheable needs at least one tracked step name");
        }

        if (options.UnrelatedEditSourceIndex is { } index)
        {
            if (sourceList.Count < 2)
            {
                throw new GeneratorTestException("UnrelatedEditSourceIndex needs at least two sources");
            }

            if (index < 1 || index >= sourceList.Count)
            {
                throw new GeneratorTestException(
                    $"UnrelatedEditSourceIndex {index} is out of range; it must be between 1 and {sourceList.Count - 1} (Source0 takes the trivia edit)."
                );
            }
        }

        var first = Run(generator, sourceList, additionalReferences, parseOptions);
        var known = first.RunResult.Results[0].TrackedSteps;

        var unknown = names.Where(n => !known.ContainsKey(n)).ToList();
        if (unknown.Count > 0)
        {
            throw new GeneratorTestException(
                $"Tracked step(s) not found: {string.Join(", ", unknown)}. "
                + $"Steps that exist: {string.Join(", ", known.Keys.OrderBy(k => k, StringComparer.Ordinal))}."
            );
        }

        var driver = first.Driver.RunGenerators(first.InputCompilation.Clone());
        AssertStepsCached(driver, names, 1, "an unchanged compilation clone");

        var firstTree = first.InputCompilation.SyntaxTrees.First();
        var edited = CSharpSyntaxTree.ParseText(
            firstTree.GetText().ToString() + "// trailing trivia\n",
            (CSharpParseOptions)firstTree.Options,
            firstTree.FilePath
        );
        var triviaCompilation = first.InputCompilation.ReplaceSyntaxTree(firstTree, edited);
        driver = driver.RunGenerators(triviaCompilation);
        AssertStepsCached(
            driver,
            names,
            2,
            "a trivia-only edit",
            options.RequireRecomputationAfterTriviaEdit
        );

        if (options.UnrelatedEditSourceIndex is { } unrelated)
        {
            var path = $"Source{unrelated}.cs";
            var target = triviaCompilation.SyntaxTrees.Single(t => t.FilePath == path);
            var changed = CSharpSyntaxTree.ParseText(
                target.GetText().ToString() + "\nnamespace HarnessUnrelatedEdit { }\n",
                (CSharpParseOptions)target.Options,
                target.FilePath
            );
            driver = driver.RunGenerators(triviaCompilation.ReplaceSyntaxTree(target, changed));
            AssertStepsCached(driver, names, 3, $"an unrelated edit in {path}");
        }

        AssertNoHazards(first.RunResult.Results[0].TrackedSteps, names);

        return first;
    }

    private static void AssertNoHazards(
        ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> trackedSteps,
        List<string> stepNames
    )
    {
        foreach (var name in stepNames)
        {
            foreach (var step in trackedSteps[name])
            {
                for (var i = 0; i < step.Outputs.Length; i++)
                {
                    if (CachingHazardWalker.FindHazard(step.Outputs[i].Value) is { } hazard)
                    {
                        throw new GeneratorTestException(
                            $"Step '{name}' output {i} holds a caching hazard at {hazard.Path}: {hazard.Reason}."
                        );
                    }
                }
            }
        }
    }

    private static void AssertStepsCached(
        GeneratorDriver driver,
        List<string> stepNames,
        int run,
        string scenario,
        bool requireUnchanged = false
    )
    {
        var result = driver.GetRunResult().Results[0];
        if (result.Exception is not null)
        {
            throw new GeneratorTestException(
                $"Generator threw in run {run} after {scenario}:{Environment.NewLine}{result.Exception}");
        }

        foreach (var name in stepNames)
        {
            if (!result.TrackedSteps.TryGetValue(name, out var steps))
            {
                throw new GeneratorTestException($"Step '{name}' did not run in run {run} after {scenario}.");
            }

            var unchanged = 0;
            foreach (var step in steps)
            {
                for (var i = 0; i < step.Outputs.Length; i++)
                {
                    var reason = step.Outputs[i].Reason;
                    if (reason is not (IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged))
                    {
                        var hazard = CachingHazardWalker.FindHazard(step.Outputs[i].Value) is { } found
                            ? $" The output holds a caching hazard at {found.Path}: {found.Reason}."
                            : "";
                        throw new GeneratorTestException(
                            $"Step '{name}' run {run} output {i} was {reason} after {scenario}; expected Cached or Unchanged.{hazard}"
                        );
                    }

                    if (reason is IncrementalStepRunReason.Unchanged)
                    {
                        unchanged++;
                    }
                }
            }

            if (requireUnchanged && unchanged == 0)
            {
                throw new GeneratorTestException(
                    $"Step '{name}' run {run} had no Unchanged output after {scenario}, so it did not re-run for the edited source; "
                    + "RequireRecomputationAfterTriviaEdit needs the per-item model step named "
                    + "(an aggregate such as Collect over unchanged items reports Cached)."
                );
            }
        }
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> PlatformReferences = new(() =>
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        return
        [
            .. tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)),
        ];
    });

    private static List<Diagnostic> Errors(Compilation compilation) =>
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

    private static void AppendErrors(System.Text.StringBuilder message, string heading, List<Diagnostic> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        message.AppendLine(heading);
        foreach (var error in errors)
        {
            message.AppendLine(Format(error));
        }
    }

    private static string Format(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var file = span.Path.Length == 0
            ? "<no file>"
            : span.Path;
        return $"{file}:{span.StartLinePosition.Line + 1}: {diagnostic.Id}: {diagnostic.GetMessage()}";
    }
}
