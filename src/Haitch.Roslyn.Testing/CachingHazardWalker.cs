using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Testing;

/// <summary>Finds values in an incremental pipeline model that defeat caching.</summary>
public static class CachingHazardWalker
{
    private const string RootPath = "(root)";
    private const int MaxDepth = 256;

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldCache = new();

    private static readonly (Type Type, string Name)[] RoslynHazards =
    [
        (typeof(ISymbol), "ISymbol"),
        (typeof(SyntaxNode), "SyntaxNode"),
        (typeof(SyntaxTree), "SyntaxTree"),
        (typeof(SemanticModel), "SemanticModel"),
        (typeof(Compilation), "Compilation"),
        (typeof(Location), "Location"),
        (typeof(Diagnostic), "Diagnostic"),
    ];

    /// <summary>
    /// Finds the first caching hazard reachable from <paramref name="value"/>: Roslyn symbols, syntax, compilations,
    /// arrays and classes without value equality. Public and private instance fields are walked, so auto-property
    /// backing fields count; any generic type named <c>EquatableArray`1</c> is walked element by element. A model
    /// nested deeper than 256 levels is reported as a hazard.
    /// </summary>
    /// <remarks>
    /// The walk can report false positives: BCL types with internal state that override <c>Equals</c> are walked
    /// field by field, and value-equal collection wrappers other than <c>EquatableArray&lt;T&gt;</c> are not walked
    /// element-wise, so they are reported when they hold a hazard or lack an <c>Equals</c> override.
    /// </remarks>
    /// <param name="value">The model to inspect.</param>
    /// <returns>The member path of the hazard such as <c>Item1.Members[0].Symbol</c>, or null when there is none.</returns>
    public static string? Find(object? value) => FindHazard(value)?.Path;

    internal static (string Path, string Reason)? FindHazard(object? value)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var found = Walk(value, "", 0, visited);
        return found is { } hazard
            ? (hazard.Path.Length == 0 ? RootPath : hazard.Path, hazard.Reason)
            : null;
    }

    private static (string Path, string Reason)? Walk(
        object? value,
        string path,
        int depth,
        HashSet<object> visited
    )
    {
        if (value is null)
        {
            return null;
        }

        if (depth > MaxDepth)
        {
            return (path, $"is nested deeper than {MaxDepth} levels");
        }

        var type = value.GetType();
        if (IsSafeLeaf(type))
        {
            return null;
        }

        foreach (var (hazard, name) in RoslynHazards)
        {
            if (hazard.IsAssignableFrom(type))
            {
                return (path, $"holds {Article(name)} {name} ({type.Name})");
            }
        }

        if (type.IsArray)
        {
            return (path, $"holds an array ({type.Name}), which compares by reference");
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
        {
            return (path, $"holds an ImmutableArray ({type.Name}), which compares by reference");
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition().Name == "EquatableArray`1")
        {
            return WalkEquatableArray(value, type, path, depth, visited);
        }

        if (!type.IsValueType)
        {
            if (!visited.Add(value))
            {
                return null;
            }

            if (!HasValueEquality(type))
            {
                return (path, $"holds a {type.Name}, which does not override Equals(object)");
            }
        }

        return WalkFields(value, type, path, depth, visited);
    }

    private static (string Path, string Reason)? WalkEquatableArray(
        object value,
        Type type,
        string path,
        int depth,
        HashSet<object> visited
    )
    {
        var backing = type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            .FirstOrDefault(f => f.FieldType.IsArray);
        if (backing?.GetValue(value) is not IList items)
        {
            return null;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (Walk(items[i], $"{path}[{i}]", depth + 1, visited) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static (string Path, string Reason)? WalkFields(
        object value,
        Type type,
        string path,
        int depth,
        HashSet<object> visited
    )
    {
        foreach (var field in FieldCache.GetOrAdd(type, InstanceFields))
        {
            var name = MemberName(field);
            var childPath = path.Length == 0 ? name : $"{path}.{name}";
            if (Walk(field.GetValue(value), childPath, depth + 1, visited) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static FieldInfo[] InstanceFields(Type type)
    {
        var fields = new List<FieldInfo>();
        for (
            var current = type;
            current is not null && current != typeof(object);
            current = current.BaseType
        )
        {
            fields.AddRange(
                current.GetFields(
                    BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                )
            );
        }

        return [.. fields];
    }

    private static string MemberName(FieldInfo field) =>
        field.Name.StartsWith('<')
        && field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal)
            ? field.Name[1..field.Name.IndexOf('>')]
            : field.Name;

    private static bool IsSafeLeaf(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || typeof(Type).IsAssignableFrom(type)
        || typeof(DiagnosticDescriptor).IsAssignableFrom(type)
        || typeof(LocalizableString).IsAssignableFrom(type);

    private static bool HasValueEquality(Type type) =>
        type.GetMethod(
            "Equals",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [typeof(object)],
            null
        )?.DeclaringType != typeof(object);

    private static string Article(string name) =>
        name[0] is 'I' or 'A' or 'E' or 'O' or 'U' ? "an" : "a";
}
