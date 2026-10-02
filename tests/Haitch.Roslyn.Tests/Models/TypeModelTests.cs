using System;
using System.Linq;
using Haitch.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Tests.Models;

public class TypeModelTests
{
    [Test]
    public async Task Should_capture_a_class_kind_and_namespace()
    {
        const string source = "namespace Example; public class Sample { }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.Kind).IsEqualTo(TypeDeclarationKind.Class);
        await Assert.That(model.Namespace).IsEqualTo("Example");
        await Assert.That(model.Name).IsEqualTo("Sample");
    }

    [Test]
    public async Task Should_capture_a_struct_kind()
    {
        const string source = "namespace Example; public struct Sample { }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.Kind).IsEqualTo(TypeDeclarationKind.Struct);
    }

    [Test]
    public async Task Should_capture_a_record_class_kind()
    {
        const string source = "namespace Example; public record Sample { }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.Kind).IsEqualTo(TypeDeclarationKind.RecordClass);
    }

    [Test]
    public async Task Should_capture_a_record_struct_kind()
    {
        const string source = "namespace Example; public record struct Sample { }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.Kind).IsEqualTo(TypeDeclarationKind.RecordStruct);
    }

    [Test]
    public async Task Should_capture_an_interface_kind()
    {
        const string source = "namespace Example; public interface ISample { }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.ISample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.Kind).IsEqualTo(TypeDeclarationKind.Interface);
    }

    [Test]
    public async Task Should_capture_a_null_namespace_for_the_global_namespace()
    {
        const string source = "public class Sample { }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.Namespace).IsNull();
    }

    [Test]
    public async Task Should_capture_the_containing_type_chain_for_a_nested_type()
    {
        const string source =
            """
            namespace Example;

            public class Outer<T>
            {
                internal struct Inner
                {
                    public class Innermost { }
                }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Outer`1+Inner+Innermost");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.ContainingTypes.Count).IsEqualTo(2);
        await Assert.That(model.ContainingTypes[0].Name).IsEqualTo("Outer");
        await Assert.That(model.ContainingTypes[0].Kind).IsEqualTo(TypeDeclarationKind.Class);
        await Assert.That(model.ContainingTypes[0].TypeParameters.Count).IsEqualTo(1);
        await Assert.That(model.ContainingTypes[0].Accessibility).IsEqualTo(Accessibility.Public);
        await Assert.That(model.ContainingTypes[1].Name).IsEqualTo("Inner");
        await Assert.That(model.ContainingTypes[1].Kind).IsEqualTo(TypeDeclarationKind.Struct);
        await Assert.That(model.ContainingTypes[1].Accessibility).IsEqualTo(Accessibility.Internal);
    }

    [Test]
    public async Task Should_capture_generic_constraints_on_the_types_own_type_parameters()
    {
        const string source =
            """
            namespace Example;

            public class Sample<T> where T : class, new()
            {
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample`1");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.TypeParameters.Count).IsEqualTo(1);
        await Assert.That(model.TypeParameters[0].Name).IsEqualTo("T");
        await Assert.That(model.TypeParameters[0].HasReferenceTypeConstraint).IsTrue();
        await Assert.That(model.TypeParameters[0].HasConstructorConstraint).IsTrue();
    }

    [Test]
    public async Task Should_capture_static_abstract_and_sealed_modifiers()
    {
        const string source =
            """
            namespace Example;

            public static class StaticSample { }

            public abstract class AbstractSample { }

            public sealed class SealedSample { }
            """;

        INamedTypeSymbol staticType = CompilationHelper.GetNamedTypeSymbol(source, "Example.StaticSample");
        INamedTypeSymbol abstractType = CompilationHelper.GetNamedTypeSymbol(source, "Example.AbstractSample");
        INamedTypeSymbol sealedType = CompilationHelper.GetNamedTypeSymbol(source, "Example.SealedSample");

        TypeModel staticModel = TypeModel.From(staticType);
        TypeModel abstractModel = TypeModel.From(abstractType);
        TypeModel sealedModel = TypeModel.From(sealedType);

        await Assert.That(staticModel.IsStatic).IsTrue();
        await Assert.That(abstractModel.IsAbstract).IsTrue();
        await Assert.That(sealedModel.IsSealed).IsTrue();
    }

    // Structs have no `sealed` keyword but always report IsSealed true; renderers (4.2) must not
    // echo it back for a struct's partial declaration. This test locks that reporting in.
    [Test]
    public async Task Should_report_is_sealed_true_for_a_struct()
    {
        const string source = "namespace Example; public struct Sample { }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.IsSealed).IsTrue();
    }

    [Test]
    public async Task Should_capture_a_readonly_struct()
    {
        const string source = "namespace Example; public readonly struct Sample { }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.IsReadOnly).IsTrue();
    }

    [Test]
    public async Task Should_capture_a_ref_struct()
    {
        const string source = "namespace Example; public ref struct Sample { }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.IsRefLikeType).IsTrue();
    }

    [Test]
    public async Task Should_capture_a_file_local_type()
    {
        const string source = "namespace Example; file class Sample { }";

        INamedTypeSymbol type = GetFileLocalType(source, "Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.IsFileLocal).IsTrue();
    }

    [Test]
    public async Task Should_capture_is_read_only_and_is_ref_like_type_on_containing_types()
    {
        const string readOnlySource =
            """
            namespace Example;

            public readonly struct Outer
            {
                public class Inner { }
            }
            """;

        const string refStructSource =
            """
            namespace Example;

            public ref struct RefOuter
            {
                public class Inner { }
            }
            """;

        INamedTypeSymbol readOnlyInner = CompilationHelper.GetNamedTypeSymbol(readOnlySource, "Example.Outer+Inner");
        INamedTypeSymbol refStructInner =
            CompilationHelper.GetNamedTypeSymbol(refStructSource, "Example.RefOuter+Inner");

        TypeModel readOnlyModel = TypeModel.From(readOnlyInner);
        TypeModel refStructModel = TypeModel.From(refStructInner);

        await Assert.That(readOnlyModel.ContainingTypes[0].IsReadOnly).IsTrue();
        await Assert.That(refStructModel.ContainingTypes[0].IsRefLikeType).IsTrue();
    }

    [Test]
    public async Task Should_capture_field_property_and_method_members_when_included()
    {
        const string source =
            """
            namespace Example;

            public class Sample
            {
                public int Field;

                public string Name { get; set; } = "";

                public void DoWork() { }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type, includeMembers: true);

        await Assert.That(model.Fields.Count).IsEqualTo(1);
        await Assert.That(model.Fields[0].Name).IsEqualTo("Field");
        await Assert.That(model.Properties.Count).IsEqualTo(1);
        await Assert.That(model.Properties[0].Name).IsEqualTo("Name");
        await Assert.That(model.Methods.Count).IsEqualTo(1);
        await Assert.That(model.Methods[0].Name).IsEqualTo("DoWork");
    }

    [Test]
    public async Task Should_leave_member_arrays_empty_when_include_members_is_false()
    {
        const string source =
            """
            namespace Example;

            public class Sample
            {
                public int Field;

                public string Name { get; set; } = "";

                public void DoWork() { }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type);

        await Assert.That(model.Fields.Count).IsEqualTo(0);
        await Assert.That(model.Properties.Count).IsEqualTo(0);
        await Assert.That(model.Methods.Count).IsEqualTo(0);
    }

    // Records synthesize EqualityContract/PrintMembers/Deconstruct/Equals/GetHashCode/ToString and a
    // copy constructor; those must not leak into Properties/Methods alongside user-written members.
    [Test]
    public async Task Should_exclude_implicitly_declared_members_for_a_record()
    {
        const string source =
            """
            namespace Example;

            public record Sample(string Name)
            {
                public void DoWork() { }
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type, includeMembers: true);

        await Assert.That(model.Properties.Count).IsEqualTo(1);
        await Assert.That(model.Properties[0].Name).IsEqualTo("Name");
        await Assert.That(model.Methods.Count).IsEqualTo(1);
        await Assert.That(model.Methods[0].Name).IsEqualTo("DoWork");
    }

    [Test]
    public async Task Should_skip_indexers_when_members_are_included()
    {
        const string source =
            """
            namespace Example;

            public class Sample
            {
                public string Name { get; set; } = "";

                public int this[int index] => index;
            }
            """;

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        TypeModel model = TypeModel.From(type, includeMembers: true);

        await Assert.That(model.Properties.Count).IsEqualTo(1);
        await Assert.That(model.Properties[0].Name).IsEqualTo("Name");
    }

    [Test]
    public async Task Should_throw_for_an_enum()
    {
        const string source = "namespace Example; public enum Sample { A }";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        await Assert.That(() => TypeModel.From(type)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_throw_for_a_delegate()
    {
        const string source = "namespace Example; public delegate void Sample();";

        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample");

        await Assert.That(() => TypeModel.From(type)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Should_be_equal_across_two_compilations_of_the_same_source()
    {
        const string source =
            """
            namespace Example;

            [System.Obsolete]
            public sealed class Sample<T> where T : class
            {
                public int Field;

                public string Name { get; set; } = "";

                public void DoWork<TArg>(TArg value) { }
            }
            """;

        INamedTypeSymbol first = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample`1");
        INamedTypeSymbol second = CompilationHelper.GetNamedTypeSymbol(source, "Example.Sample`1");

        TypeModel firstModel = TypeModel.From(first, includeMembers: true);
        TypeModel secondModel = TypeModel.From(second, includeMembers: true);

        await Assert.That(firstModel).IsEqualTo(secondModel);
        await Assert.That(firstModel.GetHashCode()).IsEqualTo(secondModel.GetHashCode());
    }

    // file-local types are mangled at the metadata level, so GetTypeByMetadataName can't find them;
    // resolve the declared symbol from the syntax tree instead.
    private static INamedTypeSymbol GetFileLocalType(string source, string typeName)
    {
        CSharpCompilation compilation = CompilationHelper.Compile(source);
        SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();
        SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);

        ClassDeclarationSyntax declaration = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(node => node.Identifier.Text == typeName);

        return (INamedTypeSymbol)semanticModel.GetDeclaredSymbol(declaration)!;
    }
}
