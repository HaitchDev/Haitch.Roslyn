using System.Linq;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Writing;

public class TypeScopeEventTests
{
    private const string EventSource = """
        public interface IHost { event System.EventHandler Changed; }

        public class Source : IHost
        {
            public event System.EventHandler? Changed;
            public static event System.Action? Done;
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            protected virtual event System.EventHandler? Hook;
            [System.Obsolete("old")]
            public event System.EventHandler? Old;
            private System.EventHandler? _h;
            public event System.EventHandler? Custom { add { _h += value; } remove { _h -= value; } }
            event System.EventHandler IHost.Changed { add { } remove { } }
        }
        """;

    private const string SampleSource = "public partial class Sample { }";

    private static EventModel EventFrom(string name)
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(EventSource, "Source");

        return EventModel.From(
            type.GetMembers().OfType<IEventSymbol>().Single(e => e.Name == name)
        );
    }

    private static TypeModel Sample()
    {
        return TypeModel.From(CompilationHelper.GetNamedTypeSymbol(SampleSource, "Sample"));
    }

    private delegate void WriteMembers(TypeScope type);

    private static string Render(WriteMembers write)
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(Sample());
            write(type);
        }

        string text = writer.ToString();
        const string open = "partial class Sample\n{\n";
        int start = text.IndexOf(open, StringComparison.Ordinal) + open.Length;

        return text.Substring(start, text.Length - start - "}\n".Length);
    }

    [Test]
    public async Task Should_render_a_public_instance_event()
    {
        string body = Render(type => type.Event(EventFrom("Changed")));

        await Assert
            .That(body)
            .IsEqualTo("    public event global::System.EventHandler? Changed;\n");
    }

    [Test]
    public async Task Should_render_a_static_event()
    {
        string body = Render(type => type.Event(EventFrom("Done")));

        await Assert.That(body).IsEqualTo("    public static event global::System.Action? Done;\n");
    }

    [Test]
    public async Task Should_render_a_protected_virtual_event()
    {
        string body = Render(type => type.Event(EventFrom("Hook")));

        await Assert
            .That(body)
            .IsEqualTo("    protected virtual event global::System.EventHandler? Hook;\n");
    }

    [Test]
    public async Task Should_render_abstract_override_for_an_abstract_override_event()
    {
        string body = Render(type =>
            type.Event(EventFrom("Changed") with { IsAbstract = true, IsOverride = true })
        );

        await Assert
            .That(body)
            .IsEqualTo(
                "    public abstract override event global::System.EventHandler? Changed;\n"
            );
    }

    [Test]
    public async Task Should_render_override_and_sealed_override_events()
    {
        EventModel baseline = EventFrom("Changed");

        string body = Render(type =>
        {
            type.Event(baseline with { IsOverride = true });
            type.Event(baseline with { IsOverride = true, IsSealed = true });
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public override event global::System.EventHandler? Changed;\n\n"
                    + "    public sealed override event global::System.EventHandler? Changed;\n"
            );
    }

    [Test]
    public async Task Should_render_an_abstract_event()
    {
        string body = Render(type => type.Event(EventFrom("Changed") with { IsAbstract = true }));

        await Assert
            .That(body)
            .IsEqualTo("    public abstract event global::System.EventHandler? Changed;\n");
    }

    [Test]
    public async Task Should_write_a_buffered_attribute_above_the_event()
    {
        EventModel model = EventFrom("Old");

        string body = Render(type =>
        {
            type.Attribute(model.Attributes[0]).Event(model);
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    [global::System.ObsoleteAttribute(\"old\")]\n    public event global::System.EventHandler? Old;\n"
            );
    }

    [Test]
    public async Task Should_separate_an_event_from_the_next_member_with_a_blank_line()
    {
        string body = Render(type =>
        {
            type.Event(EventFrom("Changed"));
            type.Event(EventFrom("Done"));
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public event global::System.EventHandler? Changed;\n\n"
                    + "    public static event global::System.Action? Done;\n"
            );
    }

    [Test]
    public async Task Should_compile_events_under_nullable_enable()
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(Sample());
            type.Event(EventFrom("Changed"));
            type.Event(EventFrom("Done"));
            type.Event(EventFrom("PropertyChanged"));
        }

        string source =
            "public partial class Sample : System.ComponentModel.INotifyPropertyChanged { }\n"
            + writer;
        CSharpCompilation compilation = CompilationHelper.Compile(source, allowErrors: true);

        await Assert
            .That(
                compilation
                    .GetDiagnostics()
                    .Where(d =>
                        d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning
                    )
                    .Where(d => d.Id != "CS0067")
                    .Select(d => d.Id)
            )
            .IsEmpty();
    }

    [Test]
    public async Task Should_reject_an_event_with_custom_accessors_and_leave_the_writer_untouched()
    {
        await AssertRejected(EventFrom("Custom"));
    }

    [Test]
    public async Task Should_reject_an_explicit_interface_event_and_leave_the_writer_untouched()
    {
        await AssertRejected(
            EventFrom("Changed") with
            {
                ExplicitInterface = EventFrom("Changed").Type,
            }
        );
    }

    private static async Task AssertRejected(EventModel model)
    {
        SourceWriter writer = new();
        string before = "";
        string after = "";
        string afterValidMember = "";
        ArgumentException? thrown = null;

        using (var file = writer.File())
        {
            using var type = file.Type(Sample());
            before = writer.ToString();

            try
            {
                type.Attribute(EventFrom("Old").Attributes[0]).Event(model);
            }
            catch (ArgumentException exception)
            {
                thrown = exception;
            }

            after = writer.ToString();

            // A rejected event must not leave its buffered [Obsolete] to land on the next member.
            type.Event(EventFrom("Changed"));
            afterValidMember = writer.ToString();
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(after).IsEqualTo(before);
        await Assert.That(afterValidMember).DoesNotContain("Obsolete");
    }
}
