using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Testing;
using Haitch.Roslyn.Tests.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Generators;

public class HintNameTests
{
    private const string Shapes = """
                                  namespace My.App
                                  {
                                      public class Foo { }
                                      public class Foo<T> { }
                                      public class Outer<T> { public class Inner<U> { } public class Plain { } }
                                  }
                                  namespace A { public class B { public class X { } } }
                                  public class Global { }
                                  public class Global<T, U> { }
                                  """;

    private const string NamespacedSource = "namespace A.B { public class X { } }";

    private const string CaseSource = "namespace My.App { public class Foo { } public class foo { } }";

    private const string GlobalSource = "public class Foo { }";

    [Test]
    [Arguments("My.App.Foo", "My.App.Foo.Equality.g.cs")]
    [Arguments("My.App.Foo`1", "My.App.Foo`1.Equality.g.cs")]
    [Arguments("My.App.Outer`1+Inner`1", "My.App.Outer`1+Inner`1.Equality.g.cs")]
    [Arguments("My.App.Outer`1+Plain", "My.App.Outer`1+Plain.Equality.g.cs")]
    [Arguments("Global", "Global.Equality.g.cs")]
    [Arguments("Global`2", "Global`2.Equality.g.cs")]
    public async Task Should_format_the_hint_name_for_each_type_shape(string metadataName, string expected)
    {
        TypeModel model = ModelFor(metadataName);

        await Assert.That(HintName.For(model, "Equality")).IsEqualTo(expected);
    }

    [Test]
    public async Task Should_distinguish_a_namespace_segment_from_a_nesting_level()
    {
        string namespaced = HintName.For(
            TypeModel.From(CompilationHelper.GetNamedTypeSymbol(NamespacedSource, "A.B.X")),
            "Equality");
        string nested = HintName.For(ModelFor("A.B+X"), "Equality");

        await Assert.That(namespaced).IsNotEqualTo(nested);
    }

    [Test]
    [Arguments("")]
    [Arguments("Bad Suffix")]
    [Arguments("a/b")]
    [Arguments("Equality.cs")]
    [Arguments("Equality`1")]
    [Arguments(".Equality")]
    [Arguments("Equality.")]
    [Arguments("Equal..ity")]
    public async Task Should_throw_when_the_suffix_is_invalid(string suffix)
    {
        TypeModel model = ModelFor("My.App.Foo");

        await Assert.That(() => HintName.For(model, suffix)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_throw_when_the_suffix_is_null()
    {
        TypeModel model = ModelFor("My.App.Foo");

        await Assert.That(() => HintName.For(model, null!)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_give_case_only_variants_distinct_case_insensitive_names_when_disambiguating()
    {
        string upper = HintName.For(CaseModel("My.App.Foo"), "Equality", disambiguateCase: true);
        string lower = HintName.For(CaseModel("My.App.foo"), "Equality", disambiguateCase: true);

        await Assert.That(upper.ToLowerInvariant()).IsNotEqualTo(lower.ToLowerInvariant());
    }

    [Test]
    public async Task Should_leave_case_only_variants_colliding_by_default()
    {
        string upper = HintName.For(CaseModel("My.App.Foo"), "Equality");
        string lower = HintName.For(CaseModel("My.App.foo"), "Equality");

        await Assert.That(upper.ToLowerInvariant()).IsEqualTo(lower.ToLowerInvariant());
    }

    [Test]
    [Arguments("My.App.Foo", "My.App.Foo.abdd334c.Equality.g.cs")]
    [Arguments("My.App.foo", "My.App.foo.49529bac.Equality.g.cs")]
    public async Task Should_place_a_pinned_hash_before_the_suffix_when_disambiguating(
        string metadataName,
        string expected)
    {
        await Assert.That(HintName.For(CaseModel(metadataName), "Equality", disambiguateCase: true))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task Should_place_a_pinned_hash_for_a_type_in_the_global_namespace()
    {
        TypeModel model = TypeModel.From(CompilationHelper.GetNamedTypeSymbol(GlobalSource, "Foo"));

        await Assert.That(HintName.For(model, "Equality", disambiguateCase: true))
            .IsEqualTo("Foo.0c7e1677.Equality.g.cs");
    }

    [Test]
    public async Task Should_use_the_same_hash_segment_for_different_suffixes()
    {
        TypeModel model = CaseModel("My.App.Foo");

        await Assert.That(HintName.For(model, "Equality", disambiguateCase: true))
            .IsEqualTo("My.App.Foo.abdd334c.Equality.g.cs");
        await Assert.That(HintName.For(model, "Json", disambiguateCase: true))
            .IsEqualTo("My.App.Foo.abdd334c.Json.g.cs");
    }

    [Test]
    public async Task Should_hash_nested_and_generic_names_when_disambiguating()
    {
        TypeModel model = ModelFor("My.App.Outer`1+Inner`1");

        await Assert.That(HintName.For(model, "Equality", disambiguateCase: true))
            .IsEqualTo("My.App.Outer`1+Inner`1.2074b1e4.Equality.g.cs");
    }

    [Test]
    public async Task Should_produce_two_files_when_a_generator_adds_case_only_variants_with_disambiguation()
    {
        GeneratorHarnessResult result = GeneratorHarness.Run(new CaseVariantGenerator(true), [CaseSource]);

        await Assert.That(result.RunResult.GeneratedTrees.Length).IsEqualTo(2);
    }

    [Test]
    public async Task Should_make_AddSource_reject_case_only_variants_without_disambiguation()
    {
        await Assert.That(() => GeneratorHarness.Run(new CaseVariantGenerator(false), [CaseSource]))
            .Throws<GeneratorTestException>();
    }

    [Test]
    public async Task Should_produce_names_that_AddSource_accepts_for_every_type_shape()
    {
        CSharpCompilation compilation = CompilationHelper.Compile(Shapes);
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(new AddSourceGenerator());

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var result = driver.GetRunResult();

        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.GeneratedTrees.Length).IsEqualTo(9);
    }

    private static TypeModel ModelFor(string metadataName)
    {
        return TypeModel.From(CompilationHelper.GetNamedTypeSymbol(Shapes, metadataName));
    }

    private static TypeModel CaseModel(string metadataName)
    {
        return TypeModel.From(CompilationHelper.GetNamedTypeSymbol(CaseSource, metadataName));
    }

    private sealed class CaseVariantGenerator(bool disambiguateCase) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(context.CompilationProvider, (ctx, compilation) =>
            {
                foreach (string name in new[] { "My.App.Foo", "My.App.foo" })
                {
                    TypeModel model = TypeModel.From(compilation.GetTypeByMetadataName(name)!);
                    ctx.AddSource(HintName.For(model, "Equality", disambiguateCase), "// generated");
                }
            });
        }
    }

    private sealed class AddSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(context.CompilationProvider, (ctx, compilation) =>
            {
                foreach (INamedTypeSymbol type in Walk(compilation.Assembly.GlobalNamespace))
                {
                    ctx.AddSource(HintName.For(TypeModel.From(type), "Equality"), "// generated");
                }
            });
        }

        private static IEnumerable<INamedTypeSymbol> Walk(INamespaceOrTypeSymbol container)
        {
            foreach (ISymbol member in container.GetMembers())
            {
                if (member is INamedTypeSymbol type)
                {
                    yield return type;

                    foreach (INamedTypeSymbol nested in Walk(type))
                    {
                        yield return nested;
                    }
                }
                else if (member is INamespaceSymbol ns)
                {
                    foreach (INamedTypeSymbol inner in Walk(ns))
                    {
                        yield return inner;
                    }
                }
            }
        }
    }
}
