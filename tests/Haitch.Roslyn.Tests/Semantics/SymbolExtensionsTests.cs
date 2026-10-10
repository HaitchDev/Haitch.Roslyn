using Haitch.Roslyn.Semantics;
using Haitch.Roslyn.Tests.Models;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Semantics;

public class SymbolExtensionsTests
{
    [Test]
    public async Task Should_find_an_attribute_on_a_type()
    {
        bool found = Type("Ns.Options").HasAttribute("System.FlagsAttribute");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_find_a_generic_attribute_by_its_definitions_metadata_name()
    {
        bool found = Type("Ns.Marked").HasAttribute("Ns.MarkAttribute`1");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_find_an_attribute_on_a_method()
    {
        bool found = Member("Method").HasAttribute("System.ObsoleteAttribute");

        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task Should_not_find_an_attribute_the_symbol_does_not_carry()
    {
        bool found = Type("Ns.Plain").HasAttribute("System.FlagsAttribute");

        await Assert.That(found).IsFalse();
    }

    private static ISymbol Member(string name) => Type("Ns.Plain").GetMembers(name).Single();

    private static INamedTypeSymbol Type(string metadataName) =>
        CompilationHelper.GetNamedTypeSymbol(
            """
            namespace Ns
            {
                [System.Flags]
                enum Options
                {
                    None = 0,
                }

                class MarkAttribute<T> : System.Attribute { }

                [Mark<int>]
                class Marked { }

                class Plain
                {
                    [System.Obsolete]
                    public void Method() { }
                }
            }
            """,
            metadataName
        );
}
