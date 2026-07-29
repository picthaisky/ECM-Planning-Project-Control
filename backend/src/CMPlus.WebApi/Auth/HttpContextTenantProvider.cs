using CMPlus.Application.Abstractions;

namespace CMPlus.WebApi.Auth;

/// <summary>
/// The real, JWT-backed <see cref="ITenantProvider"/> (S2-BE-01), replacing the design-time/seed
/// stand-ins used before Sprint 2. Reads <see cref="JwtClaimTypes.TenantId"/> from the
/// authenticated user's claims **only** - a <c>tenantId</c> arriving in the request route, query
/// string, or body is never consulted here, regardless of what a controller action parameter is
/// named (ADR-0002). Throws if called for an unauthenticated request; every controller that
/// reaches a handler needing <see cref="TenantId"/> is behind <c>[Authorize]</c>, so this should
/// never happen in practice - failing loudly here is preferable to silently resolving
/// <see cref="Guid.Empty"/> and leaking tenant-less data.
/// </summary>
public sealed class HttpContextTenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider
{
    public Guid TenantId
    {
        get
        {
            var claimValue = httpContextAccessor.HttpContext?.User.FindFirst(JwtClaimTypes.TenantId)?.Value;

            if (!Guid.TryParse(claimValue, out var tenantId))
            {
                throw new InvalidOperationException(
                    "No authenticated tenantId claim is present on the current request. " +
                    "TenantId is only ever resolved from the JWT, never from route/query/body input.");
            }

            return tenantId;
        }
    }
}
