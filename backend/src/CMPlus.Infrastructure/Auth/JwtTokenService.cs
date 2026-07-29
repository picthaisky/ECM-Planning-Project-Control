using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CMPlus.Application.Abstractions;
using CMPlus.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CMPlus.Infrastructure.Auth;

/// <summary>
/// <see cref="IJwtTokenService"/> implementation (S2-BE-01). Always embeds all three mandatory
/// claims - <see cref="JwtClaimTypes.TenantId"/>, <see cref="JwtClaimTypes.UserId"/>,
/// <see cref="ClaimTypes.Role"/> - never fewer. The signing key comes exclusively from
/// <see cref="JwtOptions"/> (bound from configuration/environment) - never hardcoded
/// (docs/security/secrets-policy.md).
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider clock) : IJwtTokenService
{
    public JwtToken GenerateToken(Guid tenantId, Guid userId, UserRole role)
    {
        var jwtOptions = options.Value;
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(jwtOptions.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtClaimTypes.TenantId, tenantId.ToString()),
            new Claim(JwtClaimTypes.UserId, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Issuer,
            audience: jwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new JwtToken(accessToken, expiresAt);
    }
}
