using Haitch.Roslyn.Models;
using Haitch.Roslyn.Types;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.CSharp15.Tests;

public class NewTypeCSharp15Tests
{
    private const string Support = """
        public record Cat(string Name);
        public record Dog(string Name);
        public interface IMarker { }
        public class BaseClass { }

        """;

    private static readonly MetadataReference[] References = CreateReferences();

    private static readonly TypeRef Marker = new(
        "global::IMarker",
        NullableAnnotation.NotAnnotated,
        SpecialType.None,
        TypeKind.Interface,
        false
    );

    private static readonly TypeRef BaseClass = new(
        "global::BaseClass",
        NullableAnnotation.NotAnnotated,
        SpecialType.None,
        TypeKind.Class,
        false
    );

    [Test]
    public async Task WriteNewTypeDeclaration_ClosedRecord_WritesClosedKeywordAndCompiles()
    {
        var model = new NewTypeModel("Shape", TypeDeclarationKind.RecordClass, Accessibility.Public)
        {
            IsClosed = true,
        };

        await AssertWrites(
            model,
            """
            public closed record Shape
            {
            }

            """
        );
    }

    [Test]
    public async Task WriteNewTypeDeclaration_ClosedClassWithBody_WritesClosedKeywordAndCompiles()
    {
        var model = new NewTypeModel("Shape", TypeDeclarationKind.Class, Accessibility.Public)
        {
            IsClosed = true,
        };
        var writer = new SourceWriter();

        using (writer.WriteNewTypeDeclaration(model))
        {
            writer.WriteLine("public int Sides { get; init; }");
        }

        await Assert
            .That(writer.ToString())
            .IsEqualTo(
                """
                public closed class Shape
                {
                    public int Sides { get; init; }
                }

                """
            );
        await AssertCompiles(writer.ToString());
    }

    [Test]
    public async Task WriteNewTypeDeclaration_Union_WritesCaseTypeList()
    {
        var model = new NewTypeModel("Pet", TypeDeclarationKind.Union, Accessibility.Public)
        {
            UnionCaseTypes = new[] { "global::Cat", "global::Dog" }.ToEquatableArray(),
        };

        await AssertWrites(
            model,
            """
            public union Pet(global::Cat, global::Dog)
            {
            }

            """
        );
    }

    [Test]
    public async Task WriteNewTypeDeclaration_PartialUnionWithoutCaseList_WritesBareDeclaration()
    {
        var model = new NewTypeModel("Pet", TypeDeclarationKind.Union, Accessibility.Public)
        {
            IsPartial = true,
        };
        var writer = new SourceWriter();

        using (writer.WriteNewTypeDeclaration(model)) { }

        await Assert
            .That(writer.ToString())
            .IsEqualTo(
                """
                public partial union Pet
                {
                }

                """
            );
    }

    [Test]
    public async Task WriteNewTypeDeclaration_GenericReadOnlyUnionWithInterface_WritesAndCompiles()
    {
        var model = new NewTypeModel("Either", TypeDeclarationKind.Union, Accessibility.Public)
        {
            IsReadOnly = true,
            TypeParameters = new[] { Parameter("TL"), Parameter("TR") }.ToEquatableArray(),
            BaseTypes = new[] { Marker }.ToEquatableArray(),
            UnionCaseTypes = new[] { "TL", "TR" }.ToEquatableArray(),
        };

        await AssertWrites(
            model,
            """
            public readonly union Either<TL, TR>(TL, TR) : global::IMarker
            {
            }

            """
        );
    }

    [Test]
    [MethodDataSource(nameof(IllegalModels))]
    public async Task WriteNewTypeDeclaration_IllegalCombination_ThrowsAndLeavesWriterEmpty(
        int caseIndex
    )
    {
        var writer = new SourceWriter();
        var model = IllegalCases()[caseIndex]();

        await Assert.That(() => writer.WriteNewTypeDeclaration(model)).Throws<ArgumentException>();
        await Assert.That(writer.ToString()).IsEqualTo(string.Empty);
    }

    public static IEnumerable<int> IllegalModels()
    {
        return Enumerable.Range(0, IllegalCases().Count);
    }

    private static List<Func<NewTypeModel>> IllegalCases()
    {
        return IllegalCaseIterator().ToList();
    }

    private static IEnumerable<Func<NewTypeModel>> IllegalCaseIterator()
    {
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                IsClosed = true,
                IsAbstract = true,
            };
        yield return () =>
            Model(TypeDeclarationKind.RecordClass) with
            {
                IsClosed = true,
                IsAbstract = true,
            };
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                IsClosed = true,
                IsSealed = true,
            };
        yield return () =>
            Model(TypeDeclarationKind.Class) with
            {
                IsClosed = true,
                IsStatic = true,
            };

        foreach (
            var kind in new[]
            {
                TypeDeclarationKind.Struct,
                TypeDeclarationKind.RecordStruct,
                TypeDeclarationKind.Interface,
                TypeDeclarationKind.Union,
            }
        )
        {
            var captured = kind;

            yield return () =>
                Model(captured) with
                {
                    IsClosed = true,
                    UnionCaseTypes =
                        captured == TypeDeclarationKind.Union
                            ? new[] { "global::Cat" }.ToEquatableArray()
                            : default,
                };
        }

        yield return () => Model(TypeDeclarationKind.Union);
        yield return () =>
            Model(TypeDeclarationKind.Union) with
            {
                UnionCaseTypes = new[] { "global::Cat", " " }.ToEquatableArray(),
            };
        yield return () =>
            Model(TypeDeclarationKind.Union) with
            {
                UnionCaseTypes = new[] { "" }.ToEquatableArray(),
            };
        yield return () =>
            Model(TypeDeclarationKind.Union) with
            {
                IsPartial = false,
                UnionCaseTypes = default,
            };
        yield return () =>
            Model(TypeDeclarationKind.Union) with
            {
                UnionCaseTypes = new[] { "global::Cat" }.ToEquatableArray(),
                BaseTypes = new[] { BaseClass }.ToEquatableArray(),
            };
        yield return () =>
            Model(TypeDeclarationKind.Union) with
            {
                UnionCaseTypes = new[] { "global::Cat" }.ToEquatableArray(),
                BaseTypes = new[] { Marker, BaseClass }.ToEquatableArray(),
            };
    }

    private static TypeParameterModel Parameter(string name)
    {
        return new TypeParameterModel(
            name,
            default,
            false,
            NullableAnnotation.None,
            false,
            false,
            false,
            false
        );
    }

    private static NewTypeModel Model(TypeDeclarationKind kind)
    {
        return new NewTypeModel("T0", kind, Accessibility.Public);
    }

    private static async Task AssertWrites(NewTypeModel model, string expected)
    {
        var writer = new SourceWriter();

        using (writer.WriteNewTypeDeclaration(model)) { }

        await Assert.That(writer.ToString()).IsEqualTo(expected);
        await AssertCompiles(writer.ToString());
    }

    private static async Task AssertCompiles(string output)
    {
        var compilation = Compile("#nullable enable\n" + Support + output);
        var problems = compilation
            .GetDiagnostics()
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(d => d.ToString())
            .ToArray();

        await Assert.That(problems).IsEmpty();
    }

    private static CSharpCompilation Compile(string source)
    {
        var trees = new[] { source, CSharp15Polyfills.Source }.Select(text =>
            CSharpSyntaxTree.ParseText(text, CSharp15.ParseOptions)
        );

        return CSharpCompilation.Create(
            "NewTypeCSharp15Tests",
            trees,
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    private static MetadataReference[] CreateReferences()
    {
        var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
