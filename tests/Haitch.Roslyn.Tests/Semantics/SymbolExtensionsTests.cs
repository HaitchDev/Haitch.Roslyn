using Haitch.Roslyn.Semantics;
using Haitch.Roslyn.Tests.Models;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Semantics;

public class SymbolExtensionsTests
{
    [Test]
    public async Task Should_not_find_an_attribute_the_symbol_does_not_carry()
    {
        bool found = Type("Ns.Plain").HasAttribute("System.FlagsAttribute");

        await Assert.That(found).IsFalse();
    }

    private static INamedTypeSymbol Type(string metadataName) =>
        CompilationHelper.GetNamedTypeSymbol(
            """
            namespace Ns
            {
                class Plain { }
            }
            """,
            metadataName
        );
}
