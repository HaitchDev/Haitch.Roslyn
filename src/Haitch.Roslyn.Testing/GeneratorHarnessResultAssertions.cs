using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Testing;

/// <summary>Assertions over the diagnostics a generator reported, throwing <see cref="GeneratorTestException"/>.</summary>
public static class GeneratorHarnessResultAssertions
{
    /// <summary>Asserts the generator reported no diagnostics; compiler and input diagnostics are not considered.</summary>
    /// <returns>The same result, for chaining.</returns>
    public static GeneratorHarnessResult AssertNoDiagnostics(this GeneratorHarnessResult result)
    {
        if (result.Diagnostics.IsDefaultOrEmpty)
        {
            return result;
        }

        throw new GeneratorTestException(
            $"Expected no generator diagnostics but found {result.Diagnostics.Length}:{Environment.NewLine}{Describe(result)}"
        );
    }

    /// <summary>Asserts exactly one generator diagnostic with <paramref name="id"/> matches every given filter.</summary>
    /// <param name="result">The harness result.</param>
    /// <param name="id">The expected diagnostic id.</param>
    /// <param name="severity">The expected severity, or null to accept any.</param>
    /// <param name="line">The expected 1-based line, or null to accept any.</param>
    /// <param name="column">The expected 1-based column, or null to accept any.</param>
    /// <param name="file">The expected file path of the location, or null to accept any.</param>
    /// <param name="messageContains">A substring the message must contain, or null to accept any.</param>
    /// <returns>The matching diagnostic.</returns>
    public static Diagnostic AssertDiagnostic(
        this GeneratorHarnessResult result,
        string id,
        DiagnosticSeverity? severity = null,
        int? line = null,
        int? column = null,
        string? file = null,
        string? messageContains = null
    )
    {
        var matches = result
            .Diagnostics.Where(d =>
                d.Id == id
                && (severity is null || d.Severity == severity)
                && (
                    messageContains is null
                    || d.GetMessage().Contains(messageContains, StringComparison.Ordinal)
                )
                && LocationMatches(d, line, column, file)
            )
            .ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        var expected = new StringBuilder(id);
        Append(expected, "severity", severity);
        Append(expected, "line", line);
        Append(expected, "column", column);
        Append(expected, "file", file);
        Append(expected, "message containing", messageContains);

        var problem =
            matches.Count == 0 ? "no match" : $"{matches.Count} matches, expected exactly one";
        throw new GeneratorTestException(
            $"Expected one generator diagnostic [{expected}] but found {problem}. Actual diagnostics:{Environment.NewLine}{Describe(result)}"
        );
    }

    /// <summary>Asserts the generated source <paramref name="hintName"/> equals <paramref name="expected"/>, ignoring line-ending style only.</summary>
    /// <param name="result">The harness result.</param>
    /// <param name="hintName">The hint name of the generated source.</param>
    /// <param name="expected">The expected text.</param>
    /// <returns>The same result, for chaining.</returns>
    public static GeneratorHarnessResult AssertSource(
        this GeneratorHarnessResult result,
        string hintName,
        string expected
    )
    {
        var actual = GetSource(result, hintName);
        if (Normalize(actual) != Normalize(expected))
        {
            throw new GeneratorTestException(Differences(hintName, expected, actual));
        }

        return result;
    }

    /// <summary>
    /// Asserts the generated source <paramref name="hintName"/> equals the content of the file at <paramref name="path"/>,
    /// ignoring line-ending style only. A missing file, or the environment variable <c>HAITCH_ACCEPT=1</c>, writes the
    /// actual output to the file and still fails, so an accepted change is reviewed in version control and a rerun passes.
    /// A relative path needs the calling source file's real location: when the build remaps source paths
    /// (<c>ContinuousIntegrationBuild</c>, <c>PathMap</c>, so the caller path starts with <c>/_/</c> or names a directory
    /// that does not exist) a <see cref="GeneratorTestException"/> asks for an absolute path instead.
    /// </summary>
    /// <param name="result">The harness result.</param>
    /// <param name="hintName">The hint name of the generated source.</param>
    /// <param name="path">An absolute path, or a path relative to the directory of the calling source file; pass an absolute path when source paths are remapped.</param>
    /// <param name="callerFilePath">Filled in by the compiler; do not pass.</param>
    /// <returns>The same result, for chaining.</returns>
    public static GeneratorHarnessResult AssertSourceFile(
        this GeneratorHarnessResult result,
        string hintName,
        string path,
        [CallerFilePath] string callerFilePath = ""
    )
    {
        var actual = GetSource(result, hintName);
        var directory = Path.GetDirectoryName(callerFilePath) ?? "";
        if (!Path.IsPathRooted(path) && !IsRealDirectory(directory))
        {
            throw new GeneratorTestException(
                $"Cannot resolve the relative path '{path}': the caller path '{callerFilePath}' was remapped by the build (ContinuousIntegrationBuild or PathMap) and is not a real directory. Pass an absolute path instead."
            );
        }

        var fullPath = Path.GetFullPath(Path.Combine(directory, path));

        var accept = Environment.GetEnvironmentVariable(AcceptVariable) == "1";
        if (accept || !File.Exists(fullPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, actual);
            throw new GeneratorTestException(
                accept
                    ? $"{AcceptVariable}=1: wrote generated source '{hintName}' to {fullPath}. Review the change and rerun without it."
                    : $"Expected file {fullPath} did not exist; wrote generated source '{hintName}' to it. Review it and rerun."
            );
        }

        var expected = File.ReadAllText(fullPath);
        if (Normalize(actual) != Normalize(expected))
        {
            throw new GeneratorTestException(
                $"{Differences(hintName, expected, actual)}{Environment.NewLine}Expected file: {fullPath} (set {AcceptVariable}=1 to overwrite it)"
            );
        }

        return result;
    }

    private const string AcceptVariable = "HAITCH_ACCEPT";

    // A deterministic build rewrites the repo root to "/_/", which can exist as a rooted path on Unix without being real.
    private static bool IsRealDirectory(string directory) =>
        directory.Length > 0
        && Path.IsPathRooted(directory)
        && !(directory.Replace('\\', '/') + "/").StartsWith("/_/", StringComparison.Ordinal)
        && Directory.Exists(directory);

    private static string GetSource(GeneratorHarnessResult result, string hintName)
    {
        if (result.Sources.TryGetValue(hintName, out var source))
        {
            return source;
        }

        var names = result.Sources.Count == 0 ? "(none)" : string.Join(", ", result.Sources.Keys);
        throw new GeneratorTestException(
            $"No generated source named '{hintName}'. Actual hint names: {names}"
        );
    }

    // Only line endings are normalized: trailing whitespace is part of what a generator emits.
    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static string Differences(string hintName, string expected, string actual)
    {
        var expectedLines = Normalize(expected).Split('\n');
        var actualLines = Normalize(actual).Split('\n');
        var index = 0;
        while (
            index < expectedLines.Length
            && index < actualLines.Length
            && expectedLines[index] == actualLines[index]
        )
        {
            index++;
        }

        var expectedLine = index < expectedLines.Length ? expectedLines[index] : "<end of text>";
        var actualLine = index < actualLines.Length ? actualLines[index] : "<end of text>";
        return $"Generated source '{hintName}' differs from expected at line {index + 1}.{Environment.NewLine}  expected: {expectedLine}{Environment.NewLine}  actual:   {actualLine}";
    }

    private static bool LocationMatches(Diagnostic diagnostic, int? line, int? column, string? file)
    {
        if (line is null && column is null && file is null)
        {
            return true;
        }

        var span = diagnostic.Location.GetLineSpan();
        return (line is null || span.StartLinePosition.Line + 1 == line)
            && (column is null || span.StartLinePosition.Character + 1 == column)
            && (file is null || span.Path == file);
    }

    private static void Append(StringBuilder builder, string name, object? value)
    {
        if (value is not null)
        {
            builder.Append(", ").Append(name).Append(' ').Append(value);
        }
    }

    private static string Describe(GeneratorHarnessResult result)
    {
        if (result.Diagnostics.IsDefaultOrEmpty)
        {
            return "  (none)";
        }

        return string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(d =>
            {
                var span = d.Location.GetLineSpan();
                var position =
                    d.Location == Location.None
                        ? "(no location)"
                        : $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})";
                return $"  {d.Id} {d.Severity} {position}: {d.GetMessage()}";
            })
        );
    }
}
