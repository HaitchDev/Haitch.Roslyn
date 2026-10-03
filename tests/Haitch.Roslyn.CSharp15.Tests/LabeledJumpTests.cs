using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.CSharp15.Tests;

public class LabeledJumpTests
{
    private const string Prefix = "public class C\n{\n    public void M(int[] xs, int n)\n    {\n";
    private const string Suffix = "    }\n}\n";

    private static readonly MetadataReference[] References = CreateReferences();

    [Test]
    public async Task For_LabeledLoopWithNestedJumps_WritesLabelBeforeStatementAndCompiles()
    {
        var output = Render(body =>
        {
            using var outer = body.For("int i = 0", "i < 3", "i++", label: "outer");
            using var inner = outer.ForEach("int", "x", "xs", label: "inner");
            using (var skip = inner.If("x == i"))
            {
                skip.Continue("outer");
            }

            using (var stop = inner.If("x > 9"))
            {
                stop.Break("outer");
            }

            inner.Break("inner");
        });

        await Assert
            .That(output)
            .IsEqualTo(
                Prefix
                + "        outer:\n"
                + "        for (int i = 0; i < 3; i++)\n"
                + "        {\n"
                + "            inner:\n"
                + "            foreach (int x in xs)\n"
                + "            {\n"
                + "                if (x == i)\n"
                + "                {\n"
                + "                    continue outer;\n"
                + "                }\n"
                + "                if (x > 9)\n"
                + "                {\n"
                + "                    break outer;\n"
                + "                }\n"
                + "                break inner;\n"
                + "            }\n"
                + "        }\n"
                + Suffix
            );
        await AssertCompiles(output);
    }

    [Test]
    public async Task While_LabeledLoop_ContinueAndBreakCompile()
    {
        var output = Render(body =>
        {
            using var loop = body.While("n > 0", label: "again");
            loop.Line("n--;");
            using (var branch = loop.If("n == 5"))
            {
                branch.Continue("again");
            }

            loop.Break("again");
        });

        await Assert.That(output).Contains("        again:\n        while (n > 0)\n");
        await Assert.That(output).Contains("continue again;");
        await Assert.That(output).Contains("break again;");
        await AssertCompiles(output);
    }

    [Test]
    public async Task Switch_Labeled_BreakLabelCompiles()
    {
        var output = Render(body =>
        {
            using var sw = body.Switch("n", label: "sw");
            using (var one = sw.Case("1"))
            {
                one.Break("sw");
            }

            using var other = sw.Default();
            other.Break();
        });

        await Assert
            .That(output)
            .IsEqualTo(
                Prefix
                + "        sw:\n"
                + "        switch (n)\n"
                + "        {\n"
                + "            case 1:\n"
                + "            {\n"
                + "                break sw;\n"
                + "            }\n"
                + "            default:\n"
                + "            {\n"
                + "                break;\n"
                + "            }\n"
                + "        }\n"
                + Suffix
            );
        await AssertCompiles(output);
    }

    [Test]
    public async Task Jumps_WithoutLabel_WritePlainStatements()
    {
        var output = Render(body =>
        {
            using var loop = body.While("true");
            loop.Continue();
            loop.Break();
        });

        await Assert.That(output).Contains("continue;\n");
        await Assert.That(output).Contains("break;\n");
        await Assert.That(output).DoesNotContain(":");
    }

    [Test]
    public async Task Jumps_FromInsideTryAndIf_CompileAgainstOuterLabel()
    {
        var output = Render(body =>
        {
            using var loop = body.While("n > 0", label: "work");
            using var attempt = loop.Try();
            using (var branch = attempt.If("n == 3"))
            {
                branch.Break("work");
            }

            attempt.Continue("work");
            using var handler = attempt.Catch();
            handler.Break("work");
        });

        await AssertCompiles(output);
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("1abc")]
    [Arguments("a b")]
    [Arguments("a-b")]
    [Arguments("a;")]
    [Arguments("for")]
    [Arguments("class")]
    [Arguments("@")]
    [Arguments("@@for")]
    [Arguments("@1a")]
    public async Task Label_NotAnIdentifier_ThrowsWithoutWriting(string label)
    {
        await AssertThrowsWithoutWriting(body => body.ForEach("int", "x", "xs", label: label), "");
        await AssertThrowsWithoutWriting(body => body.For("", "", "", label: label), "");
        await AssertThrowsWithoutWriting(body => body.While("true", label: label), "");
        await AssertThrowsWithoutWriting(body => body.Switch("n", label: label), "");
    }

    [Test]
    [Arguments("@for")]
    [Arguments("@class")]
    [Arguments("var")]
    [Arguments("closed")]
    [Arguments("_x1")]
    public async Task Label_EscapedKeywordOrContextualKeyword_WritesAndCompiles(string label)
    {
        var output = Render(body =>
        {
            using var loop = body.While("n > 0", label: label);
            loop.Break(label);
        });

        await Assert.That(output).Contains($"        {label}:\n        while (n > 0)\n");
        await AssertCompiles(output);
    }

    [Test]
    public async Task Label_AlreadyOpen_ThrowsWithoutWriting()
    {
        await AssertThrowsWithoutWriting(
            body =>
            {
                using var outer = body.While("true", label: "dup");
                using var inner = outer.While("true", label: "dup");
            },
            "        dup:\n        while (true)\n        {\n        }\n"
        );
    }

    [Test]
    public async Task Jump_ToLabelThatIsNotOpen_ThrowsWithoutWriting()
    {
        await AssertThrowsWithoutWriting(body => body.Break("nowhere"), "");
        await AssertThrowsWithoutWriting(body => body.Continue("nowhere"), "");
    }

    [Test]
    public async Task Jump_ToLabelAfterItsStatementClosed_ThrowsWithoutWriting()
    {
        await AssertThrowsWithoutWriting(
            body =>
            {
                using (var loop = body.While("true", label: "done"))
                {
                    loop.Break("done");
                }

                body.Break("done");
            },
            "        done:\n        while (true)\n        {\n            break done;\n        }\n"
        );
    }

    [Test]
    public async Task Continue_NamingSwitch_ThrowsWithoutWriting()
    {
        await AssertThrowsWithoutWriting(
            body =>
            {
                using var loop = body.While("true");
                using var sw = loop.Switch("n", label: "sw");
                using var section = sw.Default();

                // Ends the section first so the empty-section check stays quiet and only the rejected call is under test.
                section.Break();
                section.Continue("sw");
            },
            "        while (true)\n        {\n            sw:\n            switch (n)\n            {\n"
            + "                default:\n                {\n                    break;\n                }\n            }\n        }\n"
        );
    }

    private static async Task AssertThrowsWithoutWriting(Action<BodyScope> act, string expectedBody)
    {
        var writer = new SourceWriter();
        var written = "";

        await Assert
            .That(() =>
            {
                try
                {
                    Render(writer, scope => act(scope));
                }
                finally
                {
                    // A close-time error recorded during unwinding would replace the exception under test.
                    written = writer.ToString();
                }
            })
            .Throws<ArgumentException>();

        // Only the statements before the rejected call (plus the scopes' closing braces) may be present.
        await Assert.That(written).IsEqualTo(Prefix + expectedBody + Suffix);
    }

    private static string Render(Action<BodyScope> body)
    {
        var writer = new SourceWriter();
        Render(writer, body);

        return writer.ToString();
    }

    private static void Render(SourceWriter writer, Action<BodyScope> body)
    {
        writer.WriteLine("public class C");
        using var type = writer.Block();
        writer.WriteLine("public void M(int[] xs, int n)");
        using var scope = new BodyScope(writer, writer.Block());
        body(scope);
    }

    private static async Task AssertCompiles(string source)
    {
        var compilation = CSharpCompilation.Create(
            "LabeledJumpTests" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText("#nullable enable\n" + source, CSharp15.ParseOptions)],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        var problems = compilation
            .GetDiagnostics()
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToArray();

        await Assert.That(problems).IsEmpty();
    }

    private static MetadataReference[] CreateReferences()
    {
        var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
