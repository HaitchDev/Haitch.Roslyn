using Haitch.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.CSharp15.Tests;

public class ExtensionBlockTests
{
    private const string Source = """
                                  #nullable enable
                                  using System;
                                  using System.Collections.Generic;
                                  namespace App;
                                  public static class StringExtensions
                                  {
                                      public static int Twice(int value) => value * 2;

                                      extension(string text)
                                      {
                                          public int WordCount => text.Split(' ').Length;
                                          public string Shout() => text.ToUpperInvariant();
                                          public char this[Index index] => text[index];
                                          public static string Empty2 => "";
                                      }

                                      extension<T>(List<T> list)
                                      {
                                          public bool IsSingle => list.Count == 1;
                                      }
                                  }
                                  """;

    private static readonly MetadataReference[] References = CreateReferences();

    [Test]
    public async Task From_ExtensionBlock_ThrowsArgumentExceptionNamingExtensionBlock()
    {
        var compilation = Compile(Source);
        var block = compilation.GetTypeByMetadataName("App.StringExtensions")!.GetTypeMembers().First();

        var exception = Assert.Throws<ArgumentException>(() => TypeModel.From(block));

        await Assert.That(exception.ParamName).IsEqualTo("type");
        await Assert.That(exception.Message).Contains("extension block");
    }

    [Test]
    public async Task From_ContainingStaticClassWithMembers_ListsOnlyOrdinaryStaticMethods()
    {
        var compilation = Compile(Source);

        var model = TypeModel.From(compilation.GetTypeByMetadataName("App.StringExtensions")!, includeMembers: true);

        await Assert.That(model.IsStatic).IsTrue();
        await Assert.That(model.Methods.Select(method => method.Name).ToArray()).IsEquivalentTo(["Twice"]);
        await Assert.That(model.Properties.ToArray()).IsEmpty();
        await Assert.That(model.Fields.ToArray()).IsEmpty();
    }

    [Test]
    public async Task From_ContainingStaticClass_HasNoNestedTypesInContainingTypes()
    {
        var compilation = Compile(Source);

        var model = TypeModel.From(compilation.GetTypeByMetadataName("App.StringExtensions")!);

        await Assert.That(model.ContainingTypes.ToArray()).IsEmpty();
        await Assert.That(model.Kind).IsEqualTo(TypeDeclarationKind.Class);
    }

    [Test]
    public async Task From_ExtensionBlockFromMetadata_ThrowsArgumentExceptionNamingExtensionBlock()
    {
        using var stream = new MemoryStream();
        var emitResult = Compile(Source).Emit(stream);
        await Assert.That(emitResult.Success).IsTrue();
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        var consumer = Compile("public class Consumer;", reference);
        var block = consumer.GetTypeByMetadataName("App.StringExtensions")!.GetTypeMembers().First();

        var exception = Assert.Throws<ArgumentException>(() => TypeModel.From(block));

        await Assert.That(exception.Message).Contains("extension block");
    }

    private static CSharpCompilation Compile(string source, params MetadataReference[] extraReferences)
    {
        return CSharpCompilation.Create(
            "ExtensionBlockTests" + Guid.NewGuid().ToString("N"),
            [
                CSharpSyntaxTree.ParseText(source, CSharp15.ParseOptions),
                CSharpSyntaxTree.ParseText(CSharp15Polyfills.Source, CSharp15.ParseOptions),
            ],
            [.. References, .. extraReferences],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
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
