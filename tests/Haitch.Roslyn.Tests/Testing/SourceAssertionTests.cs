using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Testing;

// Tests that touch HAITCH_ACCEPT share one NotInParallel key with the demo test, so the variable
// never leaks into a concurrently running AssertSourceFile call.
public class SourceAssertionTests
{
    private const string Output = "// one\n// two\nclass Out { }\n";

    private sealed class FixedGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(
                context.CompilationProvider,
                static (spc, _) =>
                {
                    spc.AddSource("Out.g.cs", Output);
                    spc.AddSource("Second.g.cs", "class Second { }\n");
                }
            );
    }

    private static GeneratorHarnessResult Run() =>
        GeneratorHarness.Run(new FixedGenerator(), new GeneratorHarnessInput { Sources = [] });

    private static void WithTempDirectory(string? acceptValue, Action<string> body)
    {
        var previous = Environment.GetEnvironmentVariable("HAITCH_ACCEPT");
        var directory = Path.Combine(Path.GetTempPath(), "haitch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Environment.SetEnvironmentVariable("HAITCH_ACCEPT", acceptValue);
            body(directory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HAITCH_ACCEPT", previous);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task AssertSource_matches_inline_and_returns_result()
    {
        var result = Run();

        var returned = result.AssertSource("Out.g.cs", Output);

        await Assert.That(returned).IsSameReferenceAs(result);
    }

    [Test]
    public async Task AssertSource_mismatch_names_first_differing_line_with_both_texts()
    {
        var result = Run();

        var ex = Assert.Throws<GeneratorTestException>(() =>
            result.AssertSource("Out.g.cs", "// one\n// TWO\nclass Out { }\n")
        );

        await Assert.That(ex.Message).Contains("line 2");
        await Assert.That(ex.Message).Contains("expected: // TWO");
        await Assert.That(ex.Message).Contains("actual:   // two");
    }

    [Test]
    public async Task AssertSource_shorter_expected_reports_end_of_text()
    {
        var result = Run();

        var ex = Assert.Throws<GeneratorTestException>(() =>
            result.AssertSource("Out.g.cs", "// one\n// two\n")
        );

        await Assert.That(ex.Message).Contains("line 3");
        await Assert.That(ex.Message).Contains("actual:   class Out { }");
    }

    [Test]
    public async Task AssertSource_unknown_hint_name_lists_actual_names()
    {
        var result = Run();

        var ex = Assert.Throws<GeneratorTestException>(() =>
            result.AssertSource("Nope.g.cs", Output)
        );

        await Assert.That(ex.Message).Contains("Nope.g.cs");
        await Assert.That(ex.Message).Contains("Out.g.cs");
        await Assert.That(ex.Message).Contains("Second.g.cs");
    }

    [Test]
    public async Task AssertSource_ignores_line_ending_style()
    {
        var result = Run();

        result.AssertSource("Out.g.cs", Output.Replace("\n", "\r\n"));

        await Assert.That(result.Sources["Out.g.cs"]).IsEqualTo(Output);
    }

    [Test]
    public async Task AssertSource_counts_trailing_whitespace()
    {
        var result = Run();

        var ex = Assert.Throws<GeneratorTestException>(() =>
            result.AssertSource("Out.g.cs", "// one \n// two\nclass Out { }\n")
        );

        await Assert.That(ex.Message).Contains("line 1");
    }

    [Test]
    [NotInParallel("HAITCH_ACCEPT")]
    public async Task AssertSourceFile_matches_file_with_crlf()
    {
        var result = Run();
        var returned = (GeneratorHarnessResult?)null;
        WithTempDirectory(
            null,
            directory =>
            {
                var file = Path.Combine(directory, "Out.txt");
                File.WriteAllText(file, Output.Replace("\n", "\r\n"));

                returned = result.AssertSourceFile("Out.g.cs", file);
            }
        );

        await Assert.That(returned).IsSameReferenceAs(result);
    }

    [Test]
    [NotInParallel("HAITCH_ACCEPT")]
    public async Task AssertSourceFile_resolves_relative_path_against_caller_directory()
    {
        var result = Run();
        var passed = false;
        WithTempDirectory(
            null,
            directory =>
            {
                Directory.CreateDirectory(Path.Combine(directory, "Expected"));
                File.WriteAllText(Path.Combine(directory, "Expected", "Out.txt"), Output);

                result.AssertSourceFile(
                    "Out.g.cs",
                    "Expected/Out.txt",
                    Path.Combine(directory, "Caller.cs")
                );
                passed = true;
            }
        );

        await Assert.That(passed).IsTrue();
    }

    [Test]
    [NotInParallel("HAITCH_ACCEPT")]
    public async Task AssertSourceFile_mismatch_names_first_differing_line()
    {
        var result = Run();
        string? message = null;
        string? after = null;
        WithTempDirectory(
            null,
            directory =>
            {
                var file = Path.Combine(directory, "Out.txt");
                File.WriteAllText(file, "// one\n// other\nclass Out { }\n");

                message = Assert
                    .Throws<GeneratorTestException>(() => result.AssertSourceFile("Out.g.cs", file))
                    .Message;
                after = File.ReadAllText(file);
            }
        );

        await Assert.That(message).Contains("line 2");
        await Assert.That(message).Contains("expected: // other");
        await Assert.That(message).Contains("actual:   // two");
        await Assert.That(after).IsEqualTo("// one\n// other\nclass Out { }\n");
    }

    [Test]
    [NotInParallel("HAITCH_ACCEPT")]
    public async Task AssertSourceFile_missing_file_is_written_and_fails()
    {
        var result = Run();
        string? message = null;
        string? written = null;
        WithTempDirectory(
            null,
            directory =>
            {
                var file = Path.Combine(directory, "Sub", "Out.txt");

                message = Assert
                    .Throws<GeneratorTestException>(() => result.AssertSourceFile("Out.g.cs", file))
                    .Message;
                written = File.ReadAllText(file);
            }
        );

        await Assert.That(message).Contains("Out.txt");
        await Assert.That(written).IsEqualTo(Output);
    }

    [Test]
    [NotInParallel("HAITCH_ACCEPT")]
    public async Task AssertSourceFile_accept_mode_overwrites_matching_file_and_fails()
    {
        var result = Run();
        string? message = null;
        string? written = null;
        WithTempDirectory(
            "1",
            directory =>
            {
                var file = Path.Combine(directory, "Out.txt");
                File.WriteAllText(file, Output);

                message = Assert
                    .Throws<GeneratorTestException>(() => result.AssertSourceFile("Out.g.cs", file))
                    .Message;
                written = File.ReadAllText(file);
            }
        );

        await Assert.That(message).Contains("HAITCH_ACCEPT");
        await Assert.That(written).IsEqualTo(Output);
    }

    [Test]
    [NotInParallel("HAITCH_ACCEPT")]
    public async Task AssertSourceFile_accept_mode_replaces_stale_content()
    {
        var result = Run();
        string? written = null;
        WithTempDirectory(
            "1",
            directory =>
            {
                var file = Path.Combine(directory, "Out.txt");
                File.WriteAllText(file, "stale");

                Assert.Throws<GeneratorTestException>(() =>
                    result.AssertSourceFile("Out.g.cs", file)
                );
                written = File.ReadAllText(file);
            }
        );

        await Assert.That(written).IsEqualTo(Output);
    }

    [Test]
    [NotInParallel("HAITCH_ACCEPT")]
    public async Task AssertSourceFile_unknown_hint_name_writes_nothing()
    {
        var result = Run();
        var exists = true;
        string? message = null;
        WithTempDirectory(
            "1",
            directory =>
            {
                var file = Path.Combine(directory, "Out.txt");

                message = Assert
                    .Throws<GeneratorTestException>(() =>
                        result.AssertSourceFile("Nope.g.cs", file)
                    )
                    .Message;
                exists = File.Exists(file);
            }
        );

        await Assert.That(message).Contains("Out.g.cs");
        await Assert.That(exists).IsFalse();
    }
}
