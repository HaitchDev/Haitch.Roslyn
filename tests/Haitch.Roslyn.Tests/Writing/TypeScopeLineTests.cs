using System;
using System.Linq;
using System.Threading.Tasks;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Writing;

public class TypeScopeLineTests
{
    private const string Source = """
        public partial class Outer { public partial class Inner { } }

        public partial class Host { }

        public class EventSource
        {
            public event System.EventHandler? Changed;
        }
        """;

    private static TypeModel Model(string metadataName)
    {
        return TypeModel.From(CompilationHelper.GetNamedTypeSymbol(Source, metadataName));
    }

    private static EventModel Changed()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(Source, "EventSource");

        return EventModel.From(type.GetMembers().OfType<IEventSymbol>().Single());
    }

    private delegate void WriteBody(SourceWriter writer, TypeScope type);

    private static string Render(string metadataName, WriteBody write)
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(Model(metadataName));
            write(writer, type);
        }

        return writer.ToString();
    }

    [Test]
    public async Task Should_write_a_line_at_the_body_indent_of_a_nested_type()
    {
        string text = Render("Outer+Inner", (_, type) => type.Line("// note"));

        await Assert.That(text).Contains("        // note\n");
        await Assert.That(text).DoesNotContain("            // note");
    }

    [Test]
    public async Task Should_write_an_empty_line_with_no_argument()
    {
        string text = Render("Host", (_, type) => type.Line("// a").Line().Line("// b"));

        await Assert.That(text).Contains("    // a\n\n    // b\n");
    }

    [Test]
    public async Task Should_match_a_direct_writer_call_between_members()
    {
        string viaScope = Render(
            "Host",
            (_, type) =>
            {
                type.Event(Changed());
                type.Line("#pragma warning disable CS0067");
                type.Event(Changed());
                type.Line("#pragma warning restore CS0067");
                type.Event(Changed());
            }
        );
        string viaWriter = Render(
            "Host",
            (writer, type) =>
            {
                type.Event(Changed());
                writer.WriteLine("#pragma warning disable CS0067");
                type.Event(Changed());
                writer.WriteLine("#pragma warning restore CS0067");
                type.Event(Changed());
            }
        );

        await Assert.That(viaScope).Contains("#pragma warning disable CS0067");
        await Assert.That(viaScope).IsEqualTo(viaWriter);
    }

    [Test]
    public async Task Should_match_a_direct_writer_call_with_a_buffered_attribute()
    {
        AttributeModel obsolete = AttributeModel.From(
            CompilationHelper
                .GetNamedTypeSymbol("[System.Obsolete] public class A { }", "A")
                .GetAttributes()
                .Single()
        )!;

        string viaScope = Render(
            "Host",
            (_, type) =>
            {
                type.Attribute(obsolete);
                type.Line("// between");
                type.Event(Changed());
            }
        );
        string viaWriter = Render(
            "Host",
            (writer, type) =>
            {
                type.Attribute(obsolete);
                writer.WriteLine("// between");
                type.Event(Changed());
            }
        );

        await Assert.That(viaScope).IsEqualTo(viaWriter);
        await Assert.That(viaScope).Contains("// between\n    [global::System.ObsoleteAttribute]");
    }

    [Test]
    public async Task Should_write_an_empty_line_with_no_argument_in_a_method_body()
    {
        string text = Render(
            "Host",
            (_, type) =>
            {
                using var method = type.Method(RunModel());
                method.Line("a();").Line().Line("b();");
            }
        );

        await Assert.That(text).Contains("a();\n\n");
    }

    [Test]
    public async Task Should_write_a_line_through_a_child_type_scope_at_its_indent()
    {
        string text = Render(
            "Outer",
            (_, type) =>
            {
                using var child = type.Type(Model("Outer+Inner"));
                child.Line("// child");
            }
        );

        await Assert.That(text).Contains("        // child\n");
        await Assert.That(text).DoesNotContain("            // child");
    }

    [Test]
    public async Task Should_throw_while_a_nested_block_is_open()
    {
        InvalidOperationException? thrown = null;

        string text = Render(
            "Host",
            (_, type) =>
            {
                using var method = type.Method(RunModel());

                try
                {
                    type.Line("// x");
                }
                catch (InvalidOperationException e)
                {
                    thrown = e;
                }
            }
        );

        await Assert.That(thrown).IsNotNull();
        await Assert.That(text).DoesNotContain("// x");
    }

    private static MethodModel RunModel()
    {
        return MethodModel.From(
            CompilationHelper
                .GetNamedTypeSymbol("public class M { public void Run() { } }", "M")
                .GetMembers("Run")
                .OfType<IMethodSymbol>()
                .Single()
        );
    }

    [Test]
    public async Task Should_compile_without_warnings_with_pragmas_around_an_unraised_event()
    {
        string text = Render(
            "Host",
            (_, type) =>
            {
                type.Line("#pragma warning disable CS0067");
                type.Event(Changed());
                type.Line("#pragma warning restore CS0067");
            }
        );
        string unguarded = Render("Host", (_, type) => type.Event(Changed()));

        var withPragmas = CompilationHelper.Compile(text).GetDiagnostics();
        var without = CompilationHelper.Compile(unguarded).GetDiagnostics();

        await Assert.That(without.Any(d => d.Id == "CS0067")).IsTrue();
        await Assert.That(withPragmas.Select(d => d.ToString())).IsEmpty();
        await Assert.That(text).Contains("#nullable enable");
    }
}
