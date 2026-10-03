using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Testing;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Generators;

public class MemberDiscoveryTests
{
    private const string MarkAttributeSource = """
        namespace Sample;

        [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
        public class MarkAttribute : System.Attribute { }
        """;

    private const string MarkMemberAttributeSource = """
        namespace Sample;

        [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field, AllowMultiple = true)]
        public class MarkMemberAttribute : System.Attribute { }
        """;

    [Test]
    public async Task Should_yield_one_item_per_instance_static_and_generic_method()
    {
        var items = Collect(
            """
            public class Widget
            {
                [Sample.Mark] public void Instance() { }
                [Sample.Mark] public static int Static() => 1;
                [Sample.Mark] public T Generic<T>(T value) => value;
                public void Unmarked() { }
            }
            """
        );

        await Assert
            .That(items.Select(item => item.Method.Name).Order().ToArray())
            .IsEquivalentTo(new[] { "Generic", "Instance", "Static" });
        await Assert
            .That(items.Single(item => item.Method.Name == "Static").Method.IsStatic)
            .IsTrue();
        await Assert
            .That(items.Single(item => item.Method.Name == "Generic").Method.TypeParameters.Count)
            .IsEqualTo(1);
        await Assert.That(items.All(item => item.ContainingType.Name == "Widget")).IsTrue();
    }

    [Test]
    public async Task Should_carry_the_nested_containing_type_without_members()
    {
        var items = Collect(
            """
            namespace App;

            public partial class Outer
            {
                public partial class Inner
                {
                    public int Field;
                    [Sample.Mark] public void Run() { }
                }
            }
            """
        );

        await Assert.That(items.Length).IsEqualTo(1);
        await Assert.That(items[0].ContainingType.Name).IsEqualTo("Inner");
        await Assert.That(items[0].ContainingType.Namespace).IsEqualTo("App");
        await Assert.That(items[0].ContainingType.ContainingTypes.Count).IsEqualTo(1);
        await Assert.That(items[0].ContainingType.Methods.Count).IsEqualTo(0);
        await Assert.That(items[0].ContainingType.Fields.Count).IsEqualTo(0);
        await Assert.That(items[0].Syntax.AreContainingTypesPartial).IsTrue();
    }

    [Test]
    public async Task Should_carry_the_method_attributes_and_location()
    {
        var items = Collect(
            """
            public class Widget
            {
                [Sample.Mark]
                [Sample.Mark]
                public void Run() { }
            }
            """
        );

        await Assert.That(items.Length).IsEqualTo(1);
        await Assert.That(items[0].Attributes.Count).IsEqualTo(2);
        await Assert.That(items[0].Syntax.Location!.FilePath).IsEqualTo("Source1.cs");
        await Assert.That(items[0].Syntax.IsPartial).IsFalse();
        await Assert.That(items[0].Syntax.AreContainingTypesPartial).IsFalse();
    }

    [Test]
    public async Task Should_yield_one_item_when_a_partial_method_is_attributed_on_the_definition_only()
    {
        var items = Collect(
            """
            public partial class Widget
            {
                [Sample.Mark] public partial void Run();
                public partial void Run() { }
            }
            """
        );

        await Assert.That(items.Length).IsEqualTo(1);
        await Assert.That(items[0].Method.IsPartialDefinition).IsTrue();
        await Assert.That(items[0].Attributes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Should_yield_one_item_when_a_partial_method_is_attributed_on_the_implementation_only()
    {
        var items = Collect(
            """
            public partial class Widget
            {
                public partial void Run();
                [Sample.Mark] public partial void Run() { }
            }
            """
        );

        await Assert.That(items.Length).IsEqualTo(1);
        await Assert.That(items[0].Method.IsPartialDefinition).IsTrue();
        await Assert.That(items[0].Attributes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Should_yield_one_item_with_both_attributes_when_both_partial_parts_are_attributed()
    {
        var items = Collect(
            """
            public partial class Widget
            {
                [Sample.Mark] public partial void Run();
                [Sample.Mark] public partial void Run() { }
            }
            """
        );

        await Assert.That(items.Length).IsEqualTo(1);
        await Assert.That(items[0].Method.IsPartialDefinition).IsTrue();
        await Assert.That(items[0].Attributes.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Should_yield_one_item_when_partial_parts_are_attributed_in_different_files()
    {
        var items = Collect(
            """
            public partial class Widget
            {
                [Sample.Mark] public partial void Run() { }
            }
            """,
            """
            public partial class Widget
            {
                [Sample.Mark] public partial void Run();
            }
            """
        );

        await Assert.That(items.Length).IsEqualTo(1);
        await Assert.That(items[0].Attributes.Count).IsEqualTo(2);
        await Assert.That(items[0].Syntax.Location!.FilePath).IsEqualTo("Source2.cs");
    }

    [Test]
    public async Task Should_ignore_local_functions_and_lambdas()
    {
        var items = Collect(
            """
            public class Widget
            {
                public void Run()
                {
                    [Sample.Mark] void Local() { }
                    System.Action lambda = [Sample.Mark] () => { };
                    Local();
                    lambda();
                }
            }
            """
        );

        await Assert.That(items.Length).IsEqualTo(0);
    }

    [Test]
    public void Should_generate_from_each_attributed_method()
    {
        GeneratorHarness
            .Run(
                new MethodGenerator(),
                new GeneratorHarnessInput
                {
                    Sources =
                    [
                        MarkAttributeSource,
                        "public class Widget { [Sample.Mark] public void Run() { } }",
                    ],
                }
            )
            .AssertNoDiagnostics()
            .AssertSource("Widget.Run.g.cs", "// generated for Widget.Run\n");
    }

    [Test]
    public async Task Should_record_the_tracking_name_in_the_run_steps()
    {
        var result = GeneratorHarness.Run(
            new MethodGenerator(),
            new GeneratorHarnessInput
            {
                Sources =
                [
                    MarkAttributeSource,
                    "public class Widget { [Sample.Mark] public void Run() { } }",
                ],
            }
        );

        await Assert
            .That(result.RunResult.Results[0].TrackedSteps.ContainsKey(MethodGenerator.StepName))
            .IsTrue();
    }

    [Test]
    public void Should_stay_cacheable_across_unrelated_and_trivia_edits()
    {
        GeneratorHarness.AssertCacheable(
            new MethodGenerator(),
            new GeneratorHarnessInput
            {
                Sources =
                [
                    """
                    public partial class Widget
                    {
                        [Sample.Mark] public void Run() { }
                        [Sample.Mark] public partial void Part();
                    }
                    """,
                    MarkAttributeSource,
                    "public partial class Widget { [Sample.Mark] public partial void Part() { } }",
                    "public class Unrelated { }",
                ],
            },
            [MethodGenerator.StepName],
            new CacheabilityOptions { UnrelatedEditSourceIndex = 3 }
        );
    }

    [Test]
    public async Task Should_yield_one_item_per_auto_and_full_property()
    {
        var collected = CollectMembers(
            """
            public class Widget
            {
                private int _full;
                [Sample.MarkMember] public int Auto { get; set; }
                [Sample.MarkMember] public int Full { get => _full; set => _full = value; }
                [Sample.MarkMember] public static string Computed => "x";
                public int Unmarked { get; set; }
            }
            """
        );

        await Assert
            .That(collected.Properties.Select(item => item.Property.Name).Order().ToArray())
            .IsEquivalentTo(new[] { "Auto", "Computed", "Full" });
        await Assert
            .That(
                collected
                    .Properties.Single(item => item.Property.Name == "Computed")
                    .Property.IsStatic
            )
            .IsTrue();
        await Assert
            .That(collected.Properties.All(item => item.ContainingType.Name == "Widget"))
            .IsTrue();
        await Assert.That(collected.Fields.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Should_carry_property_attributes_location_and_containing_type_without_members()
    {
        var collected = CollectMembers(
            """
            namespace App;

            public partial class Outer
            {
                public partial class Inner
                {
                    public int Other;
                    [Sample.MarkMember]
                    [Sample.MarkMember]
                    public int Value { get; set; }
                }
            }
            """
        );

        var item = collected.Properties.Single();
        await Assert.That(item.Attributes.Count).IsEqualTo(2);
        await Assert.That(item.Syntax.Location!.FilePath).IsEqualTo("Source1.cs");
        await Assert.That(item.Syntax.IsPartial).IsFalse();
        await Assert.That(item.Syntax.AreContainingTypesPartial).IsTrue();
        await Assert.That(item.ContainingType.Name).IsEqualTo("Inner");
        await Assert.That(item.ContainingType.Namespace).IsEqualTo("App");
        await Assert.That(item.ContainingType.ContainingTypes.Count).IsEqualTo(1);
        await Assert.That(item.ContainingType.Fields.Count).IsEqualTo(0);
        await Assert.That(item.ContainingType.Properties.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Should_yield_one_property_item_when_a_partial_property_is_attributed_on_the_definition_only()
    {
        var collected = CollectMembers(
            """
            public partial class Widget
            {
                [Sample.MarkMember] public partial int Value { get; set; }
                public partial int Value { get => 0; set { } }
            }
            """
        );

        var item = collected.Properties.Single();
        await Assert.That(item.Property.IsPartial).IsTrue();
        await Assert.That(item.Attributes.Count).IsEqualTo(1);
        await Assert.That(item.Syntax.IsPartial).IsTrue();
    }

    [Test]
    public async Task Should_yield_one_property_item_when_a_partial_property_is_attributed_on_the_implementation_only()
    {
        var collected = CollectMembers(
            """
            public partial class Widget
            {
                public partial int Value { get; set; }
                [Sample.MarkMember] public partial int Value { get => 0; set { } }
            }
            """
        );

        var item = collected.Properties.Single();
        await Assert.That(item.Property.IsPartial).IsTrue();
        await Assert.That(item.Attributes.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Should_yield_one_property_item_with_both_attributes_when_both_partial_parts_are_attributed()
    {
        var collected = CollectMembers(
            """
            public partial class Widget
            {
                [Sample.MarkMember] public partial int Value { get; set; }
                [Sample.MarkMember] public partial int Value { get => 0; set { } }
            }
            """
        );

        var item = collected.Properties.Single();
        await Assert.That(item.Property.IsPartial).IsTrue();
        await Assert.That(item.Attributes.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Should_yield_nothing_for_an_indexer()
    {
        var collected = CollectMembers(
            """
            public class Widget
            {
                [Sample.MarkMember] public int this[int index] => index;
            }
            """
        );

        await Assert.That(collected.Properties.Count).IsEqualTo(0);
        await Assert.That(collected.Fields.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Should_yield_one_field_item_per_declarator()
    {
        var collected = CollectMembers(
            """
            public class Widget
            {
                [Sample.MarkMember] public int a, b;
                [Sample.MarkMember] public static readonly string C = "c";
                public int Unmarked;
            }
            """
        );

        await Assert
            .That(collected.Fields.Select(item => item.Field.Name).Order().ToArray())
            .IsEquivalentTo(new[] { "C", "a", "b" });
        await Assert
            .That(collected.Fields.Single(item => item.Field.Name == "C").Field.IsStatic)
            .IsTrue();
        await Assert
            .That(collected.Fields.All(item => item.ContainingType.Name == "Widget"))
            .IsTrue();
        await Assert.That(collected.Fields.All(item => item.Attributes.Count == 1)).IsTrue();
        await Assert.That(collected.Properties.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Should_locate_each_field_item_at_its_own_variable_identifier()
    {
        var collected = CollectMembers(
            """
            public partial class Widget
            {
                [Sample.MarkMember] public int first, second;
            }
            """
        );

        var first = collected.Fields.Single(item => item.Field.Name == "first");
        var second = collected.Fields.Single(item => item.Field.Name == "second");

        await Assert.That(first.Syntax.IsPartial).IsFalse();
        await Assert.That(first.Syntax.AreContainingTypesPartial).IsTrue();
        await Assert.That(first.Syntax.Location!.Span.Length).IsEqualTo("first".Length);
        await Assert.That(second.Syntax.Location!.Span.Length).IsEqualTo("second".Length);
        await Assert
            .That(first.Syntax.Location!.Span.Start)
            .IsNotEqualTo(second.Syntax.Location!.Span.Start);
    }

    [Test]
    public async Task Should_yield_nothing_for_a_field_targeted_attribute_on_an_auto_property()
    {
        var collected = CollectMembers(
            """
            public class Widget
            {
                [field: Sample.MarkMember] public int Value { get; set; }
            }
            """
        );

        await Assert.That(collected.Properties.Count).IsEqualTo(0);
        await Assert.That(collected.Fields.Count).IsEqualTo(0);
    }

    [Test]
    public void Should_stay_cacheable_for_property_and_field_steps()
    {
        var input = new GeneratorHarnessInput
        {
            Sources =
            [
                MarkMemberAttributeSource,
                """
                    public partial class Widget
                    {
                        [Sample.MarkMember] public int Auto { get; set; }
                        [Sample.MarkMember] public partial int Part { get; set; }
                        [Sample.MarkMember] public int a, b;
                    }
                    """,
                "public partial class Widget { public partial int Part { get => 0; set { } } }",
                "public class Unrelated { }",
            ],
        };

        GeneratorHarness.AssertCacheable(
            new MemberStepsGenerator(),
            input,
            [MemberStepsGenerator.PropertyStepName, MemberStepsGenerator.FieldStepName],
            new CacheabilityOptions { UnrelatedEditSourceIndex = 3 }
        );
    }

    private static (List<PropertyItem> Properties, List<FieldItem> Fields) CollectMembers(
        params string[] sources
    )
    {
        var generator = new CollectingMemberGenerator();
        GeneratorHarness.Run(
            generator,
            new GeneratorHarnessInput { Sources = [MarkMemberAttributeSource, .. sources] }
        );

        return (generator.Properties, generator.Fields);
    }

    private sealed record PropertyItem(
        PropertyModel Property,
        TypeModel ContainingType,
        SyntaxInfo Syntax,
        EquatableArray<AttributeModel> Attributes
    );

    private sealed record FieldItem(
        FieldModel Field,
        TypeModel ContainingType,
        SyntaxInfo Syntax,
        EquatableArray<AttributeModel> Attributes
    );

    private sealed class MemberStepsGenerator : IIncrementalGenerator
    {
        public const string PropertyStepName = "MemberStepsGenerator.Properties";
        public const string FieldStepName = "MemberStepsGenerator.Fields";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForPropertiesWithAttribute(
                    "Sample.MarkMemberAttribute",
                    PropertyStepName
                ),
                static (spc, value) =>
                    spc.AddSource(
                        $"{value.ContainingType.Name}.{value.Property.Name}.p.g.cs",
                        "// property\n"
                    )
            );
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForFieldsWithAttribute(
                    "Sample.MarkMemberAttribute",
                    FieldStepName
                ),
                static (spc, value) =>
                    spc.AddSource(
                        $"{value.ContainingType.Name}.{value.Field.Name}.f.g.cs",
                        "// field\n"
                    )
            );
        }
    }

    private sealed class CollectingMemberGenerator : IIncrementalGenerator
    {
        public List<PropertyItem> Properties { get; } = [];
        public List<FieldItem> Fields { get; } = [];

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForPropertiesWithAttribute(
                    "Sample.MarkMemberAttribute",
                    "Properties"
                ),
                (_, value) =>
                    Properties.Add(
                        new PropertyItem(
                            value.Property,
                            value.ContainingType,
                            value.Syntax,
                            value.Attributes
                        )
                    )
            );
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForFieldsWithAttribute(
                    "Sample.MarkMemberAttribute",
                    "Fields"
                ),
                (_, value) =>
                    Fields.Add(
                        new FieldItem(
                            value.Field,
                            value.ContainingType,
                            value.Syntax,
                            value.Attributes
                        )
                    )
            );
        }
    }

    [Test]
    public async Task Should_drop_methods_the_predicate_rejects()
    {
        var generator = new PredicateGenerator(
            static (node, _) =>
                node is MethodDeclarationSyntax method
                && method.Modifiers.Any(SyntaxKind.StaticKeyword)
        );
        GeneratorHarness.Run(
            generator,
            new GeneratorHarnessInput
            {
                Sources =
                [
                    MarkAttributeSource,
                    MarkMemberAttributeSource,
                    """
                    public class Widget
                    {
                        [Sample.Mark] public static void Keep() { }
                        [Sample.Mark] public void Drop() { }
                    }
                    """,
                ],
            }
        );

        await Assert.That(generator.Methods.ToArray()).IsEquivalentTo(new[] { "Keep" });
    }

    [Test]
    public async Task Should_drop_properties_and_fields_the_predicate_rejects()
    {
        var generator = new PredicateGenerator(
            static (node, _) =>
                node switch
                {
                    PropertyDeclarationSyntax property => property.Identifier.Text.StartsWith(
                        "Keep"
                    ),
                    VariableDeclaratorSyntax declarator => declarator.Identifier.Text.StartsWith(
                        "Keep"
                    ),
                    _ => false,
                }
        );
        GeneratorHarness.Run(
            generator,
            new GeneratorHarnessInput
            {
                Sources =
                [
                    MarkAttributeSource,
                    MarkMemberAttributeSource,
                    """
                    public class Widget
                    {
                        [Sample.MarkMember] public int KeepProperty { get; set; }
                        [Sample.MarkMember] public int DropProperty { get; set; }
                        [Sample.MarkMember] public int KeepField, DropField;
                    }
                    """,
                ],
            }
        );

        await Assert.That(generator.Properties.ToArray()).IsEquivalentTo(new[] { "KeepProperty" });
        await Assert.That(generator.Fields.ToArray()).IsEquivalentTo(new[] { "KeepField" });
    }

    [Test]
    public async Task Should_only_pass_nodes_of_the_discovery_kind_to_the_predicate()
    {
        var generator = new PredicateGenerator(static (_, _) => true);
        GeneratorHarness.Run(
            generator,
            new GeneratorHarnessInput
            {
                Sources =
                [
                    MarkAttributeSource,
                    MarkMemberAttributeSource,
                    """
                    public class Widget
                    {
                        [Sample.MarkMember] public int Value { get; set; }
                        [Sample.MarkMember] public int Field;
                        [Sample.Mark] public void Run()
                        {
                            [Sample.Mark] void Local() { }
                        }
                    }
                    """,
                ],
            }
        );

        await Assert.That(generator.Methods.ToArray()).IsEquivalentTo(new[] { "Run" });
        await Assert
            .That(generator.SeenMethodNodes.Distinct().ToArray())
            .IsEquivalentTo(new[] { nameof(MethodDeclarationSyntax) });
        await Assert
            .That(generator.SeenPropertyNodes.Distinct().ToArray())
            .IsEquivalentTo(new[] { nameof(PropertyDeclarationSyntax) });
        await Assert
            .That(generator.SeenFieldNodes.Distinct().ToArray())
            .IsEquivalentTo(new[] { nameof(VariableDeclaratorSyntax) });
    }

    private sealed class PredicateGenerator(Func<SyntaxNode, CancellationToken, bool> predicate)
        : IIncrementalGenerator
    {
        public List<string> Methods { get; } = [];
        public List<string> Properties { get; } = [];
        public List<string> Fields { get; } = [];
        public List<string> SeenMethodNodes { get; } = [];
        public List<string> SeenPropertyNodes { get; } = [];
        public List<string> SeenFieldNodes { get; } = [];

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForMethodsWithAttribute(
                    "Sample.MarkAttribute",
                    "Methods",
                    predicate: Recording(SeenMethodNodes)
                ),
                (_, value) => Methods.Add(value.Method.Name)
            );
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForPropertiesWithAttribute(
                    "Sample.MarkMemberAttribute",
                    "Properties",
                    predicate: Recording(SeenPropertyNodes)
                ),
                (_, value) => Properties.Add(value.Property.Name)
            );
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForFieldsWithAttribute(
                    "Sample.MarkMemberAttribute",
                    "Fields",
                    predicate: Recording(SeenFieldNodes)
                ),
                (_, value) => Fields.Add(value.Field.Name)
            );
        }

        private Func<SyntaxNode, CancellationToken, bool> Recording(List<string> seen)
        {
            return (node, token) =>
            {
                lock (seen)
                {
                    seen.Add(node.GetType().Name);
                }

                return predicate(node, token);
            };
        }
    }

    private const string SiblingSource = """
        public class Widget
        {
            [Sample.Mark] public void Run() { }
            [Sample.MarkMember] public int Marked { get; set; }
            [Sample.MarkMember] public int MarkedField;
            public string SiblingProperty { get; set; } = "";
            public int SiblingField;
            public void SiblingMethod() { }
            public event System.Action? SiblingEvent;
        }
        """;

    [Test]
    public async Task Should_leave_the_containing_type_members_empty_by_default()
    {
        var generator = new ContainingTypeMembersGenerator(includeContainingTypeMembers: false);
        GeneratorHarness.Run(
            generator,
            new GeneratorHarnessInput
            {
                Sources = [MarkAttributeSource, MarkMemberAttributeSource, SiblingSource],
            }
        );

        await Assert.That(generator.Seen.Count).IsEqualTo(3);
        foreach (var (_, type) in generator.Seen)
        {
            await Assert.That(type.Fields.Count).IsEqualTo(0);
            await Assert.That(type.Properties.Count).IsEqualTo(0);
            await Assert.That(type.Methods.Count).IsEqualTo(0);
            await Assert.That(type.Events.Count).IsEqualTo(0);
            await Assert.That(type.MemberNames.Count).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments("Method")]
    [Arguments("Property")]
    [Arguments("Field")]
    public async Task Should_fill_the_containing_type_members_when_requested(string kind)
    {
        var generator = new ContainingTypeMembersGenerator(includeContainingTypeMembers: true);
        GeneratorHarness.Run(
            generator,
            new GeneratorHarnessInput
            {
                Sources = [MarkAttributeSource, MarkMemberAttributeSource, SiblingSource],
            }
        );

        TypeModel type = generator.Seen.Single(item => item.Kind == kind).Type;

        await Assert.That(type.Methods.Any(m => m.Name == "SiblingMethod")).IsTrue();
        await Assert.That(type.Properties.Any(p => p.Name == "SiblingProperty")).IsTrue();
        await Assert.That(type.Fields.Any(f => f.Name == "SiblingField")).IsTrue();
        await Assert.That(type.Events.Any(e => e.Name == "SiblingEvent")).IsTrue();
        await Assert.That(type.MemberNames.Contains("SiblingMethod")).IsTrue();
    }

    [Test]
    public async Task Should_stay_cached_on_trivia_edits_and_rerun_when_a_sibling_member_is_added()
    {
        string[] steps =
        [
            ContainingTypeMembersGenerator.MethodStep,
            ContainingTypeMembersGenerator.PropertyStep,
            ContainingTypeMembersGenerator.FieldStep,
        ];
        var input = new GeneratorHarnessInput
        {
            Sources = [SiblingSource, MarkAttributeSource, MarkMemberAttributeSource],
        };

        GeneratorHarnessResult result = GeneratorHarness.AssertCacheable(
            new ContainingTypeMembersGenerator(includeContainingTypeMembers: true),
            input,
            steps
        );

        SyntaxTree tree = result.InputCompilation.SyntaxTrees.First();
        SyntaxTree edited = CSharpSyntaxTree.ParseText(
            tree.GetText()
                .ToString()
                .Replace("public int SiblingField;", "public int SiblingField; public int Added;"),
            (CSharpParseOptions)tree.Options,
            tree.FilePath
        );
        GeneratorDriver driver = result.Driver.RunGenerators(
            result.InputCompilation.ReplaceSyntaxTree(tree, edited)
        );
        var tracked = driver.GetRunResult().Results[0].TrackedSteps;

        foreach (var step in steps)
        {
            await Assert
                .That(
                    tracked[step]
                        .SelectMany(s => s.Outputs)
                        .Any(output => output.Reason == IncrementalStepRunReason.Modified)
                )
                .IsTrue();
        }
    }

    private sealed class ContainingTypeMembersGenerator(bool includeContainingTypeMembers)
        : IIncrementalGenerator
    {
        public const string MethodStep = "ContainingTypeMembers.Methods";
        public const string PropertyStep = "ContainingTypeMembers.Properties";
        public const string FieldStep = "ContainingTypeMembers.Fields";

        public List<(string Kind, TypeModel Type)> Seen { get; } = [];

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForMethodsWithAttribute(
                    "Sample.MarkAttribute",
                    MethodStep,
                    includeContainingTypeMembers
                ),
                (_, value) => Seen.Add(("Method", value.ContainingType))
            );
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForPropertiesWithAttribute(
                    "Sample.MarkMemberAttribute",
                    PropertyStep,
                    includeContainingTypeMembers
                ),
                (_, value) => Seen.Add(("Property", value.ContainingType))
            );
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForFieldsWithAttribute(
                    "Sample.MarkMemberAttribute",
                    FieldStep,
                    includeContainingTypeMembers
                ),
                (_, value) => Seen.Add(("Field", value.ContainingType))
            );
        }
    }

    private static Item[] Collect(params string[] sources)
    {
        var generator = new CollectingMethodGenerator();
        GeneratorHarness.Run(
            generator,
            new GeneratorHarnessInput { Sources = [MarkAttributeSource, .. sources] }
        );

        return generator.Collected.ToArray();
    }

    private sealed record Item(
        MethodModel Method,
        TypeModel ContainingType,
        SyntaxInfo Syntax,
        EquatableArray<AttributeModel> Attributes
    );

    private sealed class MethodGenerator : IIncrementalGenerator
    {
        public const string StepName = "MethodGenerator.Values";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForMethodsWithAttribute("Sample.MarkAttribute", StepName),
                static (spc, value) =>
                    spc.AddSource(
                        $"{value.ContainingType.Name}.{value.Method.Name}.g.cs",
                        $"// generated for {value.ContainingType.Name}.{value.Method.Name}\n"
                    )
            );
        }
    }

    private sealed class CollectingMethodGenerator : IIncrementalGenerator
    {
        public List<Item> Collected { get; } = [];

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.SyntaxProvider.ForMethodsWithAttribute("Sample.MarkAttribute", "Values"),
                (_, value) =>
                    Collected.Add(
                        new Item(value.Method, value.ContainingType, value.Syntax, value.Attributes)
                    )
            );
        }
    }
}
