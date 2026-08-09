using System.Security.Claims;
using CMPlus.Application.Abstractions;
using CMPlus.Domain.Enums;

namespace CMPlus.WebApi.Auth;

/// <summary>The real, JWT-backed <see cref="ICurrentUserContext"/> (S2-BE-02). <c>null</c> for an
/// unauthenticated request - a legitimate value, not an error, unlike
/// <see cref="HttpContextTenantProvider"/> (audit rows may need a "no actor" case; tenant-scoped
/// reads/writes never do).</summary>
public sealed class HttpContextCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public Guid? UserId
    {
        get
        {
            var claimValue = httpContextAccessor.HttpContext?.User.FindFirst(JwtClaimTypes.UserId)?.Value;
            return Guid.TryParse(claimValue, out var userId) ? userId : null;
        }
    }

    /// <summary>S9-BE-05: reads the standard <see cref="ClaimTypes.Role"/> claim <c>JwtTokenService</c>
    /// always embeds (<c>JwtClaimTypes</c>'s "tenantId/userId/role always present together").
    /// Throws on an unauthenticated/malformed-role request rather than silently defaulting to some
    /// role - every caller of this member is already behind <c>[Authorize]</c>, so this should never
    /// actually fire in production; a controller that reaches here without authentication is itself
    /// a bug worth failing loudly on rather than masking with a fabricated role.</summary>
    public UserRole Role
    {
        get
        {
            var claimValue = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role)?.Value;
            if (claimValue is null || !Enum.TryParse<UserRole>(claimValue, out var role))
            {
                throw new InvalidOperationException(
                    "The current request has no valid role claim - ICurrentUserContext.Role must only be read from an authenticated ([Authorize]) request.");
            }

            return role;
        }
    }
}
