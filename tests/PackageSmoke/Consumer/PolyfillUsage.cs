// Nullable is enabled here so the nullable attributes are meaningful in both the
// nullable-enabled and nullable-disabled consumer builds.
#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;

namespace Haitch.Roslyn.PackageSmoke;

// Records, init and required all lower to compiler-support types that netstandard2.0 lacks
// (IsExternalInit, RequiredMemberAttribute, CompilerFeatureRequiredAttribute).
internal sealed record PositionalRecord(int Id, string Name);

internal readonly record struct PositionalRecordStruct(int Id, string Name);

internal sealed class InitOnlyAndRequired
{
    public int Count { get; init; }

    public required string Label { get; init; }
}

internal static class PolyfillUsage
{
    internal static bool RecordsBehaveWithValueSemantics()
    {
        PositionalRecord original = new(1, "a");
        PositionalRecord changed = original with { Name = "b" };
        PositionalRecord same = original with { };

        PositionalRecordStruct structOriginal = new(1, "a");
        PositionalRecordStruct structChanged = structOriginal with { Id = 2 };

        return original == same && original != changed && structOriginal != structChanged;
    }

    internal static string InitAndRequiredMembers()
    {
        InitOnlyAndRequired value = new() { Count = 1, Label = "x" };

        return value.Label + value.Count;
    }

    // Index and Range are polyfilled types the compiler binds to for ^ and .. on strings.
    internal static string IndexAndRange(string text) => text[1..] + text[^1];

    // NotNullWhenAttribute is polyfilled, so the compiler honours the flow state it declares.
    internal static bool TryGetLength([NotNullWhen(true)] string? text, out int length)
    {
        length = text?.Length ?? 0;

        return text is not null;
    }

    internal static int LengthOrZero(string? text) => TryGetLength(text, out int length) ? text.Length + length : 0;

    // string.Contains(char) is an instance member missing from netstandard2.0, supplied as an extension.
    internal static bool ContainsChar(string text) => text.Contains('a');

    // C# 14 static extension: Math.Clamp does not exist on netstandard2.0. ArgumentNullException.ThrowIfNull
    // is not used because Polyfill gates it behind the opt-in PolyArgumentExceptions property.
    internal static int Clamp(int value) => Math.Clamp(value, 0, 10);
}
