using Haitch.Roslyn.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.CSharp15.Tests;

public class PartialEventModelTests
{
    private const string TwoParts = """
        using System;
        namespace App;
        public partial class Host
        {
            [Obsolete("a")]
            public partial event EventHandler E;
        }
        public partial class Host
        {
            [System.ComponentModel.Description("b")]
            public partial event EventHandler E { add { } remove { } }
        }
        """;

    private static readonly MetadataReference[] References = CreateReferences();

    [Test]
    public async Task From_PartialEvent_YieldsOneModelThatIsPartialAndNotFieldLike()
    {
        var events = EventsOf(TwoParts, "App.Host");

        await Assert.That(events.Length).IsEqualTo(1);
        await Assert.That(events[0].Name).IsEqualTo("E");
        await Assert.That(events[0].IsPartial).IsTrue();
        await Assert.That(events[0].IsFieldLike).IsFalse();
    }

    [Test]
    public async Task From_PartialEvent_MergesAttributesFromBothParts()
    {
        var events = EventsOf(TwoParts, "App.Host");

        var names = events[0].Attributes.Select(a => a.MetadataName).ToArray();

        await Assert.That(names).Contains("System.ObsoleteAttribute");
        await Assert.That(names).Contains("System.ComponentModel.DescriptionAttribute");
    }

    [Test]
    public async Task From_StaticPartialEvent_IsStaticAndPartial()
    {
        var events = EventsOf(
            """
            using System;
            namespace App;
            public partial class Host
            {
                public static partial event EventHandler E;
            }
            public partial class Host
            {
                public static partial event EventHandler E { add { } remove { } }
            }
            """,
            "App.Host"
        );

        await Assert.That(events.Length).IsEqualTo(1);
        await Assert.That(events[0].IsStatic).IsTrue();
        await Assert.That(events[0].IsPartial).IsTrue();
        await Assert.That(events[0].IsFieldLike).IsFalse();
    }

    [Test]
    public async Task From_FieldLikeEvent_IsNotPartial()
    {
        var events = EventsOf(
            "using System; public class Host { public event EventHandler E; }",
            "Host"
        );

        await Assert.That(events.Length).IsEqualTo(1);
        await Assert.That(events[0].IsPartial).IsFalse();
        await Assert.That(events[0].IsFieldLike).IsTrue();
    }

    [Test]
    public async Task From_AccessorEvent_IsNotPartial()
    {
        var events = EventsOf(
            "using System; public class Host { public event EventHandler E { add { } remove { } } }",
            "Host"
        );

        await Assert.That(events.Length).IsEqualTo(1);
        await Assert.That(events[0].IsPartial).IsFalse();
        await Assert.That(events[0].IsFieldLike).IsFalse();
    }

    [Test]
    public async Task From_PartialEventPartsInSeparateSyntaxTrees_YieldsOneModel()
    {
        var events = EventsOfTrees(
            "App.Host",
            """
            using System;
            namespace App;
            public partial class Host { public partial event EventHandler E; }
            """,
            """
            using System;
            namespace App;
            public partial class Host { public partial event EventHandler E { add { } remove { } } }
            """
        );

        await Assert.That(events.Length).IsEqualTo(1);
        await Assert.That(events[0].IsPartial).IsTrue();
        await Assert.That(events[0].IsFieldLike).IsFalse();
    }

    [Test]
    public async Task From_PartialEventInNestedGenericType_YieldsOneModel()
    {
        var events = EventsOf(
            """
            using System;
            namespace App;
            public partial class Outer<T>
            {
                public partial class Inner
                {
                    public partial event EventHandler E;
                    public partial event EventHandler E { add { } remove { } }
                }
            }
            """,
            "App.Outer`1+Inner"
        );

        await Assert.That(events.Length).IsEqualTo(1);
        await Assert.That(events[0].IsPartial).IsTrue();
        await Assert.That(events[0].IsFieldLike).IsFalse();
    }

    private static EventModel[] EventsOf(string source, string typeName) =>
        EventsOfTrees(typeName, source);

    private static EventModel[] EventsOfTrees(string typeName, params string[] sources)
    {
        var compilation = CSharpCompilation.Create(
            "PartialEventModelTests" + Guid.NewGuid().ToString("N"),
            sources
                .Append(CSharp15Polyfills.Source)
                .Select(text => CSharpSyntaxTree.ParseText(text, CSharp15.ParseOptions)),
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, errors.Select(d => d.ToString()))
            );
        }

        return TypeModel
            .From(compilation.GetTypeByMetadataName(typeName)!, includeMembers: true)
            .Events.ToArray();
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
