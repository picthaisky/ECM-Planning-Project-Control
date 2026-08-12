namespace CMPlus.Application.Abstractions;

/// <summary>
/// Password hashing abstraction for <c>S2-BE-01</c>. Infrastructure provides the real
/// implementation (a salted, iterated PBKDF2 hash - see
/// <c>CMPlus.Infrastructure.Auth.Pbkdf2PasswordHasher</c>); nothing in Application/Domain ever
/// sees a plaintext password beyond the single call into <see cref="Verify"/>.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Produces a self-describing hash string (algorithm/iteration/salt/hash all
    /// encoded together) suitable for storing in <c>User.PasswordHash</c>.</summary>
    string Hash(string password);

    /// <summary>Verifies <paramref name="password"/> against a previously-produced
    /// <paramref name="hash"/>. Never throws for a malformed/foreign hash - returns false.</summary>
    bool Verify(string hash, string password);

    /// <summary>
    /// Spends the same hashing cost as a real <see cref="Verify"/> and always returns <c>false</c>.
    /// Login calls this on the unknown-email path so the response takes the same time whether or not
    /// the email is registered — closing the timing side channel that would otherwise let login be
    /// used to enumerate accounts (sprint-15-owasp.md L-01). The equal-work guarantee is the
    /// implementation's responsibility (it verifies against a fixed dummy hash at the real iteration
    /// count), which is why this lives on the abstraction rather than being faked with a constant in
    /// the handler.
    /// </summary>
    bool VerifyDummy(string password);
}
