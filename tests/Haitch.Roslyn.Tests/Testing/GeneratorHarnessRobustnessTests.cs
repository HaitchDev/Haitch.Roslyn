using Haitch.Roslyn.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Testing;

public class GeneratorHarnessRobustnessTests
{
    private sealed class EmitGenerator(string body) : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var names = context
                .CompilationProvider.Select(static (c, _) => c.SyntaxTrees.Count())
                .WithTrackingName("Count");
            context.RegisterSourceOutput(names, (ctx, _) => ctx.AddSource("Out.g.cs", body));
        }
    }

    private sealed class OptionLookupGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var value = context
                .AnalyzerConfigOptionsProvider.Combine(context.CompilationProvider)
                .Select(
                    static (pair, _) =>
                    {
                        var (options, compilation) = pair;
                        options
                            .GetOptions(compilation.SyntaxTrees.First())
                            .TryGetValue("DOTNET_DIAGNOSTIC.X.SEVERITY", out var v);
                        return v ?? "<none>";
                    }
                );
            context.RegisterSourceOutput(value, (ctx, v) => ctx.AddSource("Value.g.cs", $"// {v}"));
        }
    }

    [Test]
    public async Task AllowInputErrors_LocationLessError_IsAnInputError()
    {
        // A reference that cannot be opened is CS0009, which the compiler reports without any location.
        MetadataReference[] references = [new UnreadableReference()];

        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = GeneratorHarness.Run(
                new EmitGenerator("class Ok { }"),
                new GeneratorHarnessInput
                {
                    Sources = ["class A { }"],
                    AdditionalReferences = references,
                }
            )
        );
        await Assert.That(ex.Message).Contains("Input source errors:");
        await Assert.That(ex.Message).Contains("CS0009");

        var result = GeneratorHarness.Run(
            new EmitGenerator("class Ok { }"),
            new GeneratorHarnessInput
            {
                Sources = ["class A { }"],
                AdditionalReferences = references,
                AllowInputErrors = true,
            }
        );
        var error = result.InputDiagnostics.Single();
        await Assert.That(error.Id).IsEqualTo("CS0009");
        await Assert.That(error.Location).IsEqualTo(Location.None);
    }

    private sealed class UnreadableReference()
        : PortableExecutableReference(MetadataReferenceProperties.Assembly, "unreadable.dll")
    {
        protected override DocumentationProvider CreateDocumentationProvider() =>
            DocumentationProvider.Default;

        protected override Metadata GetMetadataImpl() => throw new IOException("unreadable");

        protected override PortableExecutableReference WithPropertiesImpl(
            MetadataReferenceProperties properties
        ) => this;
    }

    [Test]
    public async Task PerFileOptions_LookupIsCaseInsensitive()
    {
        var result = GeneratorHarness.Run(
            new OptionLookupGenerator(),
            new GeneratorHarnessInput
            {
                Sources = ["class A { }"],
                PerFileOptions = new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["Source0.cs"] = new Dictionary<string, string>
                    {
                        ["dotnet_diagnostic.x.severity"] = "error",
                    },
                },
            }
        );

        await Assert.That(result.Sources["Value.g.cs"]).IsEqualTo("// error");
    }

    private sealed class OneShotEnumerable(params string[] items) : IEnumerable<string>
    {
        private bool _enumerated;

        public IEnumerator<string> GetEnumerator()
        {
            if (_enumerated)
            {
                throw new InvalidOperationException("Enumerated more than once.");
            }

            _enumerated = true;
            return ((IEnumerable<string>)items).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static IEnumerable<string> OneShot(params string[] sources) =>
        new OneShotEnumerable(sources);

    [Test]
    public async Task LegacyAssertCacheable_AcceptsOneShotIterator()
    {
        var result = GeneratorHarness.AssertCacheable(
            new EmitGenerator("class Ok { }"),
            OneShot("class A { }"),
            "Count"
        );

        await Assert.That(result.Sources.ContainsKey("Out.g.cs")).IsTrue();
    }

    [Test]
    public async Task Default_InputErrorMessageIsExact()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = GeneratorHarness.Run(
                new EmitGenerator("class Ok { }"),
                new GeneratorHarnessInput { Sources = ["class A : Missing { }"] }
            )
        );

        await Assert
            .That(ex.Message)
            .IsEqualTo(
                "Input source errors:"
                    + Environment.NewLine
                    + "Source0.cs:1: CS0246: The type or namespace name 'Missing' could not be found (are you missing a using directive or an assembly reference?)"
            );
    }

    [Test]
    public async Task AllowInputErrors_GeneratedCodeThatBreaksTheInterfaceContractThrows()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = GeneratorHarness.Run(
                new EmitGenerator("partial class Foo : IFoo { }"),
                new GeneratorHarnessInput
                {
                    Sources =
                    [
                        "interface IFoo { void M(); } partial class Foo { } class Bad : Missing { }",
                    ],
                    AllowInputErrors = true,
                }
            )
        );

        await Assert.That(ex.Message).Contains("Generated output errors:");
        await Assert.That(ex.Message).Contains("CS0535");
    }

    [Test]
    public async Task AllowInputErrors_NewErrorLocatedInUserTreeIsAnOutputError()
    {
        // CS0103 on the user's tree turns into CS1955 once the generated member exists: a new error in input code.
        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = GeneratorHarness.Run(
                new EmitGenerator("partial class Foo { int Bar; }"),
                new GeneratorHarnessInput
                {
                    Sources = ["partial class Foo { int x = Bar(); }"],
                    AllowInputErrors = true,
                }
            )
        );

        await Assert.That(ex.Message).Contains("Generated output errors:");
        await Assert.That(ex.Message).Contains("Source0.cs");
        await Assert.That(ex.Message).Contains("CS1955");
    }

    [Test]
    public async Task AssertSourceFile_RemappedCallerPath_ThrowsGeneratorTestException()
    {
        var result = GeneratorHarness.Run(
            new EmitGenerator("class Ok { }"),
            new GeneratorHarnessInput { Sources = ["class A { }"] }
        );

        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = result.AssertSourceFile(
                "Out.g.cs",
                "Expected/Out.g.cs.txt",
                "/_/tests/Remapped/Tests.cs"
            )
        );
        await Assert.That(ex.Message).Contains("remapped");
        await Assert.That(ex.Message).Contains("absolute");
    }

    [Test]
    public async Task AssertSourceFile_NonexistentCallerDirectory_ThrowsGeneratorTestException()
    {
        var result = GeneratorHarness.Run(
            new EmitGenerator("class Ok { }"),
            new GeneratorHarnessInput { Sources = ["class A { }"] }
        );
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Tests.cs");

        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = result.AssertSourceFile("Out.g.cs", "Expected/Out.g.cs.txt", missing)
        );
        await Assert.That(ex.Message).Contains("absolute");
        await Assert.That(Directory.Exists(Path.GetDirectoryName(missing)!)).IsFalse();
    }

    [Test]
    public async Task GlobalOptions_KeysDifferingOnlyByCase_Throw()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = GeneratorHarness.Run(
                new EmitGenerator("class Ok { }"),
                new GeneratorHarnessInput
                {
                    Sources = ["class A { }"],
                    GlobalOptions = new Dictionary<string, string>
                    {
                        ["build_property.Root"] = "a",
                        ["BUILD_PROPERTY.ROOT"] = "b",
                    },
                }
            )
        );

        await Assert.That(ex.Message).Contains("build_property.Root");
        await Assert.That(ex.Message).Contains("BUILD_PROPERTY.ROOT");
    }

    [Test]
    public async Task PerFileOptions_KeysDifferingOnlyByCase_Throw()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = GeneratorHarness.Run(
                new EmitGenerator("class Ok { }"),
                new GeneratorHarnessInput
                {
                    Sources = ["class A { }"],
                    PerFileOptions = new Dictionary<string, IReadOnlyDictionary<string, string>>
                    {
                        ["Source0.cs"] = new Dictionary<string, string>
                        {
                            ["k.Value"] = "a",
                            ["K.VALUE"] = "b",
                        },
                    },
                }
            )
        );

        await Assert.That(ex.Message).Contains("k.Value");
        await Assert.That(ex.Message).Contains("K.VALUE");
    }

    [Test]
    public async Task Run_NullSourcesArray_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            _ = GeneratorHarness.Run(new EmitGenerator("class Ok { }"), (string[])null!)
        );

        await Assert.That(ex.ParamName).IsEqualTo("sources");
    }

    [Test]
    public async Task Run_NullSourceElement_ThrowsNamingTheIndex()
    {
        var ex = Assert.Throws<GeneratorTestException>(() =>
            _ = GeneratorHarness.Run(
                new EmitGenerator("class Ok { }"),
                new GeneratorHarnessInput { Sources = ["class A { }", null!] }
            )
        );

        await Assert.That(ex.Message).Contains("Sources[1] is null.");
    }
}
