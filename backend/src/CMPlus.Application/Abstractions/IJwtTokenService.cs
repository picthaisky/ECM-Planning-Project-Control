using CMPlus.Domain.Enums;

namespace CMPlus.Application.Abstractions;

/// <summary>Issued JWT plus its expiry, so callers never have to re-derive the expiry from
/// configuration (S2-BE-01).</summary>
public sealed record JwtToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Issues signed JWTs carrying the three mandatory claims - <c>tenantId</c>, <c>userId</c>,
/// <c>role</c> - always together (S2-BE-01 DoD). The signing key is read from configuration/
/// environment only by the Infrastructure implementation; it is never hardcoded
/// (docs/security/secrets-policy.md).
/// </summary>
public interface IJwtTokenService
{
    JwtToken GenerateToken(Guid tenantId, Guid userId, UserRole role);
}
