using System;
using System.Collections.Generic;
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
using MarkedField = (
    Haitch.Roslyn.Models.FieldModel Field,
    Haitch.Roslyn.Models.TypeModel ContainingType,
    Haitch.Roslyn.Models.SyntaxInfo Syntax,
    Haitch.Roslyn.Types.EquatableArray<Haitch.Roslyn.Models.AttributeModel> Attributes
);

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
    internal const string EventName = "PropertyChanged";
    private const string InterfaceMetadataName = "System.ComponentModel.INotifyPropertyChanged";
    private const string HandlerName = "global::System.ComponentModel.PropertyChangedEventHandler";

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

    private static readonly EventModel NotifyEvent = new(
        EventName,
        new TypeRef(
            HandlerName + "?",
            NullableAnnotation.Annotated,
            SpecialType.None,
            TypeKind.Delegate,
            false
        ),
        Accessibility.Public,
        IsStatic: false,
        IsFieldLike: true
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

        // The ContainingType that field discovery returns is built without members, so it cannot show
        // the type's events or names. This reads only what the checks need, straight from the symbol and
        // without a full member model; NotifyOwner is value-equal, so an unchanged type is a cache hit.
        var owners = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                AttributeName,
                static (node, _) => IsField(node),
                static (ctx, cancellationToken) =>
                    NotifyOwner.From(ctx.TargetSymbol.ContainingType, cancellationToken)
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
        System.Collections.Immutable.ImmutableArray<MarkedField> fields,
        System.Collections.Immutable.ImmutableArray<NotifyOwner> owners,
        CancellationToken cancellationToken
    )
    {
        var results = new List<Result<NotifyType>>();
        var ownersByKey = owners
            .GroupBy(o => HintName.For(o.Type, HintSuffix))
            .ToDictionary(g => g.Key, g => g.First());

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

            results.Add(
                Result
                    .Combine(
                        PartialTypeValidation.ValidateContainingTypes(
                            first.ContainingType,
                            first.Syntax,
                            NotPartial,
                            owner.Type.Name
                        ),
                        CheckInterface(owner, first.Syntax),
                        CheckFields(group.ToList(), owner)
                    )
                    .Map(c => new NotifyType(
                        c.Item1,
                        c.Item3,
                        c.Item2.AddInterface,
                        c.Item2.AddEvent
                    ))
            );
        }

        return results;
    }

    // Syntax-only filter shared by both providers: static, const and readonly fields are let through
    // so that CheckFields can report them instead of dropping them silently.
    private static bool IsField(SyntaxNode node) =>
        node is VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax };

    // Decides whether the generated setters can raise a usable event, and what to add to the type.
    private static Result<(bool AddInterface, bool AddEvent)> CheckInterface(
        NotifyOwner owner,
        SyntaxInfo syntax
    )
    {
        var declaresInterface = owner.Type.Interfaces.Any(IsNotifyInterface);
        var inheritsInterface =
            !declaresInterface && owner.Type.AllInterfaces.Any(IsNotifyInterface);
        var named = owner.EventNameCount;

        var problem =
            owner.Type.IsRefLikeType ? "is a ref struct, which cannot implement an interface"
            : inheritsInterface
                ? "gets INotifyPropertyChanged from a base type, so it has no PropertyChanged event of its own to raise"
            : named == 0 && declaresInterface
                ? "implements INotifyPropertyChanged without declaring a field-like PropertyChanged event"
            : named > 0 && !IsFieldLikeNotifyEvent(owner)
                ? "declares a PropertyChanged member that is not a field-like instance event of type PropertyChangedEventHandler"
            : null;

        return problem is null
            ? (!declaresInterface, named == 0)
            : new DiagnosticInfo(InterfaceConflict, syntax.Location, owner.Type.Name, problem);
    }

    private static bool IsNotifyInterface(TypeRef type) =>
        type.FullyQualifiedName == NotifyInterface.FullyQualifiedName;

    // A custom or abstract event has no accessors the generated code can invoke through, so only the
    // "public event Handler Name;" form qualifies, and it must be the only member with that name.
    private static bool IsFieldLikeNotifyEvent(NotifyOwner owner) =>
        owner
            is {
                EventNameCount: 1,
                Event: { IsStatic: false, IsAbstract: false, IsFieldLike: true } notifyEvent,
            }
        && notifyEvent.Type.FullyQualifiedName.TrimEnd('?') == HandlerName;

    private static Result<EquatableArray<NotifyField>> CheckFields(
        List<MarkedField> items,
        NotifyOwner owner
    )
    {
        var failures = new List<DiagnosticInfo>();
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
                    owner.Type.Name,
                    owner.MemberNames,
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

        return failures.Count > 0
            ? Result<EquatableArray<NotifyField>>.Failure(failures.ToEquatableArray())
            : items.Select(f => new NotifyField(f.Field.Name, f.Field.Type)).ToEquatableArray();
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
        EquatableArray<string> memberNames,
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

        if (memberNames.Contains(property))
        {
            return "already exists on the type";
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

        // The base list goes on the innermost declaration only; the containing types are repeated bare.
        var baseTypes = type.AddInterface ? new[] { NotifyInterface }.ToEquatableArray() : default;

        using (var file = writer.File())
        {
            if (type.Type.Namespace is { } @namespace)
            {
                using var ns = file.Namespace(@namespace);
                WriteMembers(ns.Type(type.Type, baseTypes), type);
            }
            else
            {
                WriteMembers(file.Type(type.Type, baseTypes), type);
            }
        }

        return writer.ToString();
    }

    private static void WriteMembers(TypeScope scope, NotifyType type)
    {
        using (scope)
        {
            if (type.AddEvent)
            {
                scope.Event(NotifyEvent);
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

// What the checks need to know about the type that owns [Notify] fields. MemberNames covers every
// kind of member, nested types included; an explicit implementation is stored under its qualified
// name, so it never counts as a clash.
internal sealed record NotifyOwner(
    TypeModel Type,
    EquatableArray<string> MemberNames,
    int EventNameCount,
    EventModel? Event
)
{
    public static NotifyOwner From(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        // The event's compiler-made backing field shares its name, so only declared members count.
        var named = type.GetMembers(NotifyGenerator.EventName)
            .Where(m => !m.IsImplicitlyDeclared)
            .ToList();

        return new NotifyOwner(
            TypeModel.From(type, includeMembers: false, cancellationToken),
            type.GetMembers()
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToEquatableArray(),
            named.Count,
            named
                .OfType<IEventSymbol>()
                .Select(e => EventModel.From(e, cancellationToken))
                .FirstOrDefault()
        );
    }
}

internal sealed record NotifyType(
    TypeModel Type,
    EquatableArray<NotifyField> Fields,
    bool AddInterface,
    bool AddEvent
);
