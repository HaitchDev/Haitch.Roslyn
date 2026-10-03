# Haitch.Roslyn

Source-only utilities for Roslyn incremental source generators: value-equal models, attribute and member discovery, typed attribute arguments, cache-safe diagnostics and scoped source writing, plus a test harness. Part of the [Haitch](https://haitch.dev) suite of foundational libraries.

## Install

```shell
dotnet add package Haitch.Roslyn          # source-only, compiles into your generator as internal types
dotnet add package Haitch.Roslyn.Testing  # generator test harness and assertions
```

Haitch.Roslyn needs C# 14 (`<LangVersion>14</LangVersion>`) and your own reference to `Microsoft.CodeAnalysis.CSharp` 4.12.0 or later. Reference both packages with `PrivateAssets="all"`.

## Quick start

```csharp
using System;
using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;

[Generator]
public sealed class HelloGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddEmbeddedAttributeDefinition();
            ctx.AddMarkerAttribute("HelloAttribute.g.cs", "Hello", "HelloAttribute", AttributeTargets.Class);
        });

        var types = context.SyntaxProvider.ForTypesWithAttribute("Hello.HelloAttribute", "Types");

        context.RegisterSourceOutput(types, static (spc, item) =>
        {
            var writer = new SourceWriter();
            using (var file = writer.File())
            {
                if (item.Type.Namespace is { } @namespace)
                {
                    using var ns = file.Namespace(@namespace);
                    WriteGreeting(writer, ns.Type(item.Type), item.Type);
                }
                else
                {
                    WriteGreeting(writer, file.Type(item.Type), item.Type);
                }
            }

            spc.AddSource(HintName.For(item.Type, "Hello"), writer.ToString());
        });
    }

    private static void WriteGreeting(SourceWriter writer, TypeScope scope, TypeModel type)
    {
        using (scope)
        {
            writer.WriteLine($"public const string Greeting = \"Hello from {type.Name}\";");
        }
    }
}
```

Test it with the harness:

```csharp
var result = GeneratorHarness.Run(new HelloGenerator(), ["[Hello.Hello] public partial class Foo;"]);
GeneratorHarness.AssertCacheable(new HelloGenerator(), ["[Hello.Hello] public partial class Foo;"], "Types");
```

## Documentation

Full documentation is at [haitch.dev/libraries/roslyn](https://haitch.dev/libraries/roslyn/overview/).
