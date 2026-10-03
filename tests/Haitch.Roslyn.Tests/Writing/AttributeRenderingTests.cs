using System.Linq;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Types;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Haitch.Roslyn.Tests.Writing;

public class AttributeRenderingTests
{
    private const string Definitions = """
        using System;

        namespace Attrs;

        public enum Color { Red = 1, Negative = -1 }

        public class PlainAttribute : Attribute { }

        public class PrimitivesAttribute : Attribute
        {
            public PrimitivesAttribute(int i, long l, uint u, ulong ul, bool b, char c, float f, double d) { }
        }

        public class TextAttribute : Attribute
        {
            public TextAttribute(string text) { }
        }

        public class CharsAttribute : Attribute
        {
            public CharsAttribute(char quote, char newline, char backslash) { }
        }

        public class EnumsAttribute : Attribute
        {
            public EnumsAttribute(Color positive, Color negative) { }
        }

        public class TypeArgAttribute : Attribute
        {
            public TypeArgAttribute(Type type) { }
        }

        public class ArrayAttribute : Attribute
        {
            public ArrayAttribute(int[] values) { }
        }

        public class NullsAttribute : Attribute
        {
            public NullsAttribute(string? text, Type? type) { }
        }

        public class NamedAttribute : Attribute
        {
            public NamedAttribute(string name) { }

            public int Count { get; set; }

            public string? Label { get; set; }
        }

        public class ParamsAttribute : Attribute
        {
            public ParamsAttribute(params int[] values) { }
        }

        public class GenAttribute<T> : Attribute
        {
            public GenAttribute(T value) { }
        }

        [Flags]
        public enum Mode { None = 0, A = 1, B = 2 }

        public class ModeAttribute : Attribute
        {
            public ModeAttribute(Mode mode) { }
        }

        public class ObjectsAttribute : Attribute
        {
            public ObjectsAttribute(params object?[] values) { }
        }

        public class ObjAttribute : Attribute
        {
            public ObjAttribute(object value) { }
        }

        public class OverloadedAttribute : Attribute
        {
            public OverloadedAttribute(string? text) { }

            public OverloadedAttribute(Type? type) { }
        }

        public class KeywordAttribute : Attribute
        {
            public int @class { get; set; }
        }

        public class Outer
        {
            public class InnerAttribute : Attribute { }
        }

        public enum Alias { Default = 0, None = 0 }

        public class AliasUseAttribute : Attribute
        {
            public AliasUseAttribute(Alias alias) { }
        }
        """;

    private static readonly string[] Usages =
    [
        "[Plain]",
        "[Primitives(42, 5L, 7U, 9UL, true, 'x', 1.5f, 2.5)]",
        "[Text(\"a\\\"b\\\\c\\n\\t\")]",
        "[Chars('\\'', '\\n', '\\\\')]",
        "[Enums(Color.Red, Color.Negative)]",
        "[TypeArg(typeof(System.Collections.Generic.List<int>))]",
        "[Array(new[] { 1, 2, 3 })]",
        "[Nulls(null, null)]",
        "[Named(\"x\", Count = 3, Label = \"n\")]",
        "[Params(1, 2, 3)]",
        "[Gen<int>(5)]",
        "[Params]",
        "[Objects(1, \"a\", null, Color.Red)]",
        "[Objects(null, null)]",
        "[Enums((Color)5, (Color)(-2))]",
        "[Mode(Mode.A | Mode.B)]",
        "[Obj((byte)1)]",
        "[Overloaded((string?)null)]",
        "[Keyword(@class = 4)]",
        "[Outer.Inner]",
        "[AliasUse(Alias.None)]",
        "[TypeArg(typeof(System.Collections.Generic.List<>))]",
    ];

    [Test]
    public async Task Should_render_an_attribute_without_arguments_without_parentheses()
    {
        string rendered = RenderUsage("[Plain]");

        await Assert.That(rendered).IsEqualTo("[global::Attrs.PlainAttribute]");
    }

    [Test]
    public async Task Should_render_primitive_constructor_arguments_with_suffixes()
    {
        string rendered = RenderUsage("[Primitives(42, 5L, 7U, 9UL, true, 'x', 1.5f, 2.5)]");

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::Attrs.PrimitivesAttribute(42, 5L, 7U, 9UL, true, 'x', 1.5f, 2.5d)]"
            );
    }

    [Test]
    public async Task Should_render_a_string_with_escapes()
    {
        string rendered = RenderUsage("[Text(\"a\\\"b\\\\c\\n\\t\")]");

        await Assert
            .That(rendered)
            .IsEqualTo("[global::Attrs.TextAttribute(\"a\\\"b\\\\c\\n\\t\")]");
    }

    [Test]
    public async Task Should_render_chars_with_escapes()
    {
        string rendered = RenderUsage("[Chars('\\'', '\\n', '\\\\')]");

        await Assert
            .That(rendered)
            .IsEqualTo("[global::Attrs.CharsAttribute('\\'', '\\n', '\\\\')]");
    }

    [Test]
    public async Task Should_render_enums_by_member_name()
    {
        string rendered = RenderUsage("[Enums(Color.Red, Color.Negative)]");

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::Attrs.EnumsAttribute(global::Attrs.Color.Red, global::Attrs.Color.Negative)]"
            );
    }

    [Test]
    public async Task Should_render_undefined_enum_values_as_casts_including_a_negative_value()
    {
        string rendered = RenderUsage("[Enums((Color)5, (Color)(-2))]");

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::Attrs.EnumsAttribute((global::Attrs.Color)5, (global::Attrs.Color)(-2))]"
            );
    }

    [Test]
    public async Task Should_render_a_flags_combination_as_a_cast()
    {
        string rendered = RenderUsage("[Mode(Mode.A | Mode.B)]");

        await Assert
            .That(rendered)
            .IsEqualTo("[global::Attrs.ModeAttribute((global::Attrs.Mode)3)]");
    }

    [Test]
    public async Task Should_render_a_typeof_argument()
    {
        string rendered = RenderUsage("[TypeArg(typeof(System.Collections.Generic.List<int>))]");

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::Attrs.TypeArgAttribute(typeof(global::System.Collections.Generic.List<int>))]"
            );
    }

    [Test]
    public async Task Should_render_an_array_argument()
    {
        string rendered = RenderUsage("[Array(new[] { 1, 2, 3 })]");

        await Assert
            .That(rendered)
            .IsEqualTo("[global::Attrs.ArrayAttribute(new int[] { 1, 2, 3 })]");
    }

    [Test]
    public async Task Should_render_null_arguments()
    {
        string rendered = RenderUsage("[Nulls(null, null)]");

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::Attrs.NullsAttribute(default(string), default(global::System.Type))]"
            );
    }

    [Test]
    public async Task Should_render_named_arguments_after_positional_ones()
    {
        string rendered = RenderUsage("[Named(\"x\", Count = 3, Label = \"n\")]");

        await Assert
            .That(rendered)
            .IsEqualTo("[global::Attrs.NamedAttribute(\"x\", Count = 3, Label = \"n\")]");
    }

    [Test]
    public async Task Should_render_a_params_constructor_as_an_array()
    {
        string rendered = RenderUsage("[Params(1, 2, 3)]");

        await Assert
            .That(rendered)
            .IsEqualTo("[global::Attrs.ParamsAttribute(new int[] { 1, 2, 3 })]");
    }

    [Test]
    public async Task Should_render_an_empty_params_array_with_its_type()
    {
        string rendered = RenderUsage("[Params]");

        await Assert.That(rendered).IsEqualTo("[global::Attrs.ParamsAttribute(new int[] { })]");
    }

    [Test]
    public async Task Should_render_a_mixed_object_array_with_its_type()
    {
        string rendered = RenderUsage("[Objects(1, \"a\", null, Color.Red)]");

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::Attrs.ObjectsAttribute(new object?[] { 1, \"a\", default(object), global::Attrs.Color.Red })]"
            );
    }

    [Test]
    public async Task Should_render_an_all_null_array_with_its_type()
    {
        string rendered = RenderUsage("[Objects(null, null)]");

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::Attrs.ObjectsAttribute(new object?[] { default(object), default(object) })]"
            );
    }

    [Test]
    public async Task Should_cast_small_integer_arguments_to_preserve_their_type()
    {
        string rendered = RenderUsage("[Obj((byte)1)]");

        await Assert.That(rendered).IsEqualTo("[global::Attrs.ObjAttribute((byte)1)]");
    }

    [Test]
    public async Task Should_cast_a_typed_null_so_overloads_stay_unambiguous()
    {
        string rendered = RenderUsage("[Overloaded((string?)null)]");

        await Assert
            .That(rendered)
            .IsEqualTo("[global::Attrs.OverloadedAttribute(default(string))]");
    }

    [Test]
    public async Task Should_render_an_aliased_enum_value_as_a_cast()
    {
        string rendered = RenderUsage("[AliasUse(Alias.None)]");

        await Assert
            .That(rendered)
            .IsEqualTo("[global::Attrs.AliasUseAttribute((global::Attrs.Alias)0)]");
    }

    [Test]
    public async Task Should_escape_keyword_named_arguments()
    {
        string rendered = RenderUsage("[Keyword(@class = 4)]");

        await Assert.That(rendered).IsEqualTo("[global::Attrs.KeywordAttribute(@class = 4)]");
    }

    [Test]
    public async Task Should_render_a_nested_attribute_class()
    {
        string rendered = RenderUsage("[Outer.Inner]");

        await Assert.That(rendered).IsEqualTo("[global::Attrs.Outer.InnerAttribute]");
    }

    [Test]
    public async Task Should_render_an_unbound_generic_typeof()
    {
        string rendered = RenderUsage("[TypeArg(typeof(System.Collections.Generic.List<>))]");

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::Attrs.TypeArgAttribute(typeof(global::System.Collections.Generic.List<>))]"
            );
    }

    [Test]
    public async Task Should_round_trip_rendered_attributes_to_equal_models()
    {
        AttributeModel[] original = GetModels(Usages);
        string[] renderedUsages = original.Select(SourceWriterExtensions.RenderAttribute).ToArray();

        AttributeModel[] reparsed = GetModels(renderedUsages);

        for (var i = 0; i < original.Length; i++)
        {
            await Assert.That(reparsed[i]).IsEqualTo(original[i]);
        }
    }

    [Test]
    public async Task Should_render_a_generic_attribute()
    {
        string rendered = RenderUsage("[Gen<int>(5)]");

        await Assert.That(rendered).IsEqualTo("[global::Attrs.GenAttribute<int>(5)]");
    }

    [Test]
    public async Task Should_write_one_line_per_attribute_in_order()
    {
        AttributeModel[] models = GetModels(["[Plain]", "[Text(\"a\")]"]);
        SourceWriter writer = new();

        writer.WriteAttributes(models.ToEquatableArray());

        await Assert
            .That(writer.ToString())
            .IsEqualTo("[global::Attrs.PlainAttribute]\n[global::Attrs.TextAttribute(\"a\")]\n");
    }

    [Test]
    public async Task Should_write_nothing_for_an_empty_attribute_list()
    {
        SourceWriter writer = new();

        writer.WriteAttributes(default);

        await Assert.That(writer.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Should_throw_for_an_error_constant_and_leave_the_writer_untouched()
    {
        AttributeModel plain = GetModels(["[Plain]"])[0];
        AttributeModel withError = plain with
        {
            ConstructorArguments = new[] { ConstantValue.Error }.ToEquatableArray(),
        };
        SourceWriter writer = new();

        await Assert.That(() => writer.WriteAttribute(withError)).Throws<ArgumentException>();
        await Assert.That(writer.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Should_throw_for_an_error_constant_before_writing_earlier_attributes()
    {
        AttributeModel plain = GetModels(["[Plain]"])[0];
        AttributeModel withError = plain with
        {
            ConstructorArguments = new[] { ConstantValue.Error }.ToEquatableArray(),
        };
        SourceWriter writer = new();

        await Assert
            .That(() => writer.WriteAttributes(new[] { plain, withError }.ToEquatableArray()))
            .Throws<ArgumentException>();
        await Assert.That(writer.ToString()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Should_compile_a_type_decorated_with_the_rendered_attributes()
    {
        AttributeModel[] models = GetModels(Usages);
        string declarations = string.Join(
            "\n",
            models.Select(
                (model, i) =>
                {
                    SourceWriter writer = new();
                    writer.WriteAttribute(model);

                    return $"{writer}class Decorated{i} {{ }}";
                }
            )
        );

        string source = $"{Definitions}\n\n{declarations}\n";
        CSharpCompilation compilation = CompilationHelper.Compile(source, allowErrors: true);
        string[] errors = compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.Id)
            .ToArray();

        await Assert.That(models.Length).IsEqualTo(Usages.Length);
        await Assert.That(string.Join(",", errors)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Should_render_the_generated_code_attribute()
    {
        string rendered = SourceWriterExtensions.RenderAttribute(
            WellKnownAttributes.GeneratedCode("Tool", "1.0.0")
        );

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Tool\", \"1.0.0\")]"
            );
    }

    [Test]
    public async Task Should_render_the_editor_browsable_never_attribute()
    {
        string rendered = SourceWriterExtensions.RenderAttribute(
            WellKnownAttributes.EditorBrowsableNever
        );

        await Assert
            .That(rendered)
            .IsEqualTo(
                "[global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Never)]"
            );
    }

    [Test]
    public async Task Should_build_generated_code_equal_to_the_model_read_from_source()
    {
        AttributeModel fromSource = GetModelsFromSource([
            "[System.CodeDom.Compiler.GeneratedCode(\"Tool\", \"1.0.0\")]",
        ])[0];

        await Assert.That(WellKnownAttributes.GeneratedCode("Tool", "1.0.0")).IsEqualTo(fromSource);
    }

    [Test]
    public async Task Should_build_editor_browsable_never_equal_to_the_model_read_from_source()
    {
        AttributeModel fromSource = GetModelsFromSource([
            "[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]",
        ])[0];

        await Assert.That(WellKnownAttributes.EditorBrowsableNever).IsEqualTo(fromSource);
    }

    [Test]
    public async Task Should_give_well_known_models_the_metadata_name_read_from_source()
    {
        AttributeModel[] fromSource = GetModelsFromSource([
            "[System.CodeDom.Compiler.GeneratedCode(\"Tool\", \"1.0.0\")]",
            "[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]",
        ]);

        await Assert
            .That(WellKnownAttributes.GeneratedCode("Tool", "1.0.0").MetadataName)
            .IsEqualTo(fromSource[0].MetadataName);
        await Assert
            .That(WellKnownAttributes.EditorBrowsableNever.MetadataName)
            .IsEqualTo(fromSource[1].MetadataName);
    }

    [Test]
    public async Task Should_compile_a_type_and_a_method_carrying_both_well_known_attributes()
    {
        SourceWriter writer = new();
        writer.WriteAttribute(WellKnownAttributes.GeneratedCode("Tool", "1.0.0"));
        writer.WriteAttribute(WellKnownAttributes.EditorBrowsableNever);
        string attributes = writer.ToString();

        string source = $"{attributes}class Decorated\n{{\n{attributes}void Method() {{ }}\n}}\n";
        CSharpCompilation compilation = CompilationHelper.Compile(source, allowErrors: true);
        string[] errors = compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id)
            .ToArray();

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task Should_not_be_hijacked_by_consumer_types_with_the_same_names()
    {
        SourceWriter writer = new();
        writer.WriteAttribute(WellKnownAttributes.GeneratedCode("Tool", "1.0.0"));
        writer.WriteAttribute(WellKnownAttributes.EditorBrowsableNever);

        string source = $$"""
            namespace Consumer
            {
                class System { }
                class GeneratedCodeAttribute : global::System.Attribute { }
                class EditorBrowsableAttribute : global::System.Attribute { }
                class EditorBrowsableState { }

                {{writer}}class Decorated { }
            }
            """;
        CSharpCompilation compilation = CompilationHelper.Compile(source, allowErrors: true);
        string[] errors = compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id)
            .ToArray();

        await Assert.That(errors).IsEmpty();
    }

    private static AttributeModel[] GetModelsFromSource(string[] usages)
    {
        string source = string.Join(
            "\n",
            usages.Select((usage, i) => $"{usage}\nclass Target{i} {{ }}")
        );
        CSharpCompilation compilation = CompilationHelper.Compile(source);

        return usages
            .Select(
                (_, i) => compilation.GetTypeByMetadataName($"Target{i}")!.GetAttributes().Single()
            )
            .Select(attribute => AttributeModel.From(attribute)!)
            .ToArray();
    }

    private static string RenderUsage(string usage)
    {
        AttributeModel model = GetModels([usage])[0];

        return SourceWriterExtensions.RenderAttribute(model);
    }

    private static AttributeModel[] GetModels(string[] usages)
    {
        string source =
            $"{Definitions}\n\n{string.Join("\n", usages.Select((usage, i) => $"{usage}\nclass Target{i} {{ }}"))}\n";
        CSharpCompilation compilation = CompilationHelper.Compile(source);

        return usages
            .Select(
                (_, i) =>
                    compilation.GetTypeByMetadataName($"Attrs.Target{i}")!.GetAttributes().Single()
            )
            .Select(attribute => AttributeModel.From(attribute)!)
            .ToArray();
    }
}
