using Haitch.Roslyn.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Diagnostics;

public class LocationInfoTests
{
    private const string Source = "class Sample { }";

    [Test]
    public async Task Should_capture_a_source_location()
    {
        Location location = GetClassIdentifierLocation(Source, "sample.cs");

        LocationInfo? info = LocationInfo.From(location);

        await Assert.That(info).IsNotNull();
    }

    [Test]
    public async Task Should_return_null_for_Location_None()
    {
        LocationInfo? info = LocationInfo.From(Location.None);

        await Assert.That(info).IsNull();
    }

    [Test]
    public async Task Should_return_null_for_a_metadata_location()
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]
        );

        INamedTypeSymbol objectSymbol = compilation.GetSpecialType(SpecialType.System_Object);
        Location location = objectSymbol.Locations[0];

        LocationInfo? info = LocationInfo.From(location);

        await Assert.That(info).IsNull();
    }

    [Test]
    public async Task Should_round_trip_path_span_and_line_span()
    {
        Location location = GetClassIdentifierLocation(Source, "sample.cs");
        LocationInfo info = LocationInfo.From(location)!;

        Location roundTripped = info.ToLocation();

        FileLinePositionSpan expected = location.GetLineSpan();
        FileLinePositionSpan actual = roundTripped.GetLineSpan();

        await Assert.That(roundTripped.SourceSpan).IsEqualTo(location.SourceSpan);
        await Assert.That(actual.Path).IsEqualTo(expected.Path);
        await Assert.That(actual.Span).IsEqualTo(expected.Span);
    }

    [Test]
    public async Task Should_be_equal_for_the_same_location_captured_from_separately_parsed_identical_trees()
    {
        LocationInfo? first = LocationInfo.From(GetClassIdentifierLocation(Source, "sample.cs"));
        LocationInfo? second = LocationInfo.From(GetClassIdentifierLocation(Source, "sample.cs"));

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first!.GetHashCode()).IsEqualTo(second!.GetHashCode());
    }

    [Test]
    public async Task Should_be_unequal_for_a_different_path()
    {
        LocationInfo? first = LocationInfo.From(GetClassIdentifierLocation(Source, "sample.cs"));
        LocationInfo? second = LocationInfo.From(GetClassIdentifierLocation(Source, "other.cs"));

        await Assert.That<LocationInfo?>(first).IsNotEqualTo(second);
    }

    [Test]
    public async Task Should_round_trip_From_ToLocation_through_equality()
    {
        Location location = GetClassIdentifierLocation(Source, "sample.cs");
        LocationInfo info = LocationInfo.From(location)!;

        LocationInfo? roundTripped = LocationInfo.From(info.ToLocation());

        await Assert.That(roundTripped).IsEqualTo(info);
    }

    private static Location GetClassIdentifierLocation(string source, string path)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: path);
        CompilationUnitSyntax root = (CompilationUnitSyntax)tree.GetRoot();
        ClassDeclarationSyntax classDeclaration = (ClassDeclarationSyntax)root.Members[0];

        return classDeclaration.Identifier.GetLocation();
    }
}
