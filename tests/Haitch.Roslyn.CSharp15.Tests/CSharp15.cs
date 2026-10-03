using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.CSharp15.Tests;

internal static class CSharp15
{
    // Roslyn 5.9.0 has no LanguageVersion.CSharp15; unions and closed types parse only under Preview.
    public static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
}
