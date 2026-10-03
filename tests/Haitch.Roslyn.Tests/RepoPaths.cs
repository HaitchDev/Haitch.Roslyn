using System.Reflection;

namespace Haitch.Roslyn.Tests;

internal static class RepoPaths
{
    public static string Root { get; } = GetRepoRoot();

    private static string GetRepoRoot()
    {
        // AppContext.BaseDirectory breaks under an out-of-tree --artifacts-path, and
        // [CallerFilePath] is rewritten to /_/ under ContinuousIntegrationBuild /
        // DeterministicSourcePaths, so the repo root is baked in at build time instead.
        AssemblyMetadataAttribute? attribute = typeof(RepoPaths)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(metadata => metadata.Key == "RepoRoot");

        if (attribute?.Value is not { Length: > 0 } value)
        {
            throw new InvalidOperationException(
                "RepoRoot assembly metadata was not found; check the AssemblyMetadata item in Haitch.Roslyn.Tests.csproj."
            );
        }

        return value;
    }
}
