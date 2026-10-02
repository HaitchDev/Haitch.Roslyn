using Haitch.Roslyn.Diagnostics;
using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Sample;

// End-to-end sample: types marked [Sample.Sample] get a TypeName constant and a ToString override
// in a generated partial declaration. A marked type that is not partial reports SAMPLE001 instead.
// Known gap: the sample does not guard member conflicts. A user-declared ToString or TypeName makes
// the output fail with CS0111/CS0102, because ForTypesWithAttribute cannot request members, so the
// model has no member list to check against.
internal sealed class SampleGenerator : IIncrementalGenerator
{
    public const string MarkerMetadataName = "Sample.SampleAttribute";
    public const string ModelStepName = "SampleGenerator.Types";
    public const string ValidatedStepName = "SampleGenerator.Validated";

    // Test-only descriptors; RS2008 (analyzer release tracking) does not apply to a fixture that is never shipped as a real analyzer rule.
#pragma warning disable RS2008
    public static readonly DiagnosticDescriptor NotPartial = Describe("SAMPLE001", "'{0}' must be partial");
    public static readonly DiagnosticDescriptor StaticType = Describe("SAMPLE002", "'{0}' must not be static");

    private static readonly PartialTypeDiagnostics Diagnostics = new(
        NotPartial,
        Describe("SAMPLE003", "Containing type of '{0}' must be partial"),
        Describe("SAMPLE004", "'{0}' must not be file-local"));

    private static DiagnosticDescriptor Describe(string id, string message) =>
        new(id, "Invalid [Sample] type", message, "Sample", DiagnosticSeverity.Error, isEnabledByDefault: true);
#pragma warning restore RS2008

    private static readonly TypeRef StringType = new(
        "string",
        NullableAnnotation.NotAnnotated,
        SpecialType.System_String,
        TypeKind.Class,
        IsValueType: false);

    private static readonly MethodModel ToStringMethod = new(
        "ToString",
        MethodKind.Ordinary,
        StringType,
        ReturnRefKind.None,
        Accessibility.Public,
        IsStatic: false,
        IsAbstract: false,
        IsVirtual: false,
        IsOverride: true,
        IsSealed: false,
        IsAsync: false,
        IsExtern: false,
        IsExtensionMethod: false,
        IsPartialDefinition: false,
        IsReadOnly: false,
        ExplicitInterface: null,
        ExplicitInterfaceMemberName: null,
        TypeParameters: default,
        Parameters: default,
        Attributes: default);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddEmbeddedAttributeDefinition();
            ctx.AddMarkerAttribute("SampleAttribute.g.cs", "Sample", "SampleAttribute", AttributeTargets.Class);
        });

        var types = context.SyntaxProvider.ForTypesWithAttribute(MarkerMetadataName, ModelStepName);

        var valid = types
            .Select(static (item, _) => ValidateSample(item.Type, item.Syntax))
            .ReportDiagnostics(context, ValidatedStepName);

        context.RegisterSourceOutput(valid,
            static (spc, type) => spc.AddSource(HintName.For(type, "Sample"), Render(type)));
    }

    private static Result<TypeModel> ValidateSample(TypeModel type, SyntaxInfo syntax)
    {
        var result = PartialTypeValidation.Validate(type, syntax, Diagnostics);

        // A static class cannot hold the instance ToString override.
        return result.IsSuccess && type.IsStatic
            ? Result<TypeModel>.Failure(new DiagnosticInfo(StaticType, syntax.Location, type.Name))
            : result;
    }

    private static string Render(TypeModel type)
    {
        var writer = new SourceWriter();

        using (var file = writer.File())
        {
            if (type.Namespace is { } @namespace)
            {
                using var ns = file.Namespace(@namespace);
                WriteType(ns.Type(type), type);
            }
            else
            {
                WriteType(file.Type(type), type);
            }
        }

        return writer.ToString();
    }

    private static void WriteType(TypeScope scope, TypeModel type)
    {
        using (scope)
        {
            scope.Field(
                new FieldModel(
                    "TypeName",
                    StringType,
                    Accessibility.Public,
                    IsStatic: false,
                    IsReadOnly: false,
                    IsConst: true,
                    IsRequired: false,
                    ConstantValue.ForString(type.Name),
                    Attributes: default));

            using var body = scope.Method(ToStringMethod);
            body.Line("return TypeName;");
        }
    }
}
