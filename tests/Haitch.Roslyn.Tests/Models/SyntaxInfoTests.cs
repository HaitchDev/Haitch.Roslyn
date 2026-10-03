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

    [Test]
    public async Task Should_describe_a_method_by_its_own_partial_modifier_and_identifier()
    {
        const string source = """
            public partial class Sample
            {
                public partial void Run();
                public void Plain() { }
            }
            """;

        MethodDeclarationSyntax partialMethod = GetNode<MethodDeclarationSyntax>(source, "Run");
        MethodDeclarationSyntax plainMethod = GetNode<MethodDeclarationSyntax>(source, "Plain");

        SyntaxInfo partialInfo = SyntaxInfo.From(partialMethod);
        SyntaxInfo plainInfo = SyntaxInfo.From(plainMethod);

        await Assert.That(partialInfo.IsPartial).IsTrue();
        await Assert.That(plainInfo.IsPartial).IsFalse();
        await Assert.That(plainInfo.AreContainingTypesPartial).IsTrue();
        await Assert.That(partialInfo.Location!.Span).IsEqualTo(partialMethod.Identifier.Span);
    }

    [Test]
    public async Task Should_describe_a_property_by_its_own_partial_modifier_and_identifier()
    {
        const string source = """
            public partial class Sample
            {
                public partial int Value { get; set; }
                public int Plain { get; set; }
            }
            """;

        PropertyDeclarationSyntax partialProperty = GetNode<PropertyDeclarationSyntax>(
            source,
            "Value"
        );
        PropertyDeclarationSyntax plainProperty = GetNode<PropertyDeclarationSyntax>(
            source,
            "Plain"
        );

        SyntaxInfo partialInfo = SyntaxInfo.From(partialProperty);
        SyntaxInfo plainInfo = SyntaxInfo.From(plainProperty);

        await Assert.That(partialInfo.IsPartial).IsTrue();
        await Assert.That(plainInfo.IsPartial).IsFalse();
        await Assert.That(partialInfo.Location!.Span).IsEqualTo(partialProperty.Identifier.Span);
    }

    [Test]
    public async Task Should_describe_a_field_declarator_as_never_partial_and_locate_its_identifier()
    {
        const string source = """
            public partial class Sample
            {
                public int first, second;
            }
            """;

        VariableDeclaratorSyntax second = GetNode<VariableDeclaratorSyntax>(source, "second");

        SyntaxInfo info = SyntaxInfo.From(second);

        await Assert.That(info.IsPartial).IsFalse();
        await Assert.That(info.AreContainingTypesPartial).IsTrue();
        await Assert.That(info.Location!.Span).IsEqualTo(second.Identifier.Span);
    }

    [Test]
    public async Task Should_flag_a_member_whose_containing_chain_has_a_non_partial_type()
    {
        const string source = """
            public partial class Outer
            {
                public class Middle
                {
                    public partial class Inner
                    {
                        public int Value { get; set; }
                    }
                }
            }
            """;

        SyntaxInfo info = SyntaxInfo.From(GetNode<PropertyDeclarationSyntax>(source, "Value"));

        await Assert.That(info.AreContainingTypesPartial).IsFalse();
    }

    [Test]
    public async Task Should_throw_for_an_unsupported_member_node()
    {
        EnumDeclarationSyntax node = GetNode<EnumDeclarationSyntax>(
            "public enum Sample { A }",
            "Sample"
        );

        await Assert.That(() => SyntaxInfo.From(node)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_describe_a_type_passed_as_a_syntax_node_like_the_type_overload()
    {
        const string source = """
            public class Outer
            {
                public partial class Inner { }
            }
            """;

        TypeDeclarationSyntax type = GetTypeDeclaration(source, "Inner");

        SyntaxInfo viaNode = SyntaxInfo.From((SyntaxNode)type);

        await Assert.That(viaNode).IsEqualTo(SyntaxInfo.From(type));
    }

    [Test]
    public async Task Should_throw_for_a_local_variable_declarator()
    {
        const string source = """
            public partial class Sample
            {
                public void Run()
                {
                    int local = 1;
                }
            }
            """;

        VariableDeclaratorSyntax local = GetNode<VariableDeclaratorSyntax>(source, "local");

        await Assert.That(() => SyntaxInfo.From(local)).Throws<ArgumentException>();
    }

    private static T GetNode<T>(string source, string identifier)
        where T : SyntaxNode
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            path: "sample.cs"
        );

        return tree.GetRoot()
            .DescendantNodes()
            .OfType<T>()
            .Single(node =>
                node switch
                {
                    MethodDeclarationSyntax method => method.Identifier.Text == identifier,
                    PropertyDeclarationSyntax property => property.Identifier.Text == identifier,
                    VariableDeclaratorSyntax variable => variable.Identifier.Text == identifier,
                    EnumDeclarationSyntax enumeration => enumeration.Identifier.Text == identifier,
                    _ => false,
                }
            );
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
