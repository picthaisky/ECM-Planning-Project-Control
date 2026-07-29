namespace CMPlus.Application.Abstractions;

/// <summary>
/// Custom claim type names shared between token issuance (Infrastructure's
/// <c>JwtTokenService</c>) and token consumption (WebApi's <c>HttpContextTenantProvider</c>/
/// <c>HttpContextCurrentUserContext</c>), so the two sides can never drift apart on the claim
/// name string (S2-BE-01 DoD: tenantId/userId/role are always present together). The role claim
/// itself deliberately uses the BCL's standard <see cref="System.Security.Claims.ClaimTypes.Role"/>
/// (not a custom name) so ASP.NET Core's built-in <c>[Authorize(Roles = "...")]</c> works with no
/// extra configuration.
/// </summary>
public static class JwtClaimTypes
{
    public const string TenantId = "tenantId";

    public const string UserId = "userId";
}
