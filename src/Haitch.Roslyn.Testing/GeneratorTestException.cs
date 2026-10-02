namespace Haitch.Roslyn.Testing;

/// <summary>The single exception type the testing package throws, so it works with any test framework.</summary>
public sealed class GeneratorTestException(string message) : Exception(message);
