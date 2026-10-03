using System.Reflection;

namespace Haitch.Roslyn.Tests;

public class PackageSurfaceTests
{
    [Test]
    public async Task Should_expose_no_public_types()
    {
        // Source-only package: public types would collide with identically named types in consumers.
        Assembly assembly = typeof(PackageSurfaceTests).Assembly;

        IEnumerable<Type> publicPackageTypes = assembly
            .GetTypes()
            .Where(IsPackageType)
            .Where(type => type.IsVisible);

        await Assert.That(publicPackageTypes).IsEmpty();
    }

    // The exact namespaces the Polyfill and TUnit source generators emit into this assembly
    // (inspected via the compiled test DLL) — not a broad System*/Microsoft* exclusion, which
    // would also hide a genuine public type accidentally declared in one of those namespaces.
    private static readonly string[] GeneratedNamespaces =
    [
        "Polyfills",
        "System.Buffers",
        "System.Diagnostics",
        "System.IO",
        "System.Runtime.CompilerServices",
        "System.Text",
        "System.Text.Unicode",
        "TUnit.Generated",
    ];

    private static bool IsPackageType(Type type)
    {
        string? ns = type.Namespace;

        if (ns is null)
        {
            // Global-namespace types come either from src (no `namespace` declaration) or from
            // compiler-synthesized artifacts (e.g. <Module>, <PrivateImplementationDetails>); the
            // latter are never public, but guard on the synthetic name shape defensively.
            return !type.Name.StartsWith("<", StringComparison.Ordinal);
        }

        bool isExcludedNamespace =
            GeneratedNamespaces.Contains(ns)
            || GeneratedNamespaces.Any(generated =>
                ns.StartsWith(generated + ".", StringComparison.Ordinal)
            );

        if (isExcludedNamespace)
        {
            return false;
        }

        bool isHaitchRoslyn =
            ns == "Haitch.Roslyn" || ns.StartsWith("Haitch.Roslyn.", StringComparison.Ordinal);
        bool isTestNamespace =
            ns == "Haitch.Roslyn.Tests"
            || ns.StartsWith("Haitch.Roslyn.Tests.", StringComparison.Ordinal);

        return isHaitchRoslyn && !isTestNamespace;
    }
}
