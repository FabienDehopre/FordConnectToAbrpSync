using FordConnectToAbrpSync.Security;

namespace FordConnectToAbrpSync.Tests;

public class SignalAuthenticatorTests
{
    private const string Secret = "s3cret-value";

    [Test]
    public async Task IsAuthorized_CorrectBearerToken_True()
    {
        await Assert.That(SignalAuthenticator.IsAuthorized(Secret, $"Bearer {Secret}")).IsTrue();
    }

    [Test]
    public async Task IsAuthorized_WrongToken_False()
    {
        await Assert.That(SignalAuthenticator.IsAuthorized(Secret, "Bearer nope")).IsFalse();
    }

    [Test]
    public async Task IsAuthorized_MissingBearerPrefix_False()
    {
        await Assert.That(SignalAuthenticator.IsAuthorized(Secret, Secret)).IsFalse();
    }

    [Test]
    public async Task IsAuthorized_NoHeader_False()
    {
        await Assert.That(SignalAuthenticator.IsAuthorized(Secret, null)).IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task IsAuthorized_NoConfiguredSecret_AlwaysFalse(string? configured)
    {
        await Assert.That(SignalAuthenticator.IsAuthorized(configured, $"Bearer {Secret}")).IsFalse();
    }

    [Test]
    public async Task IsAuthorized_TokenIsPrefixOfSecret_False()
    {
        await Assert.That(SignalAuthenticator.IsAuthorized(Secret, "Bearer s3cret")).IsFalse();
    }
}
