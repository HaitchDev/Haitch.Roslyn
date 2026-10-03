using System.Linq;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Types;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Writing.Scopes;

public class PropertyScopeTests
{
    private const string PropertySource =
        """
        public abstract class Source
        {
            private int _count;
            public int Count { get { return _count; } private set { _count = value; } }
            public string Tag { get { return ""; } init { } }
            public int Open { get { return _count; } set { _count = value; } }
            public int WriteOnly { set { _count = value; } }
            public static int Shared { get { return 1; } }
            public required int Req { get { return _count; } set { _count = value; } }
            public virtual int Virt { get { return 1; } }
            public abstract int Abs { get; set; }
        }

        public interface ISource
        {
            int Member { get; }
        }

        public interface IDefault
        {
            int Value { get { return 1; } }
        }
        """;

    private const string SampleSource = "public partial class Sample { }";

    private static PropertyModel PropertyFrom(string name, string typeName = "Source")
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(PropertySource, typeName);

        return PropertyModel.From(type.GetMembers(name).OfType<IPropertySymbol>().Single());
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
    public async Task Should_render_a_get_body_and_a_private_set_body()
    {
        string body = Render(type =>
        {
            using var property = type.Property(PropertyFrom("Count"));
            using (var get = property.Get())
            {
                get.Line("return _count;");
            }

            using (var set = property.Set())
            {
                set.Line("_count = value;");
            }
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public int Count\n    {\n        get\n        {\n            return _count;\n        }\n        private set\n        {\n            _count = value;\n        }\n    }\n");
    }

    [Test]
    public async Task Should_render_an_init_accessor_and_omit_accessibility_equal_to_the_property()
    {
        string body = Render(type =>
        {
            using (var tag = type.Property(PropertyFrom("Tag")))
            {
                using (tag.Get()) { }

                using (tag.Init()) { }
            }

            using var open = type.Property(PropertyFrom("Open"));
            using (open.Set()) { }
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public string Tag\n    {\n        get\n        {\n        }\n        init\n        {\n        }\n    }\n\n    public int Open\n    {\n        set\n        {\n        }\n    }\n");
    }

    [Test]
    public async Task Should_render_a_set_only_property()
    {
        string body = Render(type =>
        {
            using var property = type.Property(PropertyFrom("WriteOnly"));
            using (property.Set()) { }
        });

        await Assert
            .That(body)
            .IsEqualTo("    public int WriteOnly\n    {\n        set\n        {\n        }\n    }\n");
    }

    [Test]
    public async Task Should_render_static_and_virtual_headers_and_nested_blocks()
    {
        string body = Render(type =>
        {
            using (var shared = type.Property(PropertyFrom("Shared")))
            {
                using var get = shared.Get();
                using var block = get.Block("if (true)");
                block.Line("return 2;");
            }

            using var virt = type.Property(PropertyFrom("Virt"));
            using (virt.Get()) { }
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public static int Shared\n    {\n        get\n        {\n            if (true)\n            {\n                return 2;\n            }\n        }\n    }\n\n    public virtual int Virt\n    {\n        get\n        {\n        }\n    }\n");
    }

    [Test]
    public async Task Should_separate_a_property_from_sibling_members_with_a_blank_line()
    {
        string body = Render(type =>
        {
            type.Field(
                FieldModel.From(
                    CompilationHelper
                        .GetNamedTypeSymbol("public class F { public int X; }", "F")
                        .GetMembers("X")
                        .OfType<IFieldSymbol>()
                        .Single()));

            using (var property = type.Property(PropertyFrom("WriteOnly")))
            {
                using (property.Set()) { }
            }

            type.AutoProperty(PropertyFrom("Count"));
        });

        await Assert
            .That(body)
            .IsEqualTo(
                "    public int X;\n\n    public int WriteOnly\n    {\n        set\n        {\n        }\n    }\n\n    public int Count { get; private set; }\n");
    }

    [Test]
    public async Task Should_reject_an_abstract_property()
    {
        await Assert
            .That(() =>
            {
                Render(type =>
                {
                    using var property = type.Property(PropertyFrom("Abs"));
                });
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_an_abstract_interface_property()
    {
        await Assert
            .That(() =>
            {
                Render(type =>
                {
                    using var property = type.Property(PropertyFrom("Member", "ISource"));
                });
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_an_explicit_interface_implementation_property()
    {
        PropertyModel property = PropertyFrom("Open") with
        {
            Name = "Member",
            ExplicitInterface = new TypeRef("global::ISource", NullableAnnotation.NotAnnotated, SpecialType.None, TypeKind.Interface, false),
            ExplicitInterfaceMemberName = "Member",
        };

        await Assert
            .That(() =>
            {
                Render(type =>
                {
                    using var scope = type.Property(property);
                });
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_a_property_without_accessors()
    {
        PropertyModel property = PropertyFrom("Open") with { Accessors = default };

        await Assert
            .That(() =>
            {
                Render(type =>
                {
                    using var scope = type.Property(property);
                });
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_reject_an_accessor_the_model_does_not_have()
    {
        await Assert
            .That(() =>
            {
                Render(type =>
                {
                    using var property = type.Property(PropertyFrom("Shared"));
                    property.Set();
                });
            })
            .Throws<ArgumentException>();
        await Assert
            .That(() =>
            {
                Render(type =>
                {
                    using var property = type.Property(PropertyFrom("WriteOnly"));
                    property.Get();
                });
            })
            .Throws<ArgumentException>();
        await Assert
            .That(() =>
            {
                Render(type =>
                {
                    using var property = type.Property(PropertyFrom("Count"));
                    property.Init();
                });
            })
            .Throws<ArgumentException>();
        await Assert
            .That(() =>
            {
                Render(type =>
                {
                    using var property = type.Property(PropertyFrom("Tag"));
                    property.Set();
                });
            })
            .Throws<ArgumentException>();
    }

    private static (string Before, string After) TryRejectedProperty()
    {
        SourceWriter writer = new();
        using var file = writer.File();
        using var type = file.Type(Sample());
        string before = writer.ToString();

        try
        {
            type.Property(PropertyFrom("Abs"));
        }
        catch (ArgumentException) { }

        return (before, writer.ToString());
    }

    private static string[] CalledTwice()
    {
        var failures = new List<string>();

        Render(type =>
        {
            using var property = type.Property(PropertyFrom("Open"));
            using (property.Get()) { }

            using (property.Set()) { }

            foreach (string kind in new[] { "get", "set" })
            {
                try
                {
                    using var second = kind == "get"
                        ? property.Get()
                        : property.Set();
                    failures.Add(kind + ": no exception");
                }
                catch (InvalidOperationException) { }
            }
        });

        Render(type =>
        {
            using var property = type.Property(PropertyFrom("Tag"));
            using (property.Init()) { }

            try
            {
                using var second = property.Init();
                failures.Add("init: no exception");
            }
            catch (InvalidOperationException) { }
        });

        return failures.ToArray();
    }

    [Test]
    public async Task Should_reject_calling_an_accessor_twice_on_the_same_scope()
    {
        await Assert.That(CalledTwice()).IsEmpty();
    }

    [Test]
    public async Task Should_leave_the_writer_untouched_when_a_model_is_rejected()
    {
        (string before, string after) = TryRejectedProperty();

        await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    public async Task Should_produce_a_type_that_compiles()
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(Sample());
            using (var count = type.Property(PropertyFrom("Count")))
            {
                using (var get = count.Get())
                {
                    get.Line("return _count;");
                }

                using (var set = count.Set())
                {
                    set.Line("_count = value;");
                }
            }

            using (var tag = type.Property(PropertyFrom("Tag")))
            {
                using (var get = tag.Get())
                {
                    get.Line("return \"\";");
                }

                using (tag.Init()) { }
            }

            using (var writeOnly = type.Property(PropertyFrom("WriteOnly")))
            {
                using var set = writeOnly.Set();
                set.Line("_count = value;");
            }

            using (var shared = type.Property(PropertyFrom("Shared")))
            {
                using var get = shared.Get();
                get.Line("return 1;");
            }

            using (var virt = type.Property(PropertyFrom("Virt")))
            {
                using var get = virt.Get();
                get.Line("return 1;");
            }

            using (var req = type.Property(PropertyFrom("Req")))
            {
                using (var get = req.Get())
                {
                    get.Line("return _count;");
                }

                using var set = req.Set();
                set.Line("_count = value;");
            }

            using (var value = type.Property(PropertyFrom("Value", "IDefault")))
            {
                using var get = value.Get();
                get.Line("return 1;");
            }
        }

        string source = "public partial class Sample { private int _count; }\n" + writer;
        CSharpCompilation compilation = CompilationHelper.Compile(source, allowErrors: true);

        await Assert
            .That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Id))
            .IsEmpty();
    }

    private static CSharpCompilation CompileCSharp13(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        return CSharpCompilation.Create(
            "PartialPropertyAssembly",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp13))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static string[] ErrorIds(CSharpCompilation compilation)
    {
        return compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Id)
            .ToArray();
    }

    private static PropertyModel PropertyOf(CSharpCompilation compilation, string typeName, string name)
    {
        INamedTypeSymbol type = compilation.GetTypeByMetadataName(typeName)!;

        return PropertyModel.From(type.GetMembers(name).OfType<IPropertySymbol>().Single());
    }

    private static TypeModel TypeOf(CSharpCompilation compilation, string typeName)
    {
        return TypeModel.From(compilation.GetTypeByMetadataName(typeName)!);
    }

    [Test]
    public async Task Should_compile_the_implementation_of_partial_properties()
    {
        const string user =
            "public partial class Host { public partial int X { get; set; } partial string Y { get; } }";
        CSharpCompilation userCompilation = CompileCSharp13(user);

        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(TypeOf(userCompilation, "Host"));

            using (var x = type.Property(PropertyOf(userCompilation, "Host", "X")))
            {
                using (var get = x.Get())
                {
                    get.Line("return 0;");
                }

                using (x.Set()) { }
            }

            using var y = type.Property(PropertyOf(userCompilation, "Host", "Y"));
            using var yGet = y.Get();
            yGet.Line("return \"\";");
        }

        await Assert.That(ErrorIds(CompileCSharp13(user + "\n" + writer))).IsEmpty();
    }

    [Test]
    public async Task Should_compile_a_readonly_partial_property_in_a_mutable_struct()
    {
        const string user = "public partial struct Host { public readonly partial int X { get; } }";
        CSharpCompilation userCompilation = CompileCSharp13(user);

        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(TypeOf(userCompilation, "Host"));
            using var x = type.Property(PropertyOf(userCompilation, "Host", "X"));
            using var get = x.Get();
            get.Line("return 0;");
        }

        await Assert.That(ErrorIds(CompileCSharp13(user + "\n" + writer))).IsEmpty();
    }

    private static async Task<string[]> ImplementedPartialX(string user, string getBody = "return 0;", bool set = false)
    {
        CSharpCompilation userCompilation = CompileCSharp13(user);

        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(TypeOf(userCompilation, "Host"));
            using var x = type.Property(PropertyOf(userCompilation, "Host", "X"));

            using (var get = x.Get())
            {
                get.Line(getBody);
            }

            if (set)
            {
                using var setScope = x.Set();
            }
        }

        await Task.CompletedTask;

        return ErrorIds(CompileCSharp13(user + "\n" + writer));
    }

    [Test]
    public async Task Should_keep_readonly_on_only_the_getter_when_a_setter_follows()
    {
        await Assert
            .That(
                await ImplementedPartialX(
                    "public partial struct Host { public partial int X { readonly get; set; } }",
                    set: true))
            .IsEmpty();
    }

    [Test]
    public async Task Should_keep_readonly_on_a_partial_property_in_a_readonly_struct()
    {
        await Assert
            .That(
                await ImplementedPartialX(
                    "public readonly partial struct Host { public readonly partial int X { get; } }"))
            .IsEmpty();
    }

    [Test]
    public async Task Should_compile_a_sealed_interface_property()
    {
        CSharpCompilation source =
            CompileCSharp13("public interface ISealed { sealed int Value { get { return 1; } } }");
        const string user = "public partial interface IHost { }";
        CSharpCompilation userCompilation = CompileCSharp13(user);

        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(TypeOf(userCompilation, "IHost"));
            // Roslyn reports IsSealed = false for a sealed interface member, so the flag is set by hand.
            using var value = type.Property(PropertyOf(source, "ISealed", "Value") with { IsSealed = true });
            using var get = value.Get();
            get.Line("return 1;");
        }

        await Assert.That(writer.ToString()).Contains("public sealed int Value");
        await Assert.That(ErrorIds(CompileCSharp13(user + "\n" + writer))).IsEmpty();
    }

    [Test]
    public async Task Should_reject_a_line_on_a_property_scope()
    {
        string[] errors = ScopeCompileCheck.Compile(
            """
                    Haitch.Roslyn.Models.TypeModel type = null!;
                    Haitch.Roslyn.Models.PropertyModel model = null!;
                    using var file = writer.File();
                    using var t = file.Type(type);
                    using var property = t.Property(model);
                    property.Line("x");
            """);

        await Assert.That(errors).IsEquivalentTo(new[] { "CS1061" });
    }

    [Test]
    public async Task Should_reject_a_method_on_a_property_scope()
    {
        string[] errors = ScopeCompileCheck.Compile(
            """
                    Haitch.Roslyn.Models.TypeModel type = null!;
                    Haitch.Roslyn.Models.PropertyModel model = null!;
                    Haitch.Roslyn.Models.MethodModel method = null!;
                    using var file = writer.File();
                    using var t = file.Type(type);
                    using var property = t.Property(model);
                    property.Method(method);
            """);

        await Assert.That(errors).IsEquivalentTo(new[] { "CS1061" });
    }
}
