using System.Linq;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Types;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Writing;

public class PartialBaseListTests
{
    private static readonly TypeRef NotifyChanged = new(
        "global::System.ComponentModel.INotifyPropertyChanged",
        NullableAnnotation.NotAnnotated,
        SpecialType.None,
        TypeKind.Interface,
        false
    );
    private static readonly TypeRef Disposable = new(
        "global::System.IDisposable",
        NullableAnnotation.NotAnnotated,
        SpecialType.None,
        TypeKind.Interface,
        false
    );

    private const string Nested = """
        public partial class Outer { public partial class Middle { public partial class Inner { } } }
        """;

    private static TypeModel Model(string source, string metadataName)
    {
        return TypeModel.From(CompilationHelper.GetNamedTypeSymbol(source, metadataName));
    }

    private static async Task AssertCompiles(string source, string output)
    {
        string[] problems = CompilationHelper
            .Compile($"{source}\n{output}", allowErrors: true)
            .GetDiagnostics()
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(d => d.Id != "CS0067")
            .Select(d => d.ToString())
            .ToArray();

        await Assert.That(problems).IsEmpty();
    }

    private static string RenderFile(TypeModel model, EquatableArray<TypeRef> baseTypes)
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var type = file.Type(model, baseTypes);
        }

        return writer.ToString();
    }

    [Test]
    public async Task Should_write_the_base_on_a_top_level_partial_and_compile()
    {
        const string source = "public partial class Sample { }";
        string output = RenderFile(
            Model(source, "Sample"),
            new[] { NotifyChanged }.ToEquatableArray()
        );

        await Assert
            .That(output)
            .Contains(
                "partial class Sample : global::System.ComponentModel.INotifyPropertyChanged\n{"
            );
        await AssertCompiles(
            "public partial class Sample { public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged; }",
            output
        );
    }

    [Test]
    public async Task Should_write_the_base_on_only_the_innermost_of_nested_partials()
    {
        string output = RenderFile(
            Model(Nested, "Outer+Middle+Inner"),
            new[] { NotifyChanged }.ToEquatableArray()
        );

        await Assert.That(output).Contains("partial class Outer\n{");
        await Assert.That(output).Contains("partial class Middle\n");
        await Assert
            .That(output)
            .Contains(
                "partial class Inner : global::System.ComponentModel.INotifyPropertyChanged\n"
            );
        await Assert.That(output.Split([" : "], StringSplitOptions.None).Length).IsEqualTo(2);
        await AssertCompiles(
            "public partial class Outer { public partial class Middle { public partial class Inner { public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged; } } }",
            output
        );
    }

    [Test]
    public async Task Should_write_the_base_before_where_clauses_on_a_generic_partial()
    {
        const string source = "public partial class Box<T> where T : class { }";
        string output = RenderFile(Model(source, "Box`1"), new[] { Disposable }.ToEquatableArray());

        await Assert
            .That(output)
            .Contains("partial class Box<T> : global::System.IDisposable\n    where T : class\n{");
        await AssertCompiles(
            "public partial class Box<T> where T : class { public void Dispose() { } }",
            output
        );
    }

    [Test]
    public async Task Should_write_two_base_types_in_order()
    {
        const string source = "public partial class Sample { }";
        string output = RenderFile(
            Model(source, "Sample"),
            new[] { Disposable, NotifyChanged }.ToEquatableArray()
        );

        await Assert
            .That(output)
            .Contains(
                "partial class Sample : global::System.IDisposable, global::System.ComponentModel.INotifyPropertyChanged\n"
            );
        await AssertCompiles(
            "public partial class Sample { public void Dispose() { } public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged; }",
            output
        );
    }

    [Test]
    public async Task Should_write_exactly_the_same_for_a_default_or_empty_list()
    {
        const string source = "public partial class Sample { }";
        TypeModel model = Model(source, "Sample");
        string baseline = RenderFile(model, default);

        SourceWriter plain = new();
        using (var file = plain.File())
        {
            using var type = file.Type(model);
        }

        await Assert.That(baseline).IsEqualTo(plain.ToString());
        await Assert.That(RenderFile(model, new TypeRef[0].ToEquatableArray())).IsEqualTo(baseline);
        await Assert.That(baseline).DoesNotContain(" : ");
    }

    [Test]
    public async Task Should_accept_the_base_list_by_name_on_the_namespace_scope()
    {
        const string source = "public partial class Sample { }";
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var ns = file.Namespace("N");
            using var type = ns.Type(
                Model(source, "Sample"),
                baseTypes: new[] { Disposable }.ToEquatableArray()
            );
        }

        await Assert
            .That(writer.ToString())
            .Contains("partial class Sample : global::System.IDisposable\n");
    }

    [Test]
    public async Task Should_write_the_base_on_a_type_scope_nested_partial()
    {
        TypeModel outer = Model(Nested, "Outer");
        TypeModel middle = Model(Nested, "Outer+Middle");
        SourceWriter writer = new();

        using (var file = writer.File())
        {
            using var o = file.Type(outer);
            using var m = o.Type(middle, new[] { Disposable }.ToEquatableArray());
        }

        string text = writer.ToString();

        await Assert.That(text).Contains("partial class Outer\n");
        await Assert.That(text).Contains("partial class Middle : global::System.IDisposable\n");
    }
}
