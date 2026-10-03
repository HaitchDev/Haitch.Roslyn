namespace Haitch.Roslyn.CSharp15.Tests;

internal static class CSharp15Polyfills
{
    // Compilation source, not part of this assembly: the net10 references lack the types the C# 15 compiler
    // looks for (CS0518/CS0656 without them).
    public const string Source = """
        namespace System.Runtime.CompilerServices
        {
            public interface IUnion
            {
                object? Value { get; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class UnionAttribute : System.Attribute
            {
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class IsClosedTypeAttribute : System.Attribute
            {
            }
        }
        """;
}
