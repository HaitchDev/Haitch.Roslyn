using Haitch.Roslyn.Testing;

namespace Haitch.Roslyn.Tests.Testing;

public class GeneratorTestExceptionTests
{
    [Test]
    public async Task Carries_its_message()
    {
        var exception = new GeneratorTestException("boom");

        await Assert.That(exception.Message).IsEqualTo("boom");
    }
}
