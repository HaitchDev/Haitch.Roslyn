using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Models;

/// <summary>
/// Compiles a source string into a <see cref="CSharpCompilation"/> using the reference
/// assemblies of the runtime the tests are executing on. Shared by the model tests (3.1-3.4).
/// </summary>
internal static class CompilationHelper
{
    private static readonly MetadataReference[] References = CreateReferences();
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    public static CSharpCompilation Compile(string source, bool allowErrors = false)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, ParseOptions);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "CompilationHelperAssembly",
            [syntaxTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true
            )
        );

        Diagnostic[] errors = compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (!allowErrors && errors.Length > 0)
        {
            throw new InvalidOperationException(
                $"Source failed to compile:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}"
            );
        }

        return compilation;
    }

    public static INamedTypeSymbol GetNamedTypeSymbol(
        string source,
        string metadataName,
        bool allowErrors = false
    )
    {
        CSharpCompilation compilation = Compile(source, allowErrors);
        INamedTypeSymbol? symbol = compilation.GetTypeByMetadataName(metadataName);

        if (symbol is null)
        {
            throw new InvalidOperationException(
                $"Type '{metadataName}' was not found in the compiled source."
            );
        }

        return symbol;
    }

    private static MetadataReference[] CreateReferences()
    {
        var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
