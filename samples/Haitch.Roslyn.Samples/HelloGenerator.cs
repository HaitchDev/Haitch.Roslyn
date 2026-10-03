using System;
using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Samples;

// Proves the samples wiring: [Hello.Hello] partial types get a Greeting constant.
// It deliberately skips validation, so a non-partial type produces uncompilable output.
public sealed class HelloGenerator : IIncrementalGenerator
{
    private static readonly TypeRef StringType = new(
        FullyQualifiedName: "string",
        NullableAnnotation: NullableAnnotation.NotAnnotated,
        SpecialType: SpecialType.System_String,
        TypeKind: TypeKind.Class,
        IsValueType: false
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddEmbeddedAttributeDefinition();
            ctx.AddMarkerAttribute(
                "HelloAttribute.g.cs",
                "Hello",
                "HelloAttribute",
                AttributeTargets.Class
            );
        });

        var types = context.SyntaxProvider.ForTypesWithAttribute(
            "Hello.HelloAttribute",
            "HelloGenerator.Types"
        );

        context.RegisterSourceOutput(
            types,
            static (spc, item) => spc.AddSource(HintName.For(item.Type, "Hello"), Render(item.Type))
        );
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
                    Name: "Greeting",
                    Type: StringType,
                    Accessibility: Accessibility.Public,
                    IsStatic: false,
                    IsReadOnly: false,
                    IsConst: true,
                    IsRequired: false,
                    ConstantValue: ConstantValue.ForString("Hello from " + type.Name),
                    Attributes: default
                )
            );
        }
    }
}
