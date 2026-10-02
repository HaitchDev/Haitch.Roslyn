using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Writing.Scopes;

/// <summary>
/// Compiles a snippet against all shipped sources (the models depend on diagnostics types) so tests can prove that illegal scope
/// nesting is rejected by the compiler. Not <see cref="Models.CompilationHelper"/>, which pins
/// C# 12 and compiles only the snippet.
/// </summary>
internal static class ScopeCompileCheck
{
    private static readonly MetadataReference[] References = CreateReferences();
    private static readonly Lazy<SyntaxTree[]> ShippedTrees = new(ParseShippedSources);

    /// <summary>
    /// Wraps <paramref name="body"/> in a method with <c>writer</c> in scope and returns the ids of
    /// every error produced, in source order.
    /// </summary>
    public static string[] Compile(string body)
    {
        string source =
            $$"""
              using Haitch.Roslyn.Writing;

              internal static class Snippet
              {
                  internal static void Run(SourceWriter writer)
                  {
              {{body}}
                  }
              }
              """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        SyntaxTree snippet = CSharpSyntaxTree.ParseText(source, parseOptions);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ScopeCompileCheckAssembly",
            [snippet, .. ShippedTrees.Value],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id)
            .ToArray();
    }

    private static SyntaxTree[] ParseShippedSources()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new List<SyntaxTree>();

        string root = Path.Combine(RepoPaths.Root, "src", "Haitch.Roslyn");

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file);

            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file));
        }

        return trees.ToArray();
    }

    private static MetadataReference[] CreateReferences()
    {
        var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        var references = trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        // The shipped sources use Roslyn types (SourceText, Accessibility, ...).
        references.Add(MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(CSharpCompilation).Assembly.Location));

        return references.ToArray();
    }
}
