using System.Collections.Immutable;
using System.Text;
using Haitch.Roslyn.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Haitch.Roslyn.Tests.Generators;

public class PostInitializationExtensionsTests
{
    [Test]
    public async Task Should_emit_a_marker_attribute_source_with_the_chosen_namespace_targets_and_properties()
    {
        MarkerGenerator generator = new(
            "Sample.Generated",
            "GenerateXAttribute",
            AttributeTargets.Class | AttributeTargets.Struct,
            allowMultiple: true,
            inherited: true,
            properties: [("string", "Name"), ("bool", "IsEnabled")]);

        GeneratorDriverRunResult result = RunGenerator(generator);

        string generatedSource = ConcatTrees(result);

        await Assert.That(generatedSource).Contains("namespace Sample.Generated");
        await Assert.That(generatedSource).Contains("internal sealed class GenerateXAttribute");
        await Assert.That(generatedSource).Contains("global::System.AttributeTargets.Class");
        await Assert.That(generatedSource).Contains("global::System.AttributeTargets.Struct");
        await Assert.That(generatedSource).Contains("AllowMultiple = true");
        await Assert.That(generatedSource).Contains("Inherited = true");
        await Assert.That(generatedSource).Contains("public string Name { get; set; }");
        await Assert.That(generatedSource).Contains("public bool IsEnabled { get; set; }");
        await Assert.That(generatedSource).Contains("[global::Microsoft.CodeAnalysis.EmbeddedAttribute]");
    }

    [Test]
    public async Task Should_produce_an_internal_embedded_attribute_that_compiles_when_applied()
    {
        MarkerGenerator generator = new("Sample", "MarkerAttribute", AttributeTargets.Class);

        (Compilation compilation, ImmutableArray<Diagnostic> diagnostics) = RunAndCompile(
            generator,
            "Consumer",
            "namespace Consumer { [Sample.MarkerAttribute] class Usage { } }");

        await Assert.That(HasDiagnosticsAtWarningOrAbove(diagnostics)).IsFalse();

        INamedTypeSymbol? attributeSymbol = compilation.GetTypeByMetadataName("Sample.MarkerAttribute");

        await Assert.That(attributeSymbol).IsNotNull();
        await Assert.That(attributeSymbol!.DeclaredAccessibility).IsEqualTo(Accessibility.Internal);
        await Assert.That(HasEmbeddedAttribute(attributeSymbol)).IsTrue();
    }

    [Test]
    public async Task Should_allow_two_assemblies_emitting_the_same_marker_to_be_referenced_together_without_CS0436()
    {
        MarkerGenerator generator = new("Shared", "MarkerAttribute", AttributeTargets.Class);

        (Compilation library, ImmutableArray<Diagnostic> libraryDiagnostics) = RunAndCompile(
            generator,
            "Library",
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Consumer\")]");

        await Assert.That(HasDiagnosticsAtWarningOrAbove(libraryDiagnostics)).IsFalse();

        MetadataReference libraryReference = EmitToReference(library);

        (Compilation consumer, ImmutableArray<Diagnostic> consumerDiagnostics) = RunAndCompile(
            generator,
            "Consumer",
            "namespace Consumer { [Shared.MarkerAttribute] class Usage { } }",
            [libraryReference]);

        // The hand-rolled EmbeddedAttribute helper (needed because Roslyn 4.8 has no built-in
        // AddEmbeddedAttributeDefinition) cannot mark itself with [EmbeddedAttribute] without
        // reintroducing the CS0579 multi-generator collision that AddEmbeddedAttributeDefinition
        // avoids, so it does not suppress its own CS0436 once InternalsVisibleTo makes both
        // copies mutually visible; that residual warning is benign (the compiler picks the local
        // definition) and orthogonal to the marker attribute, which is what this test verifies.
        await Assert.That(HasCs0436For(consumerDiagnostics, "Shared.MarkerAttribute")).IsFalse();
    }

    [Test]
    public async Task
        Should_not_raise_CS0101_when_two_generators_in_one_project_each_add_the_embedded_attribute_definition()
    {
        MarkerGenerator generatorA = new("Sample.First", "FirstAttribute", AttributeTargets.Class);
        MarkerGenerator generatorB = new("Sample.Second", "SecondAttribute", AttributeTargets.Method);

        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generatorA, generatorB);
        CSharpCompilation compilation = CreateCompilation("TwoGenerators", []);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out _);

        await Assert.That(HasDiagnosticsAtWarningOrAbove(outputCompilation.GetDiagnostics())).IsFalse();
    }

    [Test]
    public async Task Should_compile_without_warnings_when_a_property_is_a_non_nullable_reference_type()
    {
        MarkerGenerator generator = new(
            "Sample.NonNullable",
            "MarkerAttribute",
            AttributeTargets.Class,
            properties: [("string", "Name")]);

        (_, ImmutableArray<Diagnostic> diagnostics) = RunAndCompile(generator, "NonNullable");

        await Assert.That(HasDiagnosticsAtWarningOrAbove(diagnostics)).IsFalse();
    }

    [Test]
    public async Task Should_throw_when_the_namespace_is_not_a_valid_identifier()
    {
        IncrementalGeneratorPostInitializationContext context = default;

        await Assert.That(() => context.AddMarkerAttribute(
            "Marker.g.cs",
            "1Invalid.Namespace",
            "MarkerAttribute",
            AttributeTargets.Class)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_throw_when_the_attribute_name_is_not_a_valid_identifier()
    {
        IncrementalGeneratorPostInitializationContext context = default;

        await Assert.That(() => context.AddMarkerAttribute(
            "Marker.g.cs",
            "Sample",
            "not an identifier",
            AttributeTargets.Class)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_throw_when_a_property_name_is_not_a_valid_identifier()
    {
        IncrementalGeneratorPostInitializationContext context = default;

        await Assert.That(() => context.AddMarkerAttribute(
            "Marker.g.cs",
            "Sample",
            "MarkerAttribute",
            AttributeTargets.Class,
            properties: [("string", "not valid")])).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_throw_when_a_property_type_is_not_fully_qualified_or_a_keyword_type()
    {
        IncrementalGeneratorPostInitializationContext context = default;

        await Assert.That(() => context.AddMarkerAttribute(
            "Marker.g.cs",
            "Sample",
            "MarkerAttribute",
            AttributeTargets.Class,
            properties: [("System.Type", "Value")])).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_throw_when_targets_is_zero()
    {
        IncrementalGeneratorPostInitializationContext context = default;

        await Assert.That(() => context.AddMarkerAttribute(
            "Marker.g.cs",
            "Sample",
            "MarkerAttribute",
            (AttributeTargets)0)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_throw_when_targets_has_undefined_bits()
    {
        IncrementalGeneratorPostInitializationContext context = default;

        await Assert.That(() => context.AddMarkerAttribute(
            "Marker.g.cs",
            "Sample",
            "MarkerAttribute",
            (AttributeTargets)(1 << 30))).Throws<ArgumentException>();
    }

    private static GeneratorDriverRunResult RunGenerator(IIncrementalGenerator generator)
    {
        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        CSharpCompilation compilation = CreateCompilation("Empty", []);

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        return driver.GetRunResult();
    }

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) RunAndCompile(
        IIncrementalGenerator generator,
        string assemblyName,
        string? consumerSource = null,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        List<SyntaxTree> trees = [];

        if (consumerSource is not null)
        {
            trees.Add(CSharpSyntaxTree.ParseText(consumerSource));
        }

        CSharpCompilation compilation = CreateCompilation(assemblyName, trees, extraReferences);

        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out _);

        return (outputCompilation, outputCompilation.GetDiagnostics());
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        IEnumerable<SyntaxTree> trees,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        List<MetadataReference> references = [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

        if (extraReferences is not null)
        {
            references.AddRange(extraReferences);
        }

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference EmitToReference(Compilation compilation)
    {
        using MemoryStream stream = new();
        EmitResult result = compilation.Emit(stream);

        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join('\n', result.Diagnostics.Select(d => d.ToString())));
        }

        stream.Position = 0;

        return MetadataReference.CreateFromStream(stream);
    }

    private static bool HasDiagnosticsAtWarningOrAbove(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Any(d => d.Severity >= DiagnosticSeverity.Warning);
    }

    private static bool HasCs0436For(ImmutableArray<Diagnostic> diagnostics, string typeName)
    {
        return diagnostics.Any(d => d.Id == "CS0436" && d.GetMessage().Contains(typeName));
    }

    private static bool HasEmbeddedAttribute(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "EmbeddedAttribute");
    }

    private static string ConcatTrees(GeneratorDriverRunResult result)
    {
        StringBuilder builder = new();

        foreach (SyntaxTree tree in result.GeneratedTrees)
        {
            builder.Append(tree.ToString());
        }

        return builder.ToString();
    }

    private sealed class MarkerGenerator : IIncrementalGenerator
    {
        private readonly string _namespace;
        private readonly string _attributeName;
        private readonly AttributeTargets _targets;
        private readonly bool _allowMultiple;
        private readonly bool _inherited;
        private readonly IReadOnlyList<(string Type, string Name)>? _properties;

        public MarkerGenerator(
            string @namespace,
            string attributeName,
            AttributeTargets targets,
            bool allowMultiple = false,
            bool inherited = false,
            IReadOnlyList<(string Type, string Name)>? properties = null)
        {
            _namespace = @namespace;
            _attributeName = attributeName;
            _targets = targets;
            _allowMultiple = allowMultiple;
            _inherited = inherited;
            _properties = properties;
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx =>
            {
                ctx.AddEmbeddedAttributeDefinition();
                ctx.AddMarkerAttribute(
                    "Marker.g.cs",
                    _namespace,
                    _attributeName,
                    _targets,
                    _allowMultiple,
                    _inherited,
                    _properties);
            });
        }
    }
}