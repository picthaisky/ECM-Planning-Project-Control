using CMPlus.Application.Abstractions;

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
}
