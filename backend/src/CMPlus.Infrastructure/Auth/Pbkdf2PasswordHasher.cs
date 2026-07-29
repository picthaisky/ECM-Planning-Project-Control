using System.Security.Cryptography;
using CMPlus.Application.Abstractions;

namespace CMPlus.Infrastructure.Auth;

/// <summary>
/// <see cref="IPasswordHasher"/> implementation using the BCL's PBKDF2
/// (<see cref="Rfc2898DeriveBytes"/>, HMAC-SHA256) - deliberately no external Identity package
/// dependency (none is available in this solution's restore sources). Same self-describing
/// "version.iterations.salt.hash" string format ASP.NET Core Identity's own
/// <c>PasswordHasher&lt;TUser&gt;</c> uses conceptually, so it can be swapped in later without a
/// data migration if that package becomes available.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32;
    private const int Iterations = 210_000; // OWASP 2023 minimum recommendation for PBKDF2-HMAC-SHA256
    private const char Delimiter = '.';
    private const string Version = "v1";

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

        return string.Join(Delimiter, Version, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var parts = hash.Split(Delimiter);
        if (parts.Length != 4 || parts[0] != Version || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedKey = Convert.FromBase64String(parts[3]);
            var actualKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedKey.Length);

            return CryptographicOperations.FixedTimeEquals(expectedKey, actualKey);
        }
        catch (FormatException)
        {
            // Malformed/foreign hash (e.g. a hand-edited value) - never throw for bad stored data,
            // just fail verification (IPasswordHasher.Verify contract).
            return false;
        }
    }
}
