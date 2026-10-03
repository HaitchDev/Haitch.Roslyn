using System.Linq;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Tests.Models;
using Haitch.Roslyn.Writing;
using Microsoft.CodeAnalysis;

namespace Haitch.Roslyn.Tests.Writing.Scopes;

public class ScopeGuardTests
{
    private const string SampleSource = "public partial class Sample { public void Other() { } }";

    private static TypeModel Sample()
    {
        return TypeModel.From(CompilationHelper.GetNamedTypeSymbol(SampleSource, "Sample"));
    }

    private static MethodModel Other()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(SampleSource, "Sample");

        return MethodModel.From(type.GetMembers("Other").OfType<IMethodSymbol>().Single());
    }

    private static string Render(Action<BodyScope> body)
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        using (var type = file.Type(Sample()))
        using (var method = type.Method(Other()))
        {
            body(method);
        }

        return writer.ToString();
    }

    [Test]
    public async Task Line_on_outer_body_while_if_is_open_throws()
    {
        InvalidOperationException? thrown = null;

        try
        {
            Render(outer =>
            {
                using var child = outer.If("a");
                outer.Line("late();");
            });
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains("BodyScope");
        await Assert.That(thrown.Message).Contains("still open");
    }

    [Test]
    public async Task Line_on_outer_body_while_foreach_is_open_throws()
    {
        await Assert
            .That(() =>
                Render(outer =>
                {
                    using var child = outer.ForEach("var", "x", "xs");
                    outer.Line("late();");
                })
            )
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Block_on_outer_body_while_child_is_open_throws()
    {
        await Assert
            .That(() =>
                Render(outer =>
                {
                    using var child = outer.If("a");
                    using var late = outer.Block("lock (o)");
                })
            )
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Break_on_outer_body_while_child_is_open_throws()
    {
        await Assert
            .That(() =>
                Render(outer =>
                {
                    using var child = outer.While("true");
                    outer.Break();
                })
            )
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Disposing_outer_body_while_child_is_open_closes_both_without_throwing()
    {
        string output = Render(outer =>
        {
            var child = outer.If("a");
            child.Line("work();");
        });

        await Assert
            .That(output)
            .Contains("        if (a)\n        {\n            work();\n        }\n    }\n}\n");
    }

    [Test]
    public async Task Disposing_a_copy_after_the_original_writes_nothing_more()
    {
        string withoutCopy = Render(outer => outer.Dispose());
        string withCopy = Render(outer =>
        {
            BodyScope copy = outer;
            outer.Dispose();
            copy.Dispose();
        });

        await Assert.That(withCopy).IsEqualTo(withoutCopy);
    }

    [Test]
    public async Task Disposing_a_stale_copy_does_not_close_a_sibling_opened_at_the_same_depth()
    {
        string output = Render(outer =>
        {
            BodyScope first = outer.Block("lock (a)");
            BodyScope stale = first;
            first.Dispose();

            BodyScope second = outer.Block("lock (b)");
            second.Line("x();");
            stale.Dispose();
            second.Line("y();");
            second.Dispose();
        });

        await Assert
            .That(output)
            .Contains(
                "        lock (b)\n        {\n            x();\n            y();\n        }\n    }\n}\n"
            );
    }

    [Test]
    public async Task Disposing_the_same_instance_twice_is_still_a_no_op()
    {
        string output = Render(outer =>
        {
            var child = outer.While("true");
            child.Line("work();");
            child.Dispose();
            child.Dispose();
        });

        await Assert
            .That(output)
            .Contains("        while (true)\n        {\n            work();\n        }\n    }");
    }

    [Test]
    public async Task Normal_nesting_and_disposal_order_renders_as_before()
    {
        string output = Render(outer =>
        {
            outer.Line("a();");

            using (var ifScope = outer.If("x"))
            {
                ifScope.Line("b();");

                using (var each = ifScope.ForEach("var", "i", "is"))
                {
                    each.Line("c();");
                }

                ifScope.Line("d();");
            }

            outer.Line("e();");
        });

        await Assert
            .That(output)
            .Contains(
                "        a();\n"
                    + "        if (x)\n"
                    + "        {\n"
                    + "            b();\n"
                    + "            foreach (var i in is)\n"
                    + "            {\n"
                    + "                c();\n"
                    + "            }\n"
                    + "            d();\n"
                    + "        }\n"
                    + "        e();\n"
            );
    }

    // The scenario runs once with the chaining call and once without; a rejected call must leave identical output.
    private static async Task AssertChainRejectedWithoutWriting(
        Func<BodyScope, bool, bool> scenario
    )
    {
        bool threw = false;
        string attempted = Render(outer => threw = scenario(outer, true));
        string baseline = Render(outer => scenario(outer, false));

        await Assert.That(threw).IsTrue();
        await Assert.That(attempted).IsEqualTo(baseline);
    }

    [Test]
    public async Task ElseIf_while_nested_if_is_open_throws_and_writes_nothing()
    {
        await AssertChainRejectedWithoutWriting(
            (outer, attempt) =>
            {
                using var branch = outer.If("a");
                using var nested = branch.If("b");

                if (attempt)
                {
                    try
                    {
                        branch.ElseIf("c").Dispose();
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                }

                return false;
            }
        );
    }

    [Test]
    public async Task Else_while_nested_if_is_open_throws_and_writes_nothing()
    {
        await AssertChainRejectedWithoutWriting(
            (outer, attempt) =>
            {
                using var branch = outer.If("a");
                using var nested = branch.If("b");

                if (attempt)
                {
                    try
                    {
                        branch.Else().Dispose();
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                }

                return false;
            }
        );
    }

    [Test]
    public async Task Catch_while_nested_if_is_open_throws_and_writes_nothing()
    {
        await AssertChainRejectedWithoutWriting(
            (outer, attempt) =>
            {
                using var block = outer.Try();
                var nested = block.If("b");
                bool threw = false;

                if (attempt)
                {
                    try
                    {
                        block.Catch().Dispose();
                    }
                    catch (InvalidOperationException)
                    {
                        threw = true;
                    }
                }

                // A bare try would record an error that makes Render throw.
                nested.Dispose();
                block.Finally().Dispose();

                return threw;
            }
        );
    }

    [Test]
    public async Task Finally_while_nested_if_is_open_throws_and_writes_nothing()
    {
        await AssertChainRejectedWithoutWriting(
            (outer, attempt) =>
            {
                using var block = outer.Try();
                var nested = block.If("b");
                bool threw = false;

                if (attempt)
                {
                    try
                    {
                        block.Finally().Dispose();
                    }
                    catch (InvalidOperationException)
                    {
                        threw = true;
                    }
                }

                // A bare try would record an error that makes Render throw.
                nested.Dispose();
                block.Finally().Dispose();

                return threw;
            }
        );
    }

    [Test]
    public async Task Case_while_nested_if_is_open_throws_and_writes_nothing()
    {
        await AssertChainRejectedWithoutWriting(
            (outer, attempt) =>
            {
                using var sw = outer.Switch("x");
                using var section = sw.Case("1");
                using var nested = section.If("b");
                bool threw = false;

                if (attempt)
                {
                    try
                    {
                        sw.Case("2").Dispose();
                    }
                    catch (InvalidOperationException)
                    {
                        threw = true;
                    }
                }

                nested.Dispose();
                section.Break();

                return threw;
            }
        );
    }

    [Test]
    public async Task Default_while_nested_if_is_open_throws_and_writes_nothing()
    {
        await AssertChainRejectedWithoutWriting(
            (outer, attempt) =>
            {
                using var sw = outer.Switch("x");
                using var section = sw.Case("1");
                using var nested = section.If("b");
                bool threw = false;

                if (attempt)
                {
                    try
                    {
                        sw.Default().Dispose();
                    }
                    catch (InvalidOperationException)
                    {
                        threw = true;
                    }
                }

                nested.Dispose();
                section.Break();

                return threw;
            }
        );
    }

    private const string PropertySource =
        "public partial class Sample { public int P { get; set; } }";

    private static PropertyModel P()
    {
        INamedTypeSymbol type = CompilationHelper.GetNamedTypeSymbol(PropertySource, "Sample");

        return PropertyModel.From(type.GetMembers("P").OfType<IPropertySymbol>().Single());
    }

    [Test]
    public async Task Member_on_type_while_method_body_is_open_throws()
    {
        await Assert
            .That(() =>
            {
                SourceWriter writer = new();
                using var file = writer.File();
                using var type = file.Type(Sample());
                using var body = type.Method(Other());
                type.Method(Other());
            })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Type_on_namespace_while_another_type_is_open_throws()
    {
        await Assert
            .That(() =>
            {
                SourceWriter writer = new();
                using var file = writer.File();
                using var ns = file.Namespace("N");
                using var first = ns.Type(Sample());
                ns.Type(Sample());
            })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Using_on_file_while_namespace_is_open_throws()
    {
        await Assert
            .That(() =>
            {
                SourceWriter writer = new();
                using var file = writer.File();
                using var ns = file.Namespace("N");
                file.Using("System");
            })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Accessor_on_property_while_another_accessor_body_is_open_throws()
    {
        await Assert
            .That(() =>
            {
                SourceWriter writer = new();
                using var file = writer.File();
                using var type = file.Type(Sample());
                using var property = type.Property(P());
                using var get = property.Get();
                property.Set();
            })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Disposing_a_copied_type_scope_twice_closes_it_once()
    {
        static string Output(bool copyAndDisposeTwice)
        {
            SourceWriter writer = new();
            using var file = writer.File();
            file.Namespace("N").Dispose();
            var ns = file.Namespace("M");
            var type = ns.Type(Sample());
            var copy = type;
            type.Dispose();

            if (copyAndDisposeTwice)
            {
                copy.Dispose();
            }

            ns.Dispose();

            return writer.ToString();
        }

        await Assert.That(Output(true)).IsEqualTo(Output(false));
    }

    [Test]
    public async Task Bare_try_disposed_does_not_throw_but_ToString_and_ToSourceText_do()
    {
        SourceWriter writer = new();

        using (var file = writer.File())
        using (var type = file.Type(Sample()))
        using (var method = type.Method(Other()))
        {
            method.Try().Dispose();
        }

        InvalidOperationException? fromToString = null;
        InvalidOperationException? fromSourceText = null;

        try
        {
            writer.ToString();
        }
        catch (InvalidOperationException ex)
        {
            fromToString = ex;
        }

        try
        {
            writer.ToSourceText();
        }
        catch (InvalidOperationException ex)
        {
            fromSourceText = ex;
        }

        await Assert.That(fromToString).IsNotNull();
        await Assert.That(fromToString!.Message).Contains("try");
        await Assert.That(fromSourceText).IsNotNull();
    }

    [Test]
    public async Task Try_with_a_catch_or_a_finally_renders_without_error()
    {
        string withCatch = Render(outer =>
        {
            using var t = outer.Try();
            using var c = t.Catch();
        });
        string withFinally = Render(outer =>
        {
            using var t = outer.Try();
            using var f = t.Finally();
        });

        await Assert.That(withCatch).Contains("catch");
        await Assert.That(withFinally).Contains("finally");
    }

    [Test]
    public async Task Duplicate_catch_type_throws()
    {
        await Assert
            .That(() =>
                Render(outer =>
                {
                    using var t = outer.Try();
                    using var first = t.Catch("IOException");
                    first.Catch("IOException");
                })
            )
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Duplicate_catch_type_after_a_filtered_catch_is_legal()
    {
        string output = Render(outer =>
        {
            using var t = outer.Try();
            using var first = t.Catch("IOException", filter: "ex.HResult == 1");
            using var second = first.Catch("IOException");
        });

        await Assert.That(output).Contains("catch (IOException) when (ex.HResult == 1)");
    }

    [Test]
    public async Task Filtered_catch_repeating_an_earlier_unfiltered_type_throws()
    {
        await Assert
            .That(() =>
                Render(outer =>
                {
                    using var t = outer.Try();
                    using var first = t.Catch("IOException");
                    first.Catch("IOException", filter: "x");
                })
            )
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Filtered_catch_repeating_an_earlier_filtered_type_is_legal()
    {
        string output = Render(outer =>
        {
            using var t = outer.Try();
            using var first = t.Catch("IOException", filter: "a");
            using var second = first.Catch("IOException", filter: "b");
        });

        await Assert.That(output).Contains("when (b)");
    }

    [Test]
    [Arguments("if")]
    [Arguments("else if")]
    [Arguments("try")]
    [Arguments("catch")]
    public async Task Branch_write_while_a_nested_block_is_open_throws_and_writes_nothing(
        string kind
    )
    {
        SourceWriter writer = new();
        string before = null!;

        await Assert
            .That(() =>
            {
                using var file = writer.File();
                using var type = file.Type(Sample());
                using var method = type.Method(Other());

                if (kind == "if")
                {
                    using var branch = method.If("a");
                    using var nested = branch.Block("lock (o)");
                    before = writer.ToString();
                    branch.Line("y();");
                }
                else if (kind == "else if")
                {
                    using var first = method.If("a");
                    using var branch = first.ElseIf("b");
                    using var nested = branch.Block("lock (o)");
                    before = writer.ToString();
                    branch.Line("y();");
                }
                else if (kind == "try")
                {
                    using var branch = method.Try();
                    var nested = branch.Block("lock (o)");
                    before = writer.ToString();

                    try
                    {
                        branch.Line("y();");
                    }
                    catch (InvalidOperationException)
                    {
                        // A bare try records an error on dispose; closing it properly keeps this test on its own guard.
                        nested.Dispose();
                        using var final = branch.Finally();

                        throw;
                    }
                }
                else
                {
                    using var t = method.Try();
                    using var branch = t.Catch();
                    using var nested = branch.Block("lock (o)");
                    before = writer.ToString();
                    branch.Line("y();");
                }
            })
            .Throws<InvalidOperationException>();

        await Assert.That(before).EndsWith("lock (o)\n            {\n");
        await Assert.That(writer.ToString()).DoesNotContain("y();");
    }

    [Test]
    public async Task Disposing_a_type_scope_with_a_method_body_open_closes_it_and_records_no_error()
    {
        SourceWriter writer = new();

        var file = writer.File();
        var type = file.Type(Sample());
        type.Method(Other());
        type.Dispose();
        file.Dispose();

        string expected =
            "// <auto-generated/>\n#nullable enable\n\npartial class Sample\n{\n    public void Other()\n    {\n    }\n}\n";

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task Disposing_a_namespace_scope_with_a_type_and_method_body_open_closes_them_and_records_no_error()
    {
        SourceWriter writer = new();

        var file = writer.File();
        var ns = file.Namespace("A");
        var type = ns.Type(Sample());
        type.Method(Other());
        ns.Dispose();
        file.Dispose();

        string expected =
            "// <auto-generated/>\n#nullable enable\n\nnamespace A\n{\n    partial class Sample\n    {\n        public void Other()\n        {\n        }\n    }\n}\n";

        await Assert.That(writer.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Exception")]
    [Arguments("System.Exception")]
    [Arguments("global::System.Exception")]
    public async Task Exception_spellings_count_as_the_general_catch(string spelling)
    {
        await Assert
            .That(() =>
                Render(outer =>
                {
                    using var t = outer.Try();
                    using var general = t.Catch(spelling);
                    general.Catch("IOException");
                })
            )
            .Throws<InvalidOperationException>();
    }

    // The stale copy's block is gone, so its guards must not mistake a sibling at the same depth for it.
    [Test]
    public async Task Writing_through_a_stale_body_copy_after_a_sibling_opened_throws()
    {
        string? message = null;

        try
        {
            Render(outer =>
            {
                BodyScope first = outer.Block("lock (a)");
                BodyScope stale = first;
                first.Dispose();

                using BodyScope sibling = outer.Block("lock (b)");
                stale.Line("z();");
            });
        }
        catch (InvalidOperationException ex)
        {
            message = ex.Message;
        }

        await Assert.That(message).IsNotNull();
        await Assert.That(message!).Contains("closed");
    }

    [Test]
    public async Task Writing_through_a_stale_if_copy_after_a_sibling_opened_throws()
    {
        string? message = null;

        try
        {
            Render(outer =>
            {
                IfScope first = outer.If("a");
                IfScope stale = first;
                first.Dispose();

                using BodyScope sibling = outer.Block("lock (b)");
                stale.Line("z();");
            });
        }
        catch (InvalidOperationException ex)
        {
            message = ex.Message;
        }

        await Assert.That(message).IsNotNull();
        await Assert.That(message!).Contains("closed");
    }

    [Test]
    public async Task Writing_through_a_stale_try_copy_after_a_sibling_opened_throws()
    {
        string? message = null;

        try
        {
            Render(outer =>
            {
                TryScope first = outer.Try();
                TryScope stale = first;
                first.Finally().Dispose();

                using BodyScope sibling = outer.Block("lock (b)");
                stale.Line("z();");
            });
        }
        catch (InvalidOperationException ex)
        {
            message = ex.Message;
        }

        await Assert.That(message).IsNotNull();
        await Assert.That(message!).Contains("closed");
    }

    [Test]
    public async Task Adding_a_case_through_a_stale_switch_copy_after_a_sibling_opened_throws()
    {
        string? message = null;

        try
        {
            Render(outer =>
            {
                SwitchScope first = outer.Switch("x");
                first.Case("1").Break();
                SwitchScope stale = first;
                first.Dispose();

                using BodyScope sibling = outer.Block("lock (b)");
                stale.Case("2");
            });
        }
        catch (InvalidOperationException ex)
        {
            message = ex.Message;
        }

        await Assert.That(message).IsNotNull();
        await Assert.That(message!).Contains("closed");
    }

    [Test]
    public async Task Writing_through_a_stale_type_copy_after_a_sibling_opened_throws()
    {
        string? message = null;
        SourceWriter writer = new();

        try
        {
            using var file = writer.File();
            TypeScope first = file.Type(Sample());
            TypeScope stale = first;
            first.Dispose();

            using TypeScope sibling = file.Type(Sample());
            stale.Method(Other());
        }
        catch (InvalidOperationException ex)
        {
            message = ex.Message;
        }

        await Assert.That(message).IsNotNull();
        await Assert.That(message!).Contains("closed");
    }
}
