using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Haitch.Roslyn.Testing;

/// <summary>An additional file the compiler cannot read: <see cref="GetText"/> returns <see langword="null"/>.</summary>
/// <param name="path">The path the generator sees; also the key for <see cref="GeneratorHarnessInput.PerFileOptions"/>.</param>
public sealed class UnreadableAdditionalText(string path) : AdditionalText
{
    /// <summary>The path the generator sees; also the key for <see cref="GeneratorHarnessInput.PerFileOptions"/>.</summary>
    public override string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <inheritdoc />
    public override SourceText? GetText(CancellationToken cancellationToken = default) => null;
}
