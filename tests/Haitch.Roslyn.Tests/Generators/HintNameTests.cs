using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
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
