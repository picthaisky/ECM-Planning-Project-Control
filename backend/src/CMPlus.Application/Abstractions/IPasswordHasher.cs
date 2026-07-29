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
}
