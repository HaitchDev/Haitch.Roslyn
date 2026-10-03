using System.Collections.Immutable;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Testing;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Testing;

public class CachingHazardWalkerTests
{
    private const string Source = """
        using System;
        namespace Example;
        public sealed class MarkAttribute(Type type, DayOfWeek day, string[] names, int number) : Attribute;
        [Mark(typeof(string), DayOfWeek.Monday, new[] { "a", "b" }, 3)]
        public class Sample
        {
            public int Field;
            public string Name { get; set; } = "";
            public void Run(int value) { }
        }
        """;

    private sealed record Member(ISymbol Symbol);

    private sealed record Outer(EquatableArray<Member> Members);

    private sealed record NodeHolder(SyntaxNode Node);

    private sealed record TreeHolder(SyntaxTree Tree);

    private sealed record ModelHolder(SemanticModel Model);

    private sealed record LocationHolder(Location Where);

    private sealed record CompilationHolder(Compilation Compilation);

    private sealed record ImmutableHolder(ImmutableArray<string> Items);

    private sealed record ArrayHolder(string[] Items);

    private sealed record PlainHolder(Plain Child);

    private sealed class Plain;

    private sealed record Chain(Chain? Next, int Id);

    private sealed record DiagnosticHolder(Diagnostic Diagnostic);

    private sealed record Cyclic(string Name)
    {
        public Cyclic? Next { get; set; }
    }

    private sealed record SafeLeaves(
        int Number,
        string Text,
        DiagnosticSeverity Severity,
        DiagnosticDescriptor Descriptor,
        LocalizableString Title,
        string? Missing
    );

    private static (CSharpCompilation Compilation, INamedTypeSymbol Type) Compile()
    {
        var compilation = CompilationHelper.Compile(Source);
        return (compilation, compilation.GetTypeByMetadataName("Example.Sample")!);
    }

    [Test]
    public async Task Should_report_the_full_member_path_when_a_symbol_is_nested_in_a_tuple_and_array()
    {
        var (_, type) = Compile();
        object model = (1, new Outer(new EquatableArray<Member>([new Member(type)])));

        await Assert.That(CachingHazardWalker.Find(model)).IsEqualTo("Item2.Members[0].Symbol");
    }

    [Test]
    public async Task Should_report_a_syntax_node()
    {
        var (compilation, _) = Compile();
        var node = compilation.SyntaxTrees.Single().GetRoot();

        await Assert.That(CachingHazardWalker.Find(new NodeHolder(node))).IsEqualTo("Node");
    }

    [Test]
    public async Task Should_report_a_syntax_tree()
    {
        var (compilation, _) = Compile();

        await Assert
            .That(CachingHazardWalker.Find(new TreeHolder(compilation.SyntaxTrees.Single())))
            .IsEqualTo("Tree");
    }

    [Test]
    public async Task Should_report_a_semantic_model()
    {
        var (compilation, _) = Compile();
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());

        await Assert.That(CachingHazardWalker.Find(new ModelHolder(model))).IsEqualTo("Model");
    }

    [Test]
    public async Task Should_report_a_location()
    {
        var (compilation, _) = Compile();
        var location = compilation.SyntaxTrees.Single().GetRoot().GetLocation();

        await Assert
            .That(CachingHazardWalker.Find(new LocationHolder(location)))
            .IsEqualTo("Where");
    }

    [Test]
    public async Task Should_report_a_compilation()
    {
        var (compilation, _) = Compile();

        await Assert
            .That(CachingHazardWalker.Find(new CompilationHolder(compilation)))
            .IsEqualTo("Compilation");
    }

    [Test]
    public async Task Should_report_an_immutable_array()
    {
        await Assert.That(CachingHazardWalker.Find(new ImmutableHolder(["a"]))).IsEqualTo("Items");
    }

    [Test]
    public async Task Should_report_an_array()
    {
        await Assert.That(CachingHazardWalker.Find(new ArrayHolder(["a"]))).IsEqualTo("Items");
    }

    [Test]
    public async Task Should_report_a_class_without_value_equality()
    {
        await Assert
            .That(CachingHazardWalker.Find(new PlainHolder(new Plain())))
            .IsEqualTo("Child");
    }

    [Test]
    public async Task Should_report_a_hazardous_root_with_a_root_path()
    {
        var (_, type) = Compile();

        await Assert.That(CachingHazardWalker.Find(type)).IsEqualTo("(root)");
    }

    [Test]
    public async Task Should_report_nothing_for_null_and_safe_leaves()
    {
        var descriptor = new DiagnosticDescriptor(
            "T001",
            "title",
            "message",
            "cat",
            DiagnosticSeverity.Warning,
            true
        );
        var leaves = new SafeLeaves(
            1,
            "text",
            DiagnosticSeverity.Error,
            descriptor,
            (LocalizableString)"title",
            null
        );

        await Assert.That(CachingHazardWalker.Find(null)).IsNull();
        await Assert.That(CachingHazardWalker.Find(leaves)).IsNull();
        await Assert.That(CachingHazardWalker.Find((1, "two", DiagnosticSeverity.Info))).IsNull();
    }

    [Test]
    public async Task Should_stop_on_reference_cycles()
    {
        var cyclic = new Cyclic("a");
        cyclic.Next = cyclic;

        await Assert.That(CachingHazardWalker.Find(cyclic)).IsNull();
    }

    [Test]
    public async Task Should_walk_an_unset_equatable_array_without_error()
    {
        await Assert.That(CachingHazardWalker.Find(new Outer(default))).IsNull();
    }

    [Test]
    public async Task Should_report_nothing_for_models_built_from_this_repos_types()
    {
        var (_, type) = Compile();
        var typeModel = TypeModel.From(type, includeMembers: true);
        var attributes = typeModel.Attributes;
        var syntaxInfo = new SyntaxInfo(true, true, null);

        await Assert.That(CachingHazardWalker.Find(typeModel)).IsNull();
        await Assert.That(CachingHazardWalker.Find((typeModel, syntaxInfo, attributes))).IsNull();
    }

    [Test]
    public async Task Should_report_a_diagnostic()
    {
        var diagnostic = Diagnostic.Create(
            "T001",
            "cat",
            "message",
            DiagnosticSeverity.Warning,
            DiagnosticSeverity.Warning,
            true,
            1
        );

        await Assert
            .That(CachingHazardWalker.Find(new DiagnosticHolder(diagnostic)))
            .IsEqualTo("Diagnostic");
    }

    [Test]
    public async Task Should_report_a_hazard_when_the_walk_is_deeper_than_the_limit()
    {
        Chain? chain = null;
        for (var i = 0; i < 300; i++)
        {
            chain = new Chain(chain, i);
        }

        var path = CachingHazardWalker.Find(chain);

        await Assert.That(path).IsNotNull();
        await Assert.That(path!.Split('.').Length).IsEqualTo(257);
    }

    [Test]
    public async Task Should_walk_a_chain_within_the_limit()
    {
        Chain? chain = null;
        for (var i = 0; i < 200; i++)
        {
            chain = new Chain(chain, i);
        }

        await Assert.That(CachingHazardWalker.Find(chain)).IsNull();
    }

    private sealed class NameEqual(string name, ISymbol symbol)
    {
        public string Name { get; } = name;

        public ISymbol Symbol { get; } = symbol;

        public override bool Equals(object? obj) => obj is NameEqual other && other.Name == Name;

        public override int GetHashCode() => Name.GetHashCode();
    }

    private sealed class UnchangedHazardGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var tracked = context
                .SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax,
                    static (ctx, ct) =>
                    {
                        var symbol = ctx.SemanticModel.GetDeclaredSymbol(
                            (ClassDeclarationSyntax)ctx.Node,
                            ct
                        )!;
                        return new NameEqual(symbol.Name, symbol);
                    }
                )
                .WithTrackingName("model");
            context.RegisterSourceOutput(
                tracked,
                static (ctx, m) => ctx.AddSource(m.Name + ".g.cs", "class G { }")
            );
        }
    }

    [Test]
    public async Task Should_report_a_hazard_in_an_output_that_reruns_unchanged()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(
                new UnchangedHazardGenerator(),
                ["class A { }\n"],
                "model"
            )
        );

        await Assert
            .That(ex.Message)
            .Contains("Step 'model' output 0 holds a caching hazard at Symbol");
        await Assert.That(ex.Message).DoesNotContain("expected Cached");
    }

    private sealed class SymbolInModelGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var tracked = context
                .SyntaxProvider.CreateSyntaxProvider(
                    static (node, _) => node is ClassDeclarationSyntax,
                    static (ctx, ct) =>
                        new Member(
                            ctx.SemanticModel.GetDeclaredSymbol(
                                (ClassDeclarationSyntax)ctx.Node,
                                ct
                            )!
                        )
                )
                .WithTrackingName("model");
            context.RegisterSourceOutput(
                tracked,
                static (ctx, m) => ctx.AddSource(m.Symbol.Name + ".g.cs", "class G { }")
            );
        }
    }

    [Test]
    public async Task Should_name_step_and_member_path_when_assert_cacheable_meets_a_hazard()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            GeneratorHarness.AssertCacheable(
                new SymbolInModelGenerator(),
                ["class A { }\n"],
                "model"
            )
        );

        await Assert.That(ex.Message).Contains("Step 'model'");
        await Assert.That(ex.Message).Contains("caching hazard at Symbol");
    }
}
