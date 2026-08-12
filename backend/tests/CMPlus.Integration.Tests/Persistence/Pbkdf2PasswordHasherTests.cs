using CMPlus.Infrastructure.Auth;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// Direct tests of the real <see cref="Pbkdf2PasswordHasher"/> (elsewhere it is only exercised
/// indirectly, via the seeded login in the WebApi controller tests). Covers the core round-trip and
/// the <c>VerifyDummy</c> timing-equalizer added for sprint-15-owasp.md L-01 — proving the real
/// implementation honours the contract, not just the handler-test fake.
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    private static readonly Pbkdf2PasswordHasher Hasher = new();

    [Fact]
    public void Hash_Then_Verify_Round_Trips_For_The_Correct_Password_And_Rejects_A_Wrong_One()
    {
        var hash = Hasher.Hash("Correct-Horse-Battery-Staple-1");

        Assert.True(Hasher.Verify(hash, "Correct-Horse-Battery-Staple-1"));
        Assert.False(Hasher.Verify(hash, "wrong-password"));
    }

    [Fact]
    public void Hash_Produces_A_Distinct_Salt_Per_Call_So_Two_Hashes_Of_The_Same_Password_Differ()
    {
        // Each Hash uses a fresh random salt, so identical passwords must not produce identical hashes
        // (defeats rainbow tables / reveals-equal-passwords). Both must still verify.
        var a = Hasher.Hash("same-password");
        var b = Hasher.Hash("same-password");

        Assert.NotEqual(a, b);
        Assert.True(Hasher.Verify(a, "same-password"));
        Assert.True(Hasher.Verify(b, "same-password"));
    }

    [Theory]
    [InlineData("some-attempted-password")]
    [InlineData("")]
    public void VerifyDummy_Always_Returns_False_And_Never_Throws(string password)
    {
        // Its whole purpose is to spend Verify's cost on the unknown-email login path and fail; it must
        // never authenticate anything and must be exception-safe for any input (including empty, which
        // Verify short-circuits — VerifyDummy mirrors that).
        Assert.False(Hasher.VerifyDummy(password));
    }

    [Fact]
    public void Verify_Returns_False_For_A_Malformed_Hash_Rather_Than_Throwing()
    {
        Assert.False(Hasher.Verify("not-a-valid-hash-format", "anything"));
        Assert.False(Hasher.Verify("v1.notanumber.salt.key", "anything"));
    }
}
