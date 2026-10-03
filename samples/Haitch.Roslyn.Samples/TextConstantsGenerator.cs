using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Haitch.Roslyn.Diagnostics;
using Haitch.Roslyn.Generators;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Samples;

// A sample ships no AnalyzerReleases files; real generators should track their rule ids there.
#pragma warning disable RS2008

// Turns every *.txt additional file into a string constant on one static class, TextConstants, in
// the RootNamespace build property's namespace (GeneratedText when it is missing or not a namespace).
//
// Constant name rule: the file name without its extension, split at every character that is not a
// letter or digit, each piece (and each letter after a digit) upper-cased and joined
// (my-file.txt -> MyFile, v2beta.txt -> V2Beta), with "_" in front when it starts with a digit. A name
// with no letters or digits derives to "", which is reported. The ConstantName item metadata
// replaces the derived name.
// The project must expose both inputs to the generator:
//   <CompilerVisibleProperty Include="RootNamespace" />
//   <CompilerVisibleItemMetadata Include="AdditionalFiles" MetadataName="ConstantName" />
//
// Constant values are regular escaped string literals (never raw strings), so the output compiles at
// any C# version.
//
// Guiding rule: a file that cannot become a constant is reported and left out; the rest still emit.
//
// Diagnostics: TEXTCONST001 file unreadable, TEXTCONST002 constant name unusable, TEXTCONST003
// constant name used by an earlier file, TEXTCONST004 RootNamespace is not a valid namespace
// (reported only when at least one constant is emitted).
public sealed class TextConstantsGenerator : IIncrementalGenerator
{
    private const string FilesStep = "TextConstantsGenerator.Files";
    private const string ClassName = "TextConstants";
    private const string FallbackNamespace = "GeneratedText";
    private const string NameMetadata = "ConstantName";

    private static readonly DiagnosticDescriptor Unreadable = new(
        "TEXTCONST001",
        "Text file cannot be read",
        "Cannot read the text of '{0}'",
        "TextConstants",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidName = new(
        "TEXTCONST002",
        "Constant name cannot be used",
        "'{0}' cannot use '{1}' as a constant name; it must be a valid identifier that is not a keyword or '"
            + ClassName
            + "'",
        "TextConstants",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor DuplicateName = new(
        "TEXTCONST003",
        "Constant name already used",
        "'{0}' would define '{1}', which an earlier file already defines; set ConstantName on one of them",
        "TextConstants",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor InvalidNamespace = new(
        "TEXTCONST004",
        "RootNamespace is not a valid namespace",
        "RootNamespace '{0}' is not a valid namespace, so '" + FallbackNamespace + "' is used",
        "TextConstants",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    // Members of object a static class inherits; a constant with one of these names hides it and warns.
    private static readonly HashSet<string> ObjectMembers = new(StringComparer.Ordinal)
    {
        "ToString",
        "Equals",
        "GetHashCode",
        "GetType",
        "ReferenceEquals",
        "MemberwiseClone",
        "Finalize",
    };

    private static readonly TypeRef StringType = new(
        FullyQualifiedName: "string",
        NullableAnnotation: NullableAnnotation.NotAnnotated,
        SpecialType: SpecialType.System_String,
        TypeKind: TypeKind.Class,
        IsValueType: false
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context
            .ForAdditionalFiles(
                static path => path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase),
                Unreadable,
                NameMetadata
            )
            .ReportDiagnostics(context, FilesStep);

        var rootNamespace = context.AnalyzerConfigOptionsProvider.ForBuildProperty("RootNamespace");

        context.RegisterSourceOutput(
            files.Collect().Combine(rootNamespace),
            static (spc, pair) => Emit(spc, pair.Left, pair.Right)
        );
    }

    // The name a file gets when it sets no ConstantName.
    public static string ConstantName(string path)
    {
        var start = path.LastIndexOfAny(['/', '\\']) + 1;
        var end = path.LastIndexOf('.');
        var stem = end >= start ? path.Substring(start, end - start) : path.Substring(start);

        var name = new StringBuilder();
        var upperNext = true;

        foreach (var c in stem)
        {
            if (char.IsLetterOrDigit(c))
            {
                name.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = char.IsDigit(c);
            }
            else
            {
                upperNext = true;
            }
        }

        if (name.Length == 0)
        {
            return "";
        }

        if (char.IsDigit(name[0]))
        {
            name.Insert(0, '_');
        }

        return name.ToString();
    }

    private static void Emit(
        SourceProductionContext spc,
        ImmutableArray<AdditionalFileModel> files,
        string? rootNamespace
    )
    {
        var constants = new List<(string Name, string Content)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Ordered by path so which file counts as the earlier one does not depend on input order.
        foreach (var file in files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            var custom = file[NameMetadata];
            var name = custom ?? ConstantName(file.Path);
            var location = new LocationInfo(file.Path, default, default);

            if (!IsUsableName(name))
            {
                Report(spc, new DiagnosticInfo(InvalidName, location, file.Path, name));
            }
            else if (!seen.Add(name))
            {
                Report(spc, new DiagnosticInfo(DuplicateName, location, file.Path, name));
            }
            else
            {
                constants.Add((name, file.Content));
            }
        }

        // TEXTCONST004 is reported only when at least one constant is emitted; with none there is no
        // namespace to get wrong.
        if (constants.Count == 0)
        {
            return;
        }

        var @namespace = rootNamespace ?? FallbackNamespace;

        if (!IsNamespace(@namespace))
        {
            Report(spc, new DiagnosticInfo(InvalidNamespace, null, @namespace));
            @namespace = FallbackNamespace;
        }

        constants.Sort((a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        spc.AddSource(ClassName + ".g.cs", Render(@namespace, constants));
    }

    private static void Report(SourceProductionContext spc, DiagnosticInfo diagnostic) =>
        spc.ReportDiagnostic(diagnostic.ToDiagnostic());

    // IsValidIdentifier accepts keywords, which a constant cannot be named.
    private static bool IsUsableName(string name) =>
        SyntaxFacts.IsValidIdentifier(name)
        && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
        && name != ClassName
        && !ObjectMembers.Contains(name);

    private static bool IsNamespace(string name) =>
        name.Split('.')
            .All(part =>
                SyntaxFacts.IsValidIdentifier(part)
                && SyntaxFacts.GetKeywordKind(part) == SyntaxKind.None
            );

    private static string Render(string @namespace, List<(string Name, string Content)> constants)
    {
        var writer = new SourceWriter();

        using (var file = writer.File())
        using (var ns = file.Namespace(@namespace))
        using (
            var type = ns.NewType(
                new NewTypeModel(ClassName, TypeDeclarationKind.Class, Accessibility.Public)
                {
                    IsStatic = true,
                }
            )
        )
        {
            foreach (var (name, content) in constants)
            {
                type.Field(
                    new FieldModel(
                        Name: name,
                        Type: StringType,
                        Accessibility: Accessibility.Public,
                        IsStatic: false,
                        IsReadOnly: false,
                        IsConst: true,
                        IsRequired: false,
                        ConstantValue: ConstantValue.ForString(content),
                        Attributes: default
                    )
                );
            }
        }

        return writer.ToString();
    }
}
