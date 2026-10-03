using System.Linq;
using Haitch.Roslyn.Models;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Models;

public class EventModelTests
{
    [Test]
    public async Task Should_capture_a_field_like_event()
    {
        EventModel model = Single(
            "public class Host { public event System.EventHandler Changed; }"
        );

        await Assert.That(model.Name).IsEqualTo("Changed");
        await Assert.That(model.Type.FullyQualifiedName).IsEqualTo("global::System.EventHandler");
        await Assert.That(model.Accessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(model.IsStatic).IsFalse();
        await Assert.That(model.IsFieldLike).IsTrue();
        await Assert.That(model.ExplicitInterface).IsNull();
    }

    [Test]
    public async Task Should_capture_an_event_with_custom_accessors_as_not_field_like()
    {
        EventModel model = Single(
            """
            public class Host
            {
                private System.EventHandler? _h;
                protected virtual event System.EventHandler? Changed { add { _h += value; } remove { _h -= value; } }
            }
            """
        );

        await Assert.That(model.IsFieldLike).IsFalse();
        await Assert.That(model.Accessibility).IsEqualTo(Accessibility.Protected);
        await Assert.That(model.IsVirtual).IsTrue();
    }

    [Test]
    public async Task Should_capture_a_static_event()
    {
        EventModel model = Single("public class Host { public static event System.Action Done; }");

        await Assert.That(model.IsStatic).IsTrue();
        await Assert.That(model.IsFieldLike).IsTrue();
    }

    [Test]
    public async Task Should_capture_an_explicit_interface_event()
    {
        EventModel model = Single(
            """
            public interface IHost { event System.EventHandler Changed; }
            public class Host : IHost
            {
                event System.EventHandler IHost.Changed { add { } remove { } }
            }
            """
        );

        await Assert.That(model.Name).IsEqualTo("Changed");
        await Assert.That(model.ExplicitInterface!.FullyQualifiedName).IsEqualTo("global::IHost");
        await Assert.That(model.ExplicitInterfaceMemberName).IsEqualTo("Changed");
        await Assert.That(model.IsFieldLike).IsFalse();
    }

    [Test]
    public async Task Should_keep_the_nullable_annotation_of_the_delegate_type()
    {
        EventModel model = Single(
            "public class Host { public event System.EventHandler? Changed; }"
        );

        await Assert.That(model.Type.NullableAnnotation).IsEqualTo(NullableAnnotation.Annotated);
    }

    [Test]
    public async Task Should_capture_override_sealed_and_abstract_modifiers()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            """
            public abstract class Base { public abstract event System.Action A; public virtual event System.Action? B; }
            public class Host : Base
            {
                public sealed override event System.Action A;
                public override event System.Action? B;
            }
            """,
            "Host"
        );
        INamedTypeSymbol baseType = type.BaseType!;

        EventModel a = EventModel.From(type.GetMembers("A").OfType<IEventSymbol>().Single());
        EventModel baseA = EventModel.From(
            baseType.GetMembers("A").OfType<IEventSymbol>().Single()
        );

        await Assert.That(a.IsOverride).IsTrue();
        await Assert.That(a.IsSealed).IsTrue();
        await Assert.That(baseA.IsAbstract).IsTrue();
    }

    [Test]
    public async Task Should_capture_attributes_on_the_event()
    {
        EventModel model = Single(
            """
            public class Host
            {
                [System.Obsolete("no")]
                public event System.EventHandler Changed { add { } remove { } }
            }
            """
        );

        await Assert.That(model.Attributes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Should_list_events_on_the_type_model_only_when_members_are_included()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            "public class Host { public event System.EventHandler Changed; public int Field; }",
            "Host"
        );

        TypeModel with = TypeModel.From(type, includeMembers: true);
        TypeModel without = TypeModel.From(type);

        await Assert.That(with.Events.Count).IsEqualTo(1);
        await Assert.That(with.Events[0].Name).IsEqualTo("Changed");
        await Assert.That(without.Events.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Should_be_equal_for_two_from_calls()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            "public class Host { public event System.EventHandler? Changed; }",
            "Host"
        );
        IEventSymbol symbol = type.GetMembers().OfType<IEventSymbol>().Single();

        await Assert.That(EventModel.From(symbol)).IsEqualTo(EventModel.From(symbol));
        await Assert
            .That(TypeModel.From(type, includeMembers: true))
            .IsEqualTo(TypeModel.From(type, includeMembers: true));
    }

    [Test]
    public async Task Should_capture_an_interface_event_as_abstract_and_field_like()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(
            "public interface Host { event System.EventHandler Changed; }",
            "Host"
        );

        EventModel model = EventModel.From(type.GetMembers().OfType<IEventSymbol>().Single());

        await Assert.That(model.Name).IsEqualTo("Changed");
        await Assert.That(model.IsAbstract).IsTrue();
        await Assert.That(model.IsFieldLike).IsTrue();
        await Assert.That(model.ExplicitInterface).IsNull();
    }

    private static EventModel Single(string source)
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Host");

        return EventModel.From(type.GetMembers().OfType<IEventSymbol>().Single());
    }
}
