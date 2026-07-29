namespace CMPlus.Infrastructure.Auth;

/// <summary>
/// Bound from the <c>Jwt</c> configuration section (env var form <c>Jwt__SigningKey</c> etc,
/// docs/security/secrets-policy.md). <see cref="SigningKey"/> is the one field that must never
/// have a checked-in default - <see cref="DependencyInjection.AddInfrastructure"/> registers this
/// with <c>ValidateOnStart()</c> (S2-SEC-01 finding M-02), so a missing or too-short key throws
/// at host startup, not silently falling back to a guessable value or surfacing only on the
/// first authenticated request.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "CMPlus";

    public string Audience { get; set; } = "CMPlusClients";

    public int ExpiryMinutes { get; set; } = 60;
}
