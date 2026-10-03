using Haitch.Roslyn.Testing;

namespace Haitch.Roslyn.Samples.Tests;

public class NotifyGeneratorTests
{
    private static readonly string[] TrackedSteps =
    [
        "NotifyGenerator.Fields",
        "NotifyGenerator.Types",
    ];

    [Test]
    public void Fields_become_properties_in_one_partial_type()
    {
        const string source = """
            namespace App;

            public partial class Person
            {
                [Notify.Notify]
                private string _title = "";

                [Notify.Notify]
                private int m_count;
            }
            """;

        Run(source)
            .AssertNoDiagnostics()
            .AssertSourceFile("App.Person.Notify.g.cs", "Expected/Person.Notify.g.cs.txt");
    }

    [Test]
    public void Type_that_already_implements_the_interface_is_not_given_it_again()
    {
        const string source = """
            using System.ComponentModel;

            namespace App;

            public partial class Document : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                [Notify.Notify]
                private string? _name;
            }
            """;

        Run(source)
            .AssertNoDiagnostics()
            .AssertSourceFile("App.Document.Notify.g.cs", "Expected/Document.Notify.g.cs.txt");
    }

    [Test]
    public void Type_with_its_own_event_but_no_interface_gets_only_the_interface()
    {
        const string source = """
            using System.ComponentModel;

            namespace App;

            public partial class Ticket
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                [Notify.Notify]
                private int _id;
            }
            """;

        Run(source)
            .AssertNoDiagnostics()
            .AssertSourceFile("App.Ticket.Notify.g.cs", "Expected/Ticket.Notify.g.cs.txt");
    }

    [Test]
    public void Nested_generic_type_keeps_its_enclosing_declarations()
    {
        const string source = """
            namespace App;

            public partial class Outer
            {
                public partial class Box<T>
                {
                    [Notify.Notify]
                    private T? _item;
                }
            }
            """;

        Run(source)
            .AssertNoDiagnostics()
            .AssertSourceFile("App.Outer+Box`1.Notify.g.cs", "Expected/Box.Notify.g.cs.txt");
    }

    [Test]
    public void Keyword_named_field_is_escaped_in_the_generated_code()
    {
        const string source = """
            namespace App;

            public partial class Keyword
            {
                [Notify.Notify]
                private string @class = "";
            }
            """;

        Run(source)
            .AssertNoDiagnostics()
            .AssertSourceFile("App.Keyword.Notify.g.cs", "Expected/Keyword.Notify.g.cs.txt");
    }

    [Test]
    public void Name_argument_overrides_the_property_name()
    {
        const string source = """
            namespace App;

            public partial class Article
            {
                [Notify.Notify(Name = "Headline")]
                private string _title = "";
            }
            """;

        Run(source)
            .AssertNoDiagnostics()
            .AssertSourceFile("App.Article.Notify.g.cs", "Expected/Article.Notify.g.cs.txt");
    }

    [Test]
    public void Raise_false_suppresses_the_event()
    {
        const string source = """
            namespace App;

            public partial class Counter
            {
                [Notify.Notify(Raise = false)]
                private int _count;
            }
            """;

        Run(source)
            .AssertNoDiagnostics()
            .AssertSourceFile("App.Counter.Notify.g.cs", "Expected/Counter.Notify.g.cs.txt");
    }

    [Test]
    public void Name_and_Raise_arguments_combine()
    {
        const string source = """
            namespace App;

            public partial class Label
            {
                [Notify.Notify(Name = "Caption", Raise = false)]
                private string _text = "";
            }
            """;

        Run(source)
            .AssertNoDiagnostics()
            .AssertSourceFile("App.Label.Notify.g.cs", "Expected/Label.Notify.g.cs.txt");
    }

    [Test]
    public async Task Type_where_nothing_raises_compiles_without_warnings()
    {
        const string source = """
            namespace App;

            public partial class Counter
            {
                [Notify.Notify(Raise = false)]
                private int _count;
            }
            """;

        var diagnostics = Run(source).Compilation.GetDiagnostics();

        await Assert.That(diagnostics.Select(d => d.ToString())).IsEmpty();
    }

    [Test]
    public async Task Name_equal_to_the_field_name_is_reported_as_an_invalid_name()
    {
        const string source = """
            namespace App;

            public partial class Person
            {
                [Notify.Notify(Name = "_title")]
                private string _title = "";
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY006", messageContains: "differ");
        await Assert.That(result.Diagnostics).HasCount(1);
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Person.Notify.g.cs");
    }

    [Test]
    public void At_prefixed_Name_is_reported_with_a_message_saying_it_is_unsupported()
    {
        const string source = """
            namespace App;

            public partial class Person
            {
                [Notify.Notify(Name = "@title")]
                private string _title = "";
            }
            """;

        Run(source).AssertDiagnostic("NOTIFY006", messageContains: "'@'-prefixed");
    }

    [Test]
    [Arguments("not an identifier")]
    [Arguments("")]
    [Arguments("class")]
    [Arguments("1st")]
    [Arguments("field")]
    [Arguments("value")]
    [Arguments("@title")]
    public async Task Invalid_Name_argument_reports_one_diagnostic_and_emits_nothing(string name)
    {
        var source = $$"""
            namespace App;

            public partial class Person
            {
                [Notify.Notify(Name = "{{name}}")]
                private string _title = "";
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY006", messageContains: "_title");
        await Assert.That(result.Diagnostics).HasCount(1);
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Person.Notify.g.cs");
    }

    [Test]
    public async Task Non_partial_type_reports_one_diagnostic_and_emits_nothing()
    {
        const string source = """
            namespace App;

            public class Person
            {
                [Notify.Notify]
                private string _title = "";

                [Notify.Notify]
                private int _count;
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY001", messageContains: "Person");
        await Assert.That(result.Diagnostics).HasCount(1);
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Person.Notify.g.cs");
    }

    [Test]
    public async Task Non_partial_outer_type_around_a_partial_inner_type_is_reported()
    {
        const string source = """
            namespace App;

            public class Outer
            {
                public partial class Inner
                {
                    [Notify.Notify]
                    private string _title = "";
                }
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY001", messageContains: "Inner");
        await Assert.That(result.Diagnostics).HasCount(1);
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Outer+Inner.Notify.g.cs");
    }

    [Test]
    public async Task Field_without_a_prefix_is_reported_because_its_property_would_collide()
    {
        const string source = """
            namespace App;

            public partial class Person
            {
                [Notify.Notify]
                private string Title = "";
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY002", messageContains: "Title");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Person.Notify.g.cs");
    }

    [Test]
    [Arguments(
        "[Notify.Notify] private string _title = \"\"; [Notify.Notify] private string m_title = \"\";",
        "'_title'"
    )]
    [Arguments("[Notify.Notify] private string _PropertyChanged = \"\";", "_PropertyChanged")]
    [Arguments(
        "public string Title { get; set; } = \"\"; [Notify.Notify] private string _title = \"\";",
        "_title"
    )]
    [Arguments("[Notify.Notify] private string _1st = \"\";", "_1st")]
    [Arguments("[Notify.Notify] private string _person = \"\";", "_person")]
    public async Task Unusable_property_name_is_reported_and_nothing_is_emitted(
        string members,
        string field
    )
    {
        var source = $$"""
            namespace App;

            public partial class Person
            {
                {{members}}
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY005", messageContains: field);
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Person.Notify.g.cs");
    }

    [Test]
    [Arguments("private static string _a = \"\";")]
    [Arguments("private readonly string _a = \"\";")]
    [Arguments("private const string _a = \"\";")]
    [Arguments("private string value = \"\";")]
    [Arguments("private string field = \"\";")]
    public async Task Field_that_cannot_become_a_property_is_reported_and_nothing_is_emitted(
        string field
    )
    {
        var source = $$"""
            namespace App;

            public partial class Person
            {
                [Notify.Notify]
                {{field}}
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY004");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Person.Notify.g.cs");
    }

    [Test]
    public async Task Pointer_field_is_reported_because_its_property_would_need_unsafe()
    {
        const string source = """
            namespace App;

            public unsafe partial class Person
            {
                [Notify.Notify]
                private int* _pointer;
            }
            """;

        var result = GeneratorHarness.Run(
            new NotifyGenerator(),
            new GeneratorHarnessInput { Sources = [source], AllowInputErrors = true }
        );

        result.AssertDiagnostic("NOTIFY004", messageContains: "_pointer");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Person.Notify.g.cs");
    }

    [Test]
    public async Task Base_class_implementing_the_interface_is_reported_because_the_event_is_not_ours()
    {
        const string source = """
            using System.ComponentModel;

            namespace App;

            public class Base : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;
            }

            public partial class Derived : Base
            {
                [Notify.Notify]
                private string _name = "";
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY003", messageContains: "Derived");
        await Assert.That(result.Diagnostics).HasCount(1);
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Derived.Notify.g.cs");
    }

    [Test]
    [Arguments(
        "event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged { add { } remove { } }"
    )]
    [Arguments("public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }")]
    public async Task Event_that_is_explicit_or_custom_is_reported(string eventDeclaration)
    {
        var source = $$"""
            using System.ComponentModel;

            namespace App;

            public partial class Document : INotifyPropertyChanged
            {
                {{eventDeclaration}}

                [Notify.Notify]
                private string _name = "";
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY003", messageContains: "Document");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Document.Notify.g.cs");
    }

    [Test]
    public async Task Event_explicit_implementation_is_reported_as_not_declaring_an_event()
    {
        const string source = """
            using System.ComponentModel;

            namespace App;

            public partial class Document : INotifyPropertyChanged
            {
                event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged { add { } remove { } }

                [Notify.Notify]
                private string _name = "";
            }
            """;

        Run(source).AssertDiagnostic("NOTIFY003", messageContains: "without declaring");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Positional_record_parameter_named_like_the_event_is_reported()
    {
        const string source = """
            namespace App;

            public partial record Row(int PropertyChanged)
            {
                [Notify.Notify]
                private string _name = "";
            }
            """;

        var result = GeneratorHarness.Run(
            new NotifyGenerator(),
            new GeneratorHarnessInput { Sources = [source], AllowInputErrors = true }
        );

        result.AssertDiagnostic("NOTIFY003", messageContains: "Row");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Row.Notify.g.cs");
    }

    [Test]
    public async Task Static_event_is_reported_because_the_setter_cannot_raise_it()
    {
        const string source = """
            using System.ComponentModel;

            namespace App;

            public partial class Document
            {
                public static event PropertyChangedEventHandler? PropertyChanged;

                [Notify.Notify]
                private string _name = "";
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY003", messageContains: "Document");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Document.Notify.g.cs");
    }

    [Test]
    public async Task Abstract_event_is_reported_because_the_setter_cannot_raise_it()
    {
        const string source = """
            using System.ComponentModel;

            namespace App;

            public abstract partial class Document : INotifyPropertyChanged
            {
                public abstract event PropertyChangedEventHandler? PropertyChanged;

                [Notify.Notify]
                private string _name = "";
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY003", messageContains: "Document");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Document.Notify.g.cs");
    }

    [Test]
    public async Task Nested_type_with_the_property_name_is_reported()
    {
        const string source = """
            namespace App;

            public partial class Person
            {
                public class Title { }

                [Notify.Notify]
                private string _title = "";
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY005", messageContains: "_title");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Person.Notify.g.cs");
    }

    [Test]
    public async Task Nested_type_named_like_the_event_is_reported()
    {
        const string source = """
            namespace App;

            public partial class Document
            {
                public class PropertyChanged { }

                [Notify.Notify]
                private string _name = "";
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY003", messageContains: "Document");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Document.Notify.g.cs");
    }

    [Test]
    public async Task Ref_struct_is_reported_because_it_cannot_implement_an_interface()
    {
        const string source = """
            namespace App;

            public ref partial struct Cursor
            {
                [Notify.Notify]
                private int _x;
            }
            """;

        var result = Run(source);

        result.AssertDiagnostic("NOTIFY003", messageContains: "Cursor");
        await Assert.That(result.Sources.Keys).DoesNotContain("App.Cursor.Notify.g.cs");
    }

    [Test]
    public async Task Syntax_error_elsewhere_in_the_input_does_not_stop_generation()
    {
        const string good = """
            namespace App;

            public partial class Person
            {
                [Notify.Notify]
                private string _title = "";
            }
            """;
        const string broken = """
            namespace App;

            public class Broken {
            """;

        var result = GeneratorHarness
            .Run(
                new NotifyGenerator(),
                new GeneratorHarnessInput { Sources = [good, broken], AllowInputErrors = true }
            )
            .AssertNoDiagnostics();

        await Assert.That(result.Sources).ContainsKey("App.Person.Notify.g.cs");
        await Assert.That(result.InputDiagnostics).IsNotEmpty();
    }

    [Test]
    public void Output_is_cacheable()
    {
        const string source = """
            namespace App;

            public partial class Person
            {
                [Notify.Notify]
                private string _title = "";

                [Notify.Notify]
                private int _count;
            }
            """;

        GeneratorHarness.AssertCacheable(
            new NotifyGenerator(),
            new GeneratorHarnessInput { Sources = [source] },
            TrackedSteps
        );
    }

    private static GeneratorHarnessResult Run(string source) =>
        GeneratorHarness.Run(
            new NotifyGenerator(),
            new GeneratorHarnessInput { Sources = [source] }
        );

    [Test]
    public async Task Adding_an_unrelated_member_leaves_the_type_step_unchanged()
    {
        const string source = """
            namespace App;

            public partial class Person
            {
                [Notify.Notify]
                private string _title = "";

                public void Existing() { }
            }
            """;

        var result = GeneratorHarness.Run(
            new NotifyGenerator(),
            new GeneratorHarnessInput { Sources = [source] }
        );

        var tree = result.InputCompilation.SyntaxTrees.First();
        var edited = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            tree.GetText()
                .ToString()
                .Replace(
                    "public void Existing() { }",
                    "public void Existing() { } public void Added() { }"
                ),
            (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)tree.Options,
            tree.FilePath
        );
        var driver = result.Driver.RunGenerators(
            result.InputCompilation.ReplaceSyntaxTree(tree, edited)
        );
        var outputs = driver
            .GetRunResult()
            .Results[0]
            .TrackedSteps["NotifyGenerator.Types"]
            .SelectMany(s => s.Outputs)
            .ToList();

        await Assert.That(outputs).IsNotEmpty();
        await Assert
            .That(
                outputs.All(o =>
                    o.Reason
                        is Microsoft.CodeAnalysis.IncrementalStepRunReason.Cached
                            or Microsoft.CodeAnalysis.IncrementalStepRunReason.Unchanged
                )
            )
            .IsTrue();
    }
}
