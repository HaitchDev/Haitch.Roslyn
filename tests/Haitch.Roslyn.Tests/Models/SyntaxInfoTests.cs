using Haitch.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Models;

public class SyntaxInfoTests
{
    [Test]
    public async Task Should_be_partial_for_a_partial_top_level_type()
    {
        TypeDeclarationSyntax type = GetTypeDeclaration(
            "public partial class Sample { }",
            "Sample"
        );

        SyntaxInfo info = SyntaxInfo.From(type);

        await Assert.That(info.IsPartial).IsTrue();
        await Assert.That(info.AreContainingTypesPartial).IsTrue();
    }

    [Test]
    public async Task Should_not_be_partial_for_a_non_partial_type()
    {
        TypeDeclarationSyntax type = GetTypeDeclaration("public class Sample { }", "Sample");

        SyntaxInfo info = SyntaxInfo.From(type);

        await Assert.That(info.IsPartial).IsFalse();
        await Assert.That(info.AreContainingTypesPartial).IsTrue();
    }

    [Test]
    public async Task Should_not_flag_containing_types_partial_when_nested_in_a_non_partial_containing_type()
    {
        const string source = """
            public class Outer
            {
                public partial class Inner { }
            }
            """;

        TypeDeclarationSyntax type = GetTypeDeclaration(source, "Inner");

        SyntaxInfo info = SyntaxInfo.From(type);

        await Assert.That(info.IsPartial).IsTrue();
        await Assert.That(info.AreContainingTypesPartial).IsFalse();
    }

    [Test]
    public async Task Should_flag_containing_types_partial_when_it_and_every_containing_type_are_partial()
    {
        const string source = """
            public partial class Outer
            {
                public partial class Inner { }
            }
            """;

        TypeDeclarationSyntax type = GetTypeDeclaration(source, "Inner");

        SyntaxInfo info = SyntaxInfo.From(type);

        await Assert.That(info.IsPartial).IsTrue();
        await Assert.That(info.AreContainingTypesPartial).IsTrue();
    }

    [Test]
    [Arguments("public partial record Sample { }", true)]
    [Arguments("public record Sample { }", false)]
    [Arguments("public partial record struct Sample { }", true)]
    [Arguments("public record struct Sample { }", false)]
    [Arguments("public partial struct Sample { }", true)]
    [Arguments("public struct Sample { }", false)]
    [Arguments("public partial interface Sample { }", true)]
    [Arguments("public interface Sample { }", false)]
    public async Task Should_detect_partial_across_type_kinds(string source, bool expectedIsPartial)
    {
        TypeDeclarationSyntax type = GetTypeDeclaration(source, "Sample");

        SyntaxInfo info = SyntaxInfo.From(type);

        await Assert.That(info.IsPartial).IsEqualTo(expectedIsPartial);
    }

    [Test]
    public async Task Should_capture_the_location_of_the_identifier()
    {
        TypeDeclarationSyntax type = GetTypeDeclaration(
            "public partial class Sample { }",
            "Sample"
        );

        SyntaxInfo info = SyntaxInfo.From(type);

        await Assert.That(info.Location!.Span).IsEqualTo(type.Identifier.Span);
    }

    [Test]
    public async Task Should_be_equal_across_two_separate_parses()
    {
        const string source = "public partial class Sample { }";

        SyntaxInfo first = SyntaxInfo.From(GetTypeDeclaration(source, "Sample"));
        SyntaxInfo second = SyntaxInfo.From(GetTypeDeclaration(source, "Sample"));

        await Assert.That(first).IsEqualTo(second);
    }

    private static TypeDeclarationSyntax GetTypeDeclaration(string source, string identifier)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: "sample.cs");
        CompilationUnitSyntax root = (CompilationUnitSyntax)tree.GetRoot();

        return root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Single(type => type.Identifier.Text == identifier);
    }
}
