using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Writing.Scopes;

public partial class TypeScopeTests
{
    private static string WriteSummaryAboveMethod(string text)
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(Plain("A"));

            type.Summary(text);

            using (type.Block("public void M()")) { }
        }

        return writer.ToString();
    }

    [Test]
    public async Task Should_escape_xml_markup_characters_in_a_summary()
    {
        string source = WriteSummaryAboveMethod("a < b > c & d");

        await Assert
            .That(source)
            .Contains(
                """
                    /// <summary>a &lt; b &gt; c &amp; d</summary>
                    public void M()
                """
            );
    }

    [Test]
    [Arguments("First.\nSecond.\n\nFourth.")]
    [Arguments("First.\r\nSecond.\r\n\r\nFourth.")]
    [Arguments("First.\x2028Second.\x2028\x2028Fourth.")]
    public async Task Should_write_one_doc_comment_line_per_line_of_a_multi_line_summary(
        string text
    )
    {
        string source = WriteSummaryAboveMethod(text);

        await Assert
            .That(source)
            .Contains(
                """
                    /// <summary>
                    /// First.
                    /// Second.
                    ///
                    /// Fourth.
                    /// </summary>
                    public void M()
                """
            );
    }

    [Test]
    public async Task Should_write_a_character_xml_cannot_hold_as_its_unicode_escape_in_a_summary()
    {
        string source = WriteSummaryAboveMethod("a\x0001b\x001Fc\td\xFFFEe\xFFFFf\xD800g");

        await Assert
            .That(source)
            .Contains("/// <summary>a\\u0001b\\u001Fc\td\\uFFFEe\\uFFFFf\\uD800g</summary>\n");
    }

    [Test]
    public async Task Should_compile_a_summary_holding_a_newline_and_xml_markup_without_doc_comment_diagnostics()
    {
        string source = WriteSummaryAboveMethod("Fails when a < b\nand b & c.");

        await Assert.That(DocumentationDiagnostics(source)).IsEmpty();
    }

    private static string[] DocumentationDiagnostics(string source)
    {
        CSharpCompilation compilation = CompilationHelper.Compile(source, allowErrors: true);
        SyntaxTree tree = compilation.SyntaxTrees[0];
        SyntaxTree documented = CSharpSyntaxTree.ParseText(
            source,
            ((CSharpParseOptions)tree.Options).WithDocumentationMode(DocumentationMode.Diagnose)
        );

        return compilation
            .ReplaceSyntaxTree(tree, documented)
            .GetDiagnostics()
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
    }

    [Test]
    public async Task Should_write_a_summary_after_the_blank_separator_directly_above_the_header()
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(Plain("A"));

            using (type.Block("public void M()")) { }

            type.Summary("Does N.");

            using (type.Block("public void N()")) { }
        }

        await Assert
            .That(writer.ToString())
            .EndsWith(
                """
                    }

                    /// <summary>Does N.</summary>
                    public void N()
                    {
                    }
                }

                """
            );
    }

    [Test]
    public async Task Should_write_a_summary_above_pending_attributes()
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(Plain("A"));

            type.Attribute(WellKnownAttributes.EditorBrowsableNever);
            type.Summary("Does M.");

            using (type.Block("public void M()")) { }
        }

        await Assert
            .That(writer.ToString())
            .EndsWith(
                """
                {
                    /// <summary>Does M.</summary>
                    [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]
                    public void M()
                    {
                    }
                }

                """
            );
    }

    [Test]
    public async Task Should_not_throw_on_dispose_but_throw_from_to_string_when_a_summary_is_pending()
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(Plain("A"));
            type.Summary("Dangling.");
        }

        await Assert.That(() => writer.ToString()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Should_reject_a_second_summary_before_a_member()
    {
        SourceWriter writer = new();

        await Assert
            .That(() =>
            {
                using var file = writer.File();
                using var type = file.Type(Plain("A"));
                type.Summary("First.");
                type.Summary("Second.");
            })
            .Throws<InvalidOperationException>();
    }
}
