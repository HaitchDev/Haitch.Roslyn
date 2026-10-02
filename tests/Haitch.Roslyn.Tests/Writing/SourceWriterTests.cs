using System.Text;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis.Text;

namespace Haitch.Roslyn.Tests.Writing;

public class SourceWriterTests
{
    [Test]
    public async Task Should_indent_nested_blocks()
    {
        SourceWriter writer = new();

        writer.WriteLine("namespace Foo");
        using (writer.Block())
        {
            writer.WriteLine("public class Bar");
            using (writer.Block())
            {
                writer.WriteLine("public int X;");
            }
        }

        string expected =
            """
            namespace Foo
            {
                public class Bar
                {
                    public int X;
                }
            }

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_reindent_multiline_text_at_the_current_indent_level()
    {
        SourceWriter writer = new();

        writer.WriteLine("public class Bar");
        using (writer.Block())
        {
            writer.WriteLine("public int X;\npublic int Y;");
        }

        string expected =
            """
            public class Bar
            {
                public int X;
                public int Y;
            }

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_not_indent_blank_lines()
    {
        SourceWriter writer = new();

        writer.WriteLine("public class Bar");
        using (writer.Block())
        {
            writer.WriteLine("public int X;");
            writer.WriteLine();
            writer.WriteLine("public int Y;");
        }

        string expected =
            """
            public class Bar
            {
                public int X;

                public int Y;
            }

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_not_indent_blank_lines_embedded_in_multiline_text()
    {
        SourceWriter writer = new();

        writer.WriteLine("public class Bar");
        using (writer.Block())
        {
            writer.WriteLine("public int X;\n\npublic int Y;");
        }

        string expected =
            """
            public class Bar
            {
                public int X;

                public int Y;
            }

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_use_custom_block_delimiters()
    {
        SourceWriter writer = new();

        writer.WriteLine("private static readonly int[] Values =");
        using (writer.Block("{", "};"))
        {
            writer.WriteLine("1,");
            writer.WriteLine("2,");
        }

        string expected =
            """
            private static readonly int[] Values =
            {
                1,
                2,
            };

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_only_use_lf_line_endings()
    {
        SourceWriter writer = new();

        writer.WriteLine("public class Bar");
        using (writer.Block())
        {
            writer.WriteLine("public int X;");
        }

        await Assert.That(writer.ToString().Contains('\r')).IsFalse();
    }

    [Test]
    public async Task Should_convert_to_source_text_with_utf8_encoding()
    {
        SourceWriter writer = new();
        writer.WriteLine("public class Bar");

        SourceText sourceText = writer.ToSourceText();

        await Assert.That(sourceText.Encoding).IsEqualTo(Encoding.UTF8);
        await Assert.That(sourceText.ToString()).IsEqualTo(writer.ToString());
    }

    [Test]
    public async Task Should_use_sha256_checksum_algorithm_when_converting_to_source_text()
    {
        SourceWriter writer = new();
        writer.WriteLine("public class Bar");

        SourceText sourceText = writer.ToSourceText();

        await Assert.That(sourceText.ChecksumAlgorithm).IsEqualTo(SourceHashAlgorithm.Sha256);
    }

    [Test]
    public async Task Should_only_indent_once_when_writing_partial_line_pieces()
    {
        SourceWriter writer = new();

        writer.WriteLine("public class Bar");
        using (writer.Block())
        {
            writer.Write("public ");
            writer.Write("void Foo()");
            writer.WriteLine();
        }

        string expected =
            """
            public class Bar
            {
                public void Foo()
            }

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_normalize_crlf_line_endings_to_lf()
    {
        SourceWriter writer = new();

        writer.WriteLine("public class Bar");
        using (writer.Block())
        {
            writer.WriteLine("a;\r\nb;");
        }

        string expected =
            """
            public class Bar
            {
                a;
                b;
            }

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
        await Assert.That(writer.ToString().Contains('\r')).IsFalse();
    }

    [Test]
    public async Task Should_not_emit_a_blank_line_for_a_trailing_newline()
    {
        SourceWriter writer = new();

        writer.WriteLine("public class Bar");
        using (writer.Block())
        {
            writer.WriteLine("a;\n");
            writer.WriteLine("b;");
        }

        string expected =
            """
            public class Bar
            {
                a;
                b;
            }

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_not_emit_a_blank_line_for_a_trailing_crlf()
    {
        SourceWriter writer = new();

        writer.WriteLine("public class Bar");
        using (writer.Block())
        {
            writer.WriteLine("a;\r\n");
            writer.WriteLine("b;");
        }

        string expected =
            """
            public class Bar
            {
                a;
                b;
            }

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_make_disposing_a_block_scope_twice_a_no_op()
    {
        SourceWriter writer = new();

        writer.WriteLine("public class Bar");
        SourceWriter.BlockScope scope = writer.Block();
        writer.WriteLine("public int X;");
        scope.Dispose();
        scope.Dispose();

        string expected =
            """
            public class Bar
            {
                public int X;
            }

            """;

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_make_disposing_the_default_block_scope_a_no_op()
    {
        SourceWriter.BlockScope scope = default;

        scope.Dispose();

        await Assert.That(scope).IsEqualTo(default(SourceWriter.BlockScope));
    }
}