using Haitch.Roslyn.Testing;

namespace Haitch.Roslyn.Samples.Tests;

public class HelloGeneratorTests
{
    [Test]
    public async Task Marked_partial_type_gets_a_Greeting_constant()
    {
        const string source = """
            namespace App;

            [Hello.Hello]
            public partial class Greeter { }
            """;

        var result = GeneratorHarness.Run(new HelloGenerator(), [source]);

        await Assert.That(result.Sources).ContainsKey("App.Greeter.Hello.g.cs");
        await Assert.That(result.Sources["App.Greeter.Hello.g.cs"]).Contains("Hello from Greeter");
    }

    [Test]
    public async Task Marked_partial_type_in_the_global_namespace_gets_a_Greeting_constant()
    {
        const string source = """
            [Hello.Hello]
            public partial class Greeter { }
            """;

        var result = GeneratorHarness.Run(new HelloGenerator(), [source]);

        await Assert.That(result.Sources["Greeter.Hello.g.cs"]).Contains("Hello from Greeter");
    }

    [Test]
    public void Output_is_cacheable()
    {
        const string source = """
            namespace App;

            [Hello.Hello]
            public partial class Greeter { }
            """;

        GeneratorHarness.AssertCacheable(new HelloGenerator(), [source], "HelloGenerator.Types");
    }
}
