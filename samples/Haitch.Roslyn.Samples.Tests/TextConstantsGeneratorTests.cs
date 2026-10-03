using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Samples.Tests;

public class TextConstantsGeneratorTests
{
    private const string Step = "TextConstantsGenerator.Files";

    private static readonly string[] Sources = ["class Input { }"];

    private static GeneratorHarnessInput Input(
        IReadOnlyList<HarnessAdditionalText> texts,
        string? rootNamespace = "App",
        Dictionary<string, IReadOnlyDictionary<string, string>>? perFile = null
    ) =>
        new()
        {
            Sources = Sources,
            AdditionalTexts = texts,
            GlobalOptions = rootNamespace is null
                ? null
                : new Dictionary<string, string>
                {
                    ["build_property.RootNamespace"] = rootNamespace,
                },
            PerFileOptions = perFile,
        };

    private static GeneratorHarnessResult Run(
        IReadOnlyList<HarnessAdditionalText> texts,
        string? rootNamespace = "App",
        Dictionary<string, IReadOnlyDictionary<string, string>>? perFile = null
    ) => GeneratorHarness.Run(new TextConstantsGenerator(), Input(texts, rootNamespace, perFile));

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Names(
        string path,
        string name
    ) =>
        new()
        {
            [path] = new Dictionary<string, string>
            {
                ["build_metadata.AdditionalFiles.ConstantName"] = name,
            },
        };

    private static string? ConstantOf(GeneratorHarnessResult result, string type, string name)
    {
        var symbol = result.Compilation.GetTypeByMetadataName(type);
        var field = symbol?.GetMembers(name).OfType<IFieldSymbol>().SingleOrDefault();
        return field?.ConstantValue as string;
    }

    [Test]
    public async Task Two_files_become_two_constants()
    {
        var result = Run([new("docs/greeting.txt", "Hello"), new("docs/farewell.txt", "Goodbye")]);

        result
            .AssertNoDiagnostics()
            .AssertSourceFile("TextConstants.g.cs", "Expected/TextConstants.Two.g.cs.txt");

        await Assert
            .That(
                result
                    .Compilation.GetDiagnostics()
                    .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            )
            .IsEmpty();
        await Assert.That(ConstantOf(result, "App.TextConstants", "Greeting")).IsEqualTo("Hello");
        await Assert.That(ConstantOf(result, "App.TextConstants", "Farewell")).IsEqualTo("Goodbye");
    }

    [Test]
    public async Task ConstantName_metadata_overrides_the_derived_name()
    {
        var result = Run([new("greeting.txt", "Hello")], perFile: Names("greeting.txt", "Welcome"));

        await Assert.That(ConstantOf(result, "App.TextConstants", "Welcome")).IsEqualTo("Hello");
        await Assert.That(ConstantOf(result, "App.TextConstants", "Greeting")).IsNull();
    }

    [Test]
    public async Task Missing_RootNamespace_falls_back_to_a_fixed_namespace()
    {
        var result = Run([new("greeting.txt", "Hello")], rootNamespace: null);

        result.AssertNoDiagnostics();
        await Assert.That(result.Sources["TextConstants.g.cs"]).Contains("namespace GeneratedText");
        await Assert
            .That(ConstantOf(result, "GeneratedText.TextConstants", "Greeting"))
            .IsEqualTo("Hello");
    }

    [Test]
    public async Task Quotes_and_newlines_round_trip()
    {
        const string content = "He said \"hi\"\r\nline two\n\ttabbed \\ backslash {x} é\0";

        var result = Run([new("quoted.txt", content)]);

        result.AssertNoDiagnostics();
        await Assert.That(ConstantOf(result, "App.TextConstants", "Quoted")).IsEqualTo(content);
        await Assert
            .That(
                result
                    .Compilation.GetDiagnostics()
                    .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            )
            .IsEmpty();
    }

    [Test]
    public async Task Non_txt_files_are_ignored()
    {
        var result = Run([new("a.txt", "A"), new("b.json", "{}")]);

        await Assert.That(result.Sources["TextConstants.g.cs"]).DoesNotContain("json");
        await Assert.That(ConstantOf(result, "App.TextConstants", "B")).IsNull();
    }

    [Test]
    public async Task An_unreadable_file_reports_one_diagnostic()
    {
        var result = GeneratorHarness.Run(
            new TextConstantsGenerator(),
            new GeneratorHarnessInput
            {
                Sources = Sources,
                AdditionalTexts = [new UnreadableAdditionalText("broken.txt")],
            }
        );

        await Assert.That(result.Diagnostics).HasSingleItem();
        result.AssertDiagnostic("TEXTCONST001", messageContains: "broken.txt", file: "broken.txt");
    }

    [Test]
    public async Task Two_files_with_the_same_constant_name_report_the_second()
    {
        var result = Run([new("a/note.txt", "A"), new("b/note.txt", "B")]);

        var diagnostic = result.AssertDiagnostic("TEXTCONST003", messageContains: "Note");
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo("b/note.txt");
        await Assert.That(ConstantOf(result, "App.TextConstants", "Note")).IsEqualTo("A");
    }

    [Test]
    public async Task An_invalid_ConstantName_is_reported_and_the_file_skipped()
    {
        var result = Run(
            [new("a.txt", "A"), new("b.txt", "B")],
            perFile: Names("a.txt", "not valid")
        );

        result.AssertDiagnostic("TEXTCONST002", messageContains: "not valid");
        await Assert.That(ConstantOf(result, "App.TextConstants", "B")).IsEqualTo("B");
    }

    // Options are included so the rerun with a new, equal options provider is exercised for both
    // global and per-file values.
    [Test]
    public async Task Output_is_cacheable()
    {
        var result = GeneratorHarness.AssertCacheable(
            new TextConstantsGenerator(),
            Input(
                [new HarnessAdditionalText("a.txt", "A"), new HarnessAdditionalText("b.txt", "B")],
                perFile: Names("a.txt", "Welcome")
            ),
            [Step]
        );

        await Assert.That(result.Sources["TextConstants.g.cs"]).Contains("Welcome");
    }

    [Test]
    [Arguments("to-string.txt", "ToString")]
    [Arguments("equals.txt", "Equals")]
    [Arguments("get-hash-code.txt", "GetHashCode")]
    [Arguments("get-type.txt", "GetType")]
    [Arguments("reference-equals.txt", "ReferenceEquals")]
    [Arguments("memberwise-clone.txt", "MemberwiseClone")]
    [Arguments("finalize.txt", "Finalize")]
    public void Names_inherited_from_object_are_reported_from_a_file_name_and_from_ConstantName(
        string path,
        string name
    )
    {
        Run([new(path, "A")]).AssertDiagnostic("TEXTCONST002", messageContains: name);
        Run([new("a.txt", "A")], perFile: Names("a.txt", name))
            .AssertDiagnostic("TEXTCONST002", messageContains: name);
    }

    [Test]
    public async Task An_invalid_RootNamespace_falls_back_and_reports_TEXTCONST004()
    {
        var result = Run([new("greeting.txt", "Hello")], rootNamespace: "1Bad");

        result.AssertDiagnostic("TEXTCONST004", messageContains: "1Bad");
        await Assert
            .That(ConstantOf(result, "GeneratedText.TextConstants", "Greeting"))
            .IsEqualTo("Hello");
    }

    [Test]
    public void A_keyword_ConstantName_is_reported() =>
        Run([new("a.txt", "A")], perFile: Names("a.txt", "class"))
            .AssertDiagnostic("TEXTCONST002", messageContains: "class");

    [Test]
    public void A_constant_named_like_its_class_is_reported() =>
        Run([new("text-constants.txt", "A")])
            .AssertDiagnostic("TEXTCONST002", messageContains: "TextConstants");

    [Test]
    public void A_name_without_letters_or_digits_is_reported() =>
        Run([new("---.txt", "A")]).AssertDiagnostic("TEXTCONST002", messageContains: "---.txt");

    [Test]
    [Arguments("greeting.txt", "Greeting")]
    [Arguments("docs/my-file name.txt", "MyFileName")]
    [Arguments("docs\\snake_case.txt", "SnakeCase")]
    [Arguments("2fast.txt", "_2Fast")]
    [Arguments("v2beta.txt", "V2Beta")]
    [Arguments("notes.v2.txt", "NotesV2")]
    [Arguments("noextension", "Noextension")]
    [Arguments("---.txt", "")]
    public async Task ConstantName_derives_a_PascalCase_identifier(string path, string expected) =>
        await Assert.That(TextConstantsGenerator.ConstantName(path)).IsEqualTo(expected);
}
