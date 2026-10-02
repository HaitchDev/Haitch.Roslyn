using System.Linq;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Types;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Writing.Scopes;

public class FieldAndPropertyScopeTests
{
    private const string MemberSource =
        """
        public enum Color { Red = 1 }

        public abstract class Source
        {
            private static readonly System.Collections.Generic.List<int> Cache = new();
            public const int Max = 5;
            public const string Label = "a\"b";
            public const Color Default = Color.Red;
            public const float F = 1.5f;
            public const double D = 2.25;
            public const decimal M = 3.5m;
            public const char C = 'x';
            public const int Neg = -3;
            public const string? Nothing = null;
            public int Plain;
            public required int Id;
            protected internal readonly string? Note;

            public required string Name { get; set; }
            public int Count { get; private set; }
            public string Tag { get; init; } = "";
            public static int Shared { get; set; }
            public int ReadOnly { get; }
            public virtual int Virt { get; set; }
            protected internal int Wide { get; protected set; }
            public abstract int Abs { get; set; }
        }

        public class Derived : Source
        {
            public override int Virt { get; set; }
            public sealed override int Abs { get; set; }
        }

        public interface ISource
        {
            int Member { get; set; }
        }
        """;

    private const string SampleSource = "public partial class Sample { }";

    private static FieldModel FieldFrom(string name)
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(MemberSource, "Source");

        return FieldModel.From(type.GetMembers(name).OfType<IFieldSymbol>().Single());
    }

    private static PropertyModel PropertyFrom(string name, string typeName = "Source")
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(MemberSource, typeName);

        return PropertyModel.From(type.GetMembers(name).OfType<IPropertySymbol>().Single());
    }

    private static TypeModel Sample()
    {
        return TypeModel.From(CompilationHelper.GetNamedTypeSymbol(SampleSource, "Sample"));
    }

    private delegate void WriteMembers(TypeScope type);

    private static string Render(WriteMembers write)
    {
        return RenderIn(Sample(), "partial class Sample", write);
    }

    private static string RenderInInterface(WriteMembers write)
    {
        TypeModel model = TypeModel.From(
            CompilationHelper.GetNamedTypeSymbol("public partial interface ISample { }", "ISample"));

        return RenderIn(model, "partial interface ISample", write);
    }

    private static string RenderIn(TypeModel model, string declaration, WriteMembers write)
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(model);
            write(type);
        }

        string text = writer.ToString();
        string open = declaration + "\n{\n";
        int start = text.IndexOf(open, StringComparison.Ordinal) + open.Length;

        return text.Substring(start, text.Length - start - "}\n".Length);
    }

    [Test]
    public async Task Should_render_a_private_static_readonly_field_with_an_initializer()
    {
        string body = Render(type => type.Field(FieldFrom("Cache"), "new()"));

        await Assert
            .That(body)
            .IsEqualTo("    private static readonly global::System.Collections.Generic.List<int> Cache = new();\n");
    }

    [Test]
    public async Task Should_render_a_field_without_an_initializer()
    {
        string body = Render(type => type.Field(FieldFrom("Plain")));

        await Assert.That(body).IsEqualTo("    public int Plain;\n");
    }

    [Test]
    public async Task Should_render_a_public_const_from_its_constant_value()
    {
        string body = Render(type =>
        {
            type.Field(FieldFrom("Max"));
        });

        await Assert.That(body).IsEqualTo("    public const int Max = 5;\n");
    }

    [Test]
    public async Task Should_render_string_and_enum_consts()
    {
        string body = Render(type =>
        {
            type.Field(FieldFrom("Label"));
            type.Field(FieldFrom("Default"));
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public const string Label = \"a\\\"b\";\n\n    public const global::Color Default = (global::Color)1;\n");
    }

    [Test]
    public async Task Should_render_a_required_field_and_a_nullable_protected_internal_readonly_field()
    {
        string body = Render(type =>
        {
            type.Field(FieldFrom("Id"));
            type.Field(FieldFrom("Note"));
        });

        await Assert
            .That(body)
            .IsEqualTo("    public required int Id;\n\n    protected internal readonly string? Note;\n");
    }

    [Test]
    public async Task Should_reject_a_const_field_with_an_explicit_initializer()
    {
        await Assert
            .That(() =>
            {
                Render(type => type.Field(FieldFrom("Max"), "6"));
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_a_const_field_without_a_constant_value()
    {
        FieldModel field = FieldFrom("Max") with { ConstantValue = null };

        await Assert.That(() =>
        {
            Render(type => type.Field(field));
        }).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_a_blank_initializer()
    {
        await Assert
            .That(() =>
            {
                Render(type => type.Field(FieldFrom("Plain"), " "));
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_render_a_required_property()
    {
        string body = Render(type => type.AutoProperty(PropertyFrom("Name")));

        await Assert.That(body).IsEqualTo("    public required string Name { get; set; }\n");
    }

    [Test]
    public async Task Should_render_a_private_set_property()
    {
        string body = Render(type => type.AutoProperty(PropertyFrom("Count")));

        await Assert.That(body).IsEqualTo("    public int Count { get; private set; }\n");
    }

    [Test]
    public async Task Should_render_an_init_property()
    {
        string body = Render(type => type.AutoProperty(PropertyFrom("Tag")));

        await Assert.That(body).IsEqualTo("    public string Tag { get; init; }\n");
    }

    [Test]
    public async Task Should_render_static_get_only_and_virtual_properties()
    {
        string body = Render(type =>
        {
            type.AutoProperty(PropertyFrom("Shared"));
            type.AutoProperty(PropertyFrom("ReadOnly"));
            type.AutoProperty(PropertyFrom("Virt"));
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public static int Shared { get; set; }\n\n    public int ReadOnly { get; }\n\n    public virtual int Virt { get; set; }\n");
    }

    [Test]
    public async Task
        Should_render_override_and_sealed_override_properties_and_accessor_accessibility_relative_to_the_property()
    {
        string body = Render(type =>
        {
            type.AutoProperty(PropertyFrom("Virt", "Derived"));
            type.AutoProperty(PropertyFrom("Abs", "Derived"));
            type.AutoProperty(PropertyFrom("Wide"));
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public override int Virt { get; set; }\n\n    public sealed override int Abs { get; set; }\n\n    protected internal int Wide { get; protected set; }\n");
    }

    [Test]
    public async Task Should_reject_an_abstract_property()
    {
        await Assert
            .That(() =>
            {
                Render(type => type.AutoProperty(PropertyFrom("Abs")));
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_an_interface_property()
    {
        await Assert
            .That(() =>
            {
                Render(type => type.AutoProperty(PropertyFrom("Member", "ISource")));
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_a_property_without_accessors()
    {
        PropertyModel property = PropertyFrom("Count") with { Accessors = default };

        await Assert.That(() =>
        {
            Render(type => type.AutoProperty(property));
        }).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_a_property_without_a_get_accessor()
    {
        PropertyModel setOnly = PropertyFrom("Count") with
        {
            Accessors = new[] { new PropertyAccessorModel(PropertyAccessorKind.Set, Accessibility.Public) }
                .ToEquatableArray(),
        };
        PropertyModel initOnly = setOnly with
        {
            Accessors = new[] { new PropertyAccessorModel(PropertyAccessorKind.Init, Accessibility.Public) }
                .ToEquatableArray(),
        };

        await Assert.That(() =>
        {
            Render(type => type.AutoProperty(setOnly));
        }).Throws<ArgumentException>();
        await Assert.That(() =>
        {
            Render(type => type.AutoProperty(initOnly));
        }).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_an_explicit_interface_implementation_property()
    {
        PropertyModel property = PropertyFrom("Count") with { Name = "ISource.Member" };

        await Assert.That(() =>
        {
            Render(type => type.AutoProperty(property));
        }).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_instance_fields_and_auto_properties_in_an_interface()
    {
        await Assert
            .That(() =>
            {
                RenderInInterface(type => type.Field(FieldFrom("Plain")));
            })
            .Throws<ArgumentException>();
        await Assert
            .That(() =>
            {
                RenderInInterface(type => type.AutoProperty(PropertyFrom("Count")));
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_allow_static_members_and_consts_in_an_interface()
    {
        string body = RenderInInterface(type =>
        {
            type.Field(FieldFrom("Max"));
            type.AutoProperty(PropertyFrom("Shared"));
        });

        await Assert.That(body)
            .IsEqualTo("    public const int Max = 5;\n\n    public static int Shared { get; set; }\n");
    }

    [Test]
    public async Task Should_separate_members_from_each_other_and_from_methods_with_a_blank_line()
    {
        INamedTypeSymbol source = CompilationHelper.GetNamedTypeSymbol("public class S { public void M() { } }", "S");
        MethodModel method = MethodModel.From(source.GetMembers("M").OfType<IMethodSymbol>().Single());

        string body = Render(type =>
        {
            type.Field(FieldFrom("Plain"));
            type.AutoProperty(PropertyFrom("Count"));

            using (type.Method(method)) { }

            type.Field(FieldFrom("Max"));
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public int Plain;\n\n    public int Count { get; private set; }\n\n    public void M()\n    {\n    }\n\n    public const int Max = 5;\n");
    }

    [Test]
    public async Task Should_produce_a_type_that_compiles_with_every_member_kind()
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(Sample());
            type.Field(FieldFrom("Cache"), "new()");
            type.Field(FieldFrom("Max"));
            type.Field(FieldFrom("Label"));
            type.Field(FieldFrom("Default"));
            type.Field(FieldFrom("F"));
            type.Field(FieldFrom("D"));
            type.Field(FieldFrom("M"));
            type.Field(FieldFrom("C"));
            type.Field(FieldFrom("Neg"));
            type.Field(FieldFrom("Nothing"));
            type.Field(FieldFrom("Plain"));
            type.Field(FieldFrom("Id"));
            type.Field(FieldFrom("Note"));
            type.AutoProperty(PropertyFrom("Name"));
            type.AutoProperty(PropertyFrom("Count"));
            type.AutoProperty(PropertyFrom("Tag"));
            type.AutoProperty(PropertyFrom("Shared"));
            type.AutoProperty(PropertyFrom("ReadOnly"));
            type.AutoProperty(PropertyFrom("Virt"));
            type.AutoProperty(PropertyFrom("Wide"));
        }

        string source = "public enum Color { Red = 1 }\n" + SampleSource + "\n" + writer;
        CSharpCompilation compilation = CompilationHelper.Compile(source, allowErrors: true);

        await Assert
            .That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id))
            .IsEmpty();
    }

    [Test]
    public async Task Should_reject_a_field_on_a_namespace_scope()
    {
        string[] errors = ScopeCompileCheck.Compile(
            """
                    Haitch.Roslyn.Models.FieldModel field = null!;
                    using var file = writer.File();
                    using var ns = file.Namespace("A");
                    ns.Field(field);
            """);

        await Assert.That(errors).IsEquivalentTo(new[] { "CS1061" });
    }

    [Test]
    public async Task Should_reject_a_property_in_a_body()
    {
        string[] errors = ScopeCompileCheck.Compile(
            """
                    Haitch.Roslyn.Models.TypeModel type = null!;
                    Haitch.Roslyn.Models.MethodModel method = null!;
                    Haitch.Roslyn.Models.PropertyModel property = null!;
                    using var file = writer.File();
                    using var t = file.Type(type);
                    using var body = t.Method(method);
                    body.AutoProperty(property);
            """);

        await Assert.That(errors).IsEquivalentTo(new[] { "CS1061" });
    }
}
