using Haitch.Roslyn.Diagnostics;
using Haitch.Roslyn.Models;
using Haitch.Roslyn.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Haitch.Roslyn.Tests.Diagnostics;

public class PartialTypeValidationTests
{
    // Test-only descriptors; RS2008 (analyzer release tracking) does not apply to fixtures that are never shipped.
#pragma warning disable RS2008
    private static readonly PartialTypeDiagnostics Descriptors = new(
        Create("PT0001"),
        Create("PT0002"),
        Create("PT0003"));
#pragma warning restore RS2008

    private static readonly LocationInfo Location = new("Widget.cs", new TextSpan(10, 6),
        new LinePositionSpan(new LinePosition(1, 4), new LinePosition(1, 10)));

    [Test]
    public async Task Should_succeed_with_the_same_model_for_a_valid_partial_type()
    {
        var model = CreateModel(isFileLocal: false);

        var result = PartialTypeValidation.Validate(model, new SyntaxInfo(true, true, Location), Descriptors);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Match(value => value, _ => null!)).IsEqualTo(model);
        await Assert.That(ReferenceEquals(result.Match(value => value, _ => null!), model)).IsTrue();
    }

    [Test]
    public async Task Should_fail_when_the_type_is_not_partial()
    {
        var result =
            PartialTypeValidation.Validate(CreateModel(false), new SyntaxInfo(false, true, Location), Descriptors);

        await AssertSingle(result, "PT0001");
    }

    [Test]
    public async Task Should_fail_when_a_containing_type_is_not_partial()
    {
        var result =
            PartialTypeValidation.Validate(CreateModel(false), new SyntaxInfo(true, false, Location), Descriptors);

        await AssertSingle(result, "PT0002");
    }

    [Test]
    public async Task Should_fail_when_the_type_is_file_local()
    {
        var result =
            PartialTypeValidation.Validate(CreateModel(true), new SyntaxInfo(true, true, Location), Descriptors);

        await AssertSingle(result, "PT0003");
    }

    [Test]
    public async Task Should_report_every_broken_rule_in_order()
    {
        var result =
            PartialTypeValidation.Validate(CreateModel(true), new SyntaxInfo(false, false, Location), Descriptors);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Diagnostics.Count).IsEqualTo(3);
        await Assert.That(result.Diagnostics[0].Descriptor.Id).IsEqualTo("PT0001");
        await Assert.That(result.Diagnostics[1].Descriptor.Id).IsEqualTo("PT0002");
        await Assert.That(result.Diagnostics[2].Descriptor.Id).IsEqualTo("PT0003");

        foreach (var diagnostic in result.Diagnostics)
        {
            await Assert.That(diagnostic.MessageArgs.Count).IsEqualTo(1);
            await Assert.That(diagnostic.MessageArgs[0]).IsEqualTo("Widget");
            await Assert.That(diagnostic.Location).IsEqualTo(Location);
        }
    }

    [Test]
    public async Task Should_report_a_null_location_when_the_syntax_has_none()
    {
        var result =
            PartialTypeValidation.Validate(CreateModel(true), new SyntaxInfo(false, false, null), Descriptors);

        await Assert.That(result.Diagnostics.Count).IsEqualTo(3);

        foreach (var diagnostic in result.Diagnostics)
        {
            await Assert.That(diagnostic.Location).IsNull();
        }
    }

    private static async Task AssertSingle(Result<TypeModel> result, string id)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Diagnostics.Count).IsEqualTo(1);

        var diagnostic = result.Diagnostics[0];
        await Assert.That(diagnostic.Descriptor.Id).IsEqualTo(id);
        await Assert.That(diagnostic.MessageArgs.Count).IsEqualTo(1);
        await Assert.That(diagnostic.MessageArgs[0]).IsEqualTo("Widget");
        await Assert.That(diagnostic.Location).IsEqualTo(Location);
    }

    private static DiagnosticDescriptor Create(string id)
    {
        return new DiagnosticDescriptor(id, "Title", "Message '{0}'", "Test", DiagnosticSeverity.Error, true);
    }

    private static TypeModel CreateModel(bool isFileLocal)
    {
        return new TypeModel(
            null,
            "Widget",
            TypeDeclarationKind.Class,
            Accessibility.Internal,
            false,
            false,
            false,
            false,
            false,
            isFileLocal,
            default,
            default,
            default,
            default,
            default,
            default);
    }
}
