using Haitch.Roslyn.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Diagnostics;

public class DiagnosticInfoTests
{
    // Test-only descriptors; RS2008 (analyzer release tracking) does not apply to fixtures that are never shipped as a real analyzer rule.
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor Descriptor = new(
        "HR0001",
        "Test diagnostic",
        "Test message '{0}'",
        "Test",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor OtherDescriptor = new(
        "HR0002",
        "Other diagnostic",
        "Other message '{0}'",
        "Test",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    [Test]
    public async Task Should_be_equal_for_the_same_descriptor_location_and_args()
    {
        var first = new DiagnosticInfo(Descriptor, GetLocation("sample.cs"), "value");
        var second = new DiagnosticInfo(Descriptor, GetLocation("sample.cs"), "value");

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
    }

    [Test]
    public async Task Should_be_unequal_for_a_different_descriptor()
    {
        LocationInfo location = GetLocation("sample.cs");

        var first = new DiagnosticInfo(Descriptor, location, "value");
        var second = new DiagnosticInfo(OtherDescriptor, location, "value");

        await Assert.That(first).IsNotEqualTo(second);
    }

    [Test]
    public async Task Should_be_unequal_for_a_different_location()
    {
        var first = new DiagnosticInfo(Descriptor, GetLocation("sample.cs"), "value");
        var second = new DiagnosticInfo(Descriptor, GetLocation("other.cs"), "value");

        await Assert.That(first).IsNotEqualTo(second);
    }

    [Test]
    public async Task Should_be_unequal_for_different_args()
    {
        LocationInfo location = GetLocation("sample.cs");

        var first = new DiagnosticInfo(Descriptor, location, "value");
        var second = new DiagnosticInfo(Descriptor, location, "other");

        await Assert.That(first).IsNotEqualTo(second);
    }

    [Test]
    public async Task Should_create_with_no_location()
    {
        var info = new DiagnosticInfo(Descriptor, null, "value");

        await Assert.That(info.Location).IsNull();
    }

    [Test]
    public async Task Should_convert_to_a_diagnostic_with_the_right_id_severity_location_and_message()
    {
        var info = new DiagnosticInfo(Descriptor, GetLocation("sample.cs"), "widget");

        Diagnostic diagnostic = info.ToDiagnostic();

        await Assert.That(diagnostic.Id).IsEqualTo(Descriptor.Id);
        await Assert.That(diagnostic.Severity).IsEqualTo(Descriptor.DefaultSeverity);
        await Assert.That(diagnostic.GetMessage()).IsEqualTo("Test message 'widget'");
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo("sample.cs");
    }

    [Test]
    public async Task Should_convert_to_a_diagnostic_with_no_location()
    {
        var info = new DiagnosticInfo(Descriptor, null, "widget");

        Diagnostic diagnostic = info.ToDiagnostic();

        await Assert.That(diagnostic.Location).IsEqualTo(Location.None);
    }

    [Test]
    public async Task Should_not_be_affected_by_mutating_the_source_array_after_construction()
    {
        var args = new[] { "original" };
        var info = new DiagnosticInfo(Descriptor, null, args);

        args[0] = "mutated";

        Diagnostic diagnostic = info.ToDiagnostic();

        await Assert.That(diagnostic.GetMessage()).IsEqualTo("Test message 'original'");
    }

    [Test]
    public async Task Should_be_equal_for_a_with_copy()
    {
        var info = new DiagnosticInfo(Descriptor, GetLocation("sample.cs"), "value");
        DiagnosticInfo copy = info with { };

        await Assert.That(copy).IsEqualTo(info);
    }

    private static LocationInfo GetLocation(string path)
    {
        const string source = "class Sample { }";
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: path);
        CompilationUnitSyntax root = (CompilationUnitSyntax)tree.GetRoot();
        ClassDeclarationSyntax classDeclaration = (ClassDeclarationSyntax)root.Members[0];

        return LocationInfo.From(classDeclaration.Identifier.GetLocation())!;
    }
}