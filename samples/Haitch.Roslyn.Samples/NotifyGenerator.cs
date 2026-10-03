using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Haitch.Roslyn.Diagnostics;
using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Types;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Haitch.Roslyn.Samples;

// A sample ships no AnalyzerReleases files; real generators should track their rule ids there.
#pragma warning disable RS2008

// Turns [Notify.Notify] fields on partial types into properties that raise PropertyChanged.
//
// Property name rule: strip one leading "_" or "m_", then upper-case the first letter
// (_title -> Title, m_count -> Count). A field whose name would not change (Title, or a bare "_")
// is reported, because its property would collide with it.
//
// Guiding rule: for any input it accepts the generated code compiles; otherwise the generator
// reports a diagnostic and emits nothing for that type.
//
// INotifyPropertyChanged: if the type itself declares a field-like PropertyChanged event, the
// generated setters raise it, and the interface is added only if the type does not list it. If the
// interface comes from a base type, or the event is explicit, custom, static or of another type,
// the generator reports NOTIFY003, because it cannot raise that event. A type with neither gets
// both the interface and the event.
//
// Diagnostics: NOTIFY001 not partial, NOTIFY002 field name unchanged by the naming rule,
// NOTIFY003 interface or event cannot be used, NOTIFY004 field cannot become a property,
// NOTIFY005 generated property name is invalid or already taken.
public sealed class NotifyGenerator : IIncrementalGenerator
{
    private const string AttributeName = "Notify.NotifyAttribute";
    private const string HintSuffix = "Notify";
    private const string EventName = "PropertyChanged";
    private const string InterfaceMetadataName = "System.ComponentModel.INotifyPropertyChanged";

    private static readonly DiagnosticDescriptor NotPartial = new(
        "NOTIFY001",
        "Type must be partial",
        "'{0}' has [Notify] fields, so it and its containing types must be declared partial",
        "Notify",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor NameCollision = new(
        "NOTIFY002",
        "Field name would collide with its property",
        "Field '{0}' needs a leading '_' or 'm_' so its generated property has a different name",
        "Notify",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InterfaceConflict = new(
        "NOTIFY003",
        "INotifyPropertyChanged cannot be used",
        "'{0}' {1}",
        "Notify",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor UnsupportedField = new(
        "NOTIFY004",
        "Field cannot become a property",
        "Field '{0}' {1}",
        "Notify",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor UnusableName = new(
        "NOTIFY005",
        "Generated property name cannot be used",
        "Field '{0}' would generate property '{1}', which {2}",
        "Notify",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly TypeRef NotifyInterface = new(
        FullyQualifiedName: "global::" + InterfaceMetadataName,
        NullableAnnotation: NullableAnnotation.NotAnnotated,
        SpecialType: SpecialType.None,
        TypeKind: TypeKind.Interface,
        IsValueType: false
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddEmbeddedAttributeDefinition();
            ctx.AddMarkerAttribute(
                "NotifyAttribute.g.cs",
                "Notify",
                "NotifyAttribute",
                AttributeTargets.Field
            );
        });

        var fields = context.SyntaxProvider.ForFieldsWithAttribute(
            AttributeName,
            "NotifyGenerator.Fields",
            static (node, _) => IsField(node)
        );

        // TypeModel does not list a type's interfaces or events, so this reads them from the symbol.
        // It runs once per marked field, but only inspects the owner's members, and every field of a
        // type yields an equal NotifyOwner.
        var owners = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                AttributeName,
                static (node, _) => IsField(node),
                static (ctx, cancellationToken) =>
                    Inspect(ctx.TargetSymbol.ContainingType, cancellationToken)
            )
            .WithTrackingName("NotifyGenerator.Owners");

        // One item per containing type, so editing one class regenerates only that class.
        var types = fields
            .Collect()
            .Combine(owners.Collect())
            .SelectMany(
                static (pair, cancellationToken) =>
                    GroupByType(pair.Left, pair.Right, cancellationToken)
            );

        var valid = types.ReportDiagnostics(context, "NotifyGenerator.Types");

        context.RegisterSourceOutput(
            valid,
            static (spc, type) => spc.AddSource(HintName.For(type.Type, HintSuffix), Render(type))
        );
    }

    private static List<Result<NotifyType>> GroupByType(
        ImmutableArray<(
            FieldModel Field,
            TypeModel ContainingType,
            SyntaxInfo Syntax,
            EquatableArray<AttributeModel> Attributes
        )> fields,
        ImmutableArray<NotifyOwner> owners,
        CancellationToken cancellationToken
    )
    {
        var results = new List<Result<NotifyType>>();
        var ownersByKey = new Dictionary<string, NotifyOwner>();

        foreach (var owner in owners)
        {
            if (!ownersByKey.ContainsKey(owner.Key))
            {
                ownersByKey.Add(owner.Key, owner);
            }
        }

        // GroupBy keeps first-seen order, so output does not depend on hashing.
        foreach (var group in fields.GroupBy(f => HintName.For(f.ContainingType, HintSuffix)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Both providers match the same fields, so a missing owner cannot happen; skip rather than throw.
            if (!ownersByKey.TryGetValue(group.Key, out var owner))
            {
                continue;
            }

            var first = group.First();
            var failures = new List<DiagnosticInfo>();

            if (!first.Syntax.AreContainingTypesPartial)
            {
                failures.Add(
                    new DiagnosticInfo(NotPartial, first.Syntax.Location, first.ContainingType.Name)
                );
            }

            if (owner.Problem is not null)
            {
                failures.Add(
                    new DiagnosticInfo(
                        InterfaceConflict,
                        first.Syntax.Location,
                        first.ContainingType.Name,
                        owner.Problem
                    )
                );
            }

            CheckFields(group.ToList(), owner, first.ContainingType.Name, failures);

            results.Add(
                failures.Count > 0
                    ? Result<NotifyType>.Failure(failures.ToEquatableArray())
                    : new NotifyType(
                        first.ContainingType,
                        group
                            .Select(f => new NotifyField(f.Field.Name, f.Field.Type))
                            .ToEquatableArray(),
                        owner.AddInterface,
                        owner.AddEvent
                    )
            );
        }

        return results;
    }

    // Syntax-only filter shared by both providers: static, const and readonly fields are let through
    // so that CheckFields can report them instead of dropping them silently.
    private static bool IsField(SyntaxNode node) =>
        node is VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax };

    // Reads what TypeModel does not carry: how the type relates to INotifyPropertyChanged and which
    // names it already uses. Problem is set when the generated setters could not raise a usable event.
    private static NotifyOwner Inspect(INamedTypeSymbol owner, CancellationToken cancellationToken)
    {
        var key = HintName.For(TypeModel.From(owner), HintSuffix);
        var memberNames = owner
            .MemberNames.OrderBy(n => n, StringComparer.Ordinal)
            .ToEquatableArray();

        cancellationToken.ThrowIfCancellationRequested();

        var declaresInterface = owner.Interfaces.Any(IsNotifyInterface);
        var inheritsInterface = !declaresInterface && owner.AllInterfaces.Any(IsNotifyInterface);
        var events = owner.GetMembers(EventName);

        string? problem = null;
        var addEvent = false;

        if (owner.IsRefLikeType)
        {
            problem = "is a ref struct, which cannot implement an interface";
        }
        else if (inheritsInterface)
        {
            problem =
                "gets INotifyPropertyChanged from a base type, so it has no PropertyChanged event of its own to raise";
        }
        else if (events.Length == 0)
        {
            if (declaresInterface)
            {
                problem =
                    "implements INotifyPropertyChanged without declaring a field-like PropertyChanged event";
            }
            else
            {
                addEvent = true;
            }
        }
        else if (!IsFieldLikeNotifyEvent(events))
        {
            problem =
                "declares a PropertyChanged member that is not a field-like instance event of type PropertyChangedEventHandler";
        }

        return new NotifyOwner(
            key,
            AddInterface: problem is null && !declaresInterface,
            AddEvent: addEvent,
            problem,
            memberNames
        );
    }

    private static bool IsNotifyInterface(INamedTypeSymbol type) =>
        type.ToDisplayString() == InterfaceMetadataName;

    // A custom or explicit event has accessors the generated code cannot rely on, so only the
    // "public event Handler Name;" form qualifies; that shows in the syntax, not the symbol.
    private static bool IsFieldLikeNotifyEvent(ImmutableArray<ISymbol> members) =>
        members.Length == 1
        && members[0] is IEventSymbol { IsStatic: false } notifyEvent
        && notifyEvent.Type.Name == "PropertyChangedEventHandler"
        && notifyEvent.Type.ContainingNamespace.ToDisplayString() == "System.ComponentModel"
        && notifyEvent.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is VariableDeclaratorSyntax { Parent.Parent: EventFieldDeclarationSyntax }
        );

    private static void CheckFields(
        List<(
            FieldModel Field,
            TypeModel ContainingType,
            SyntaxInfo Syntax,
            EquatableArray<AttributeModel> Attributes
        )> items,
        NotifyOwner owner,
        string typeName,
        List<DiagnosticInfo> failures
    )
    {
        var supported = items.Where(i => UnsupportedReason(i.Field) is null).ToList();

        foreach (var item in items)
        {
            var name = item.Field.Name;

            if (UnsupportedReason(item.Field) is { } reason)
            {
                failures.Add(
                    new DiagnosticInfo(UnsupportedField, item.Syntax.Location, name, reason)
                );
                continue;
            }

            var property = PropertyName(name);

            if (property == name)
            {
                failures.Add(new DiagnosticInfo(NameCollision, item.Syntax.Location, name));
            }
            else if (
                UnusableReason(
                    property,
                    typeName,
                    owner,
                    supported.Count(s => PropertyName(s.Field.Name) == property)
                ) is
                { } why
            )
            {
                failures.Add(
                    new DiagnosticInfo(UnusableName, item.Syntax.Location, name, property, why)
                );
            }
        }
    }

    // "value" is the setter's parameter and "field" is a C# 14 keyword inside accessors, so a field
    // with either name cannot be read or assigned there.
    private static string? UnsupportedReason(FieldModel field) =>
        field.IsStatic || field.IsConst || field.IsReadOnly
            ? "is static, const or readonly; only writable instance fields can become properties"
        : field.Name is "value" or "field"
            ? "is named 'value' or 'field', which the generated accessors would not read as this field"
        : field.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer
            ? "has a pointer type, which the generated property could not declare outside unsafe code"
        : null;

    private static string? UnusableReason(
        string property,
        string typeName,
        NotifyOwner owner,
        int fieldsWithSameProperty
    )
    {
        if (!SyntaxFacts.IsValidIdentifier(property))
        {
            return "is not a valid identifier";
        }

        if (property == EventName)
        {
            return "would clash with the PropertyChanged event";
        }

        if (property == typeName)
        {
            return "has the same name as its containing type";
        }

        foreach (var member in owner.MemberNames)
        {
            if (member == property)
            {
                return "already exists on the type";
            }
        }

        return fieldsWithSameProperty > 1 ? "is also generated from another field" : null;
    }

    internal static string PropertyName(string fieldName)
    {
        var stripped =
            fieldName.StartsWith("m_", StringComparison.Ordinal) ? fieldName.Substring(2)
            : fieldName.StartsWith("_", StringComparison.Ordinal) ? fieldName.Substring(1)
            : fieldName;

        return stripped.Length == 0
            ? fieldName
            : char.ToUpperInvariant(stripped[0]) + stripped.Substring(1);
    }

    private static string Render(NotifyType type)
    {
        var writer = new SourceWriter();

        // Partial declarations repeat the whole nesting chain; only the innermost lists the interface.
        var chain = type
            .Type.ContainingTypes.Select(c =>
                Declaration(c.Name, c.Kind, c.TypeParameters, c.IsReadOnly, c.IsRefLikeType)
            )
            .Append(
                Declaration(
                    type.Type.Name,
                    type.Type.Kind,
                    type.Type.TypeParameters,
                    type.Type.IsReadOnly,
                    type.Type.IsRefLikeType
                ) with
                {
                    BaseTypes = type.AddInterface
                        ? new[] { NotifyInterface }.ToEquatableArray()
                        : default,
                }
            )
            .ToArray();

        using (var file = writer.File())
        {
            if (type.Type.Namespace is { } @namespace)
            {
                using var ns = file.Namespace(@namespace);
                WriteChain(ns.NewType(chain[0]), chain, 0, type, writer);
            }
            else
            {
                WriteChain(file.NewType(chain[0]), chain, 0, type, writer);
            }
        }

        return writer.ToString();
    }

    private static NewTypeModel Declaration(
        string name,
        TypeDeclarationKind kind,
        EquatableArray<TypeParameterModel> typeParameters,
        bool isReadOnly,
        bool isRefLike
    )
    {
        return new NewTypeModel(name, kind, Accessibility.NotApplicable)
        {
            IsPartial = true,
            IsReadOnly = isReadOnly,
            IsRefLikeType = isRefLike,
            TypeParameters = typeParameters,
        };
    }

    private static void WriteChain(
        TypeScope scope,
        NewTypeModel[] chain,
        int index,
        NotifyType type,
        SourceWriter writer
    )
    {
        using (scope)
        {
            if (index < chain.Length - 1)
            {
                WriteChain(scope.NewType(chain[index + 1]), chain, index + 1, type, writer);
                return;
            }

            // TypeScope has no event member, so the declaration goes straight to the writer.
            // The blank line stands in for the separator TypeScope adds between its own members.
            if (type.AddEvent)
            {
                writer.WriteLine(
                    "public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;"
                );
                writer.WriteLine();
            }

            foreach (var field in type.Fields)
            {
                WriteProperty(scope, field);
            }
        }
    }

    private static void WriteProperty(TypeScope scope, NotifyField field)
    {
        var property = new PropertyModel(
            Name: PropertyName(field.Name),
            Type: field.Type,
            Accessibility: Accessibility.Public,
            IsStatic: false,
            IsRequired: false,
            IsAbstract: false,
            IsVirtual: false,
            IsOverride: false,
            IsSealed: false,
            IsPartial: false,
            IsReadOnly: false,
            Accessors: new[]
            {
                new PropertyAccessorModel(PropertyAccessorKind.Get, Accessibility.NotApplicable),
                new PropertyAccessorModel(PropertyAccessorKind.Set, Accessibility.NotApplicable),
            }.ToEquatableArray(),
            Attributes: default
        );

        using var scopeForProperty = scope.Property(property);

        // A keyword-named field needs '@'; "value" and "field" were rejected earlier.
        var member =
            SyntaxFacts.GetKeywordKind(field.Name) == SyntaxKind.None
                ? field.Name
                : "@" + field.Name;

        using (var get = scopeForProperty.Get())
        {
            get.Line($"return {member};");
        }

        using (var set = scopeForProperty.Set())
        {
            using (
                var unchanged = set.Block(
                    $"if (global::System.Collections.Generic.EqualityComparer<{field.Type.FullyQualifiedName}>.Default.Equals({member}, value))"
                )
            )
            {
                unchanged.Return();
            }

            set.Line($"{member} = value;");
            set.Line(
                $"PropertyChanged?.Invoke(this, new global::System.ComponentModel.PropertyChangedEventArgs(nameof({property.Name})));"
            );
        }
    }
}

internal sealed record NotifyField(string Name, TypeRef Type);

internal sealed record NotifyType(
    TypeModel Type,
    EquatableArray<NotifyField> Fields,
    bool AddInterface,
    bool AddEvent
);

// Per containing type: Problem is why the setters cannot raise a usable event (null when they can).
internal sealed record NotifyOwner(
    string Key,
    bool AddInterface,
    bool AddEvent,
    string? Problem,
    EquatableArray<string> MemberNames
);
