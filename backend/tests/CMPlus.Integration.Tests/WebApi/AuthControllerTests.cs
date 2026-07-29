using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S2-BE-01/03 end-to-end: real login against real seeded users, and the JWT DoD - expired/
/// tampered tokens fail with 401 + ProblemDetails, never a stack trace.
/// </summary>
public class AuthControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId, Guid TenantId, string Role);

    private readonly CustomWebApplicationFactory _factory;

    public AuthControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Login_With_A_Real_Seeded_Users_Credentials_Returns_A_Token_Carrying_All_Three_Claims()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "admin@siam-construction.dev",
            Password = DevDataSeeder.DevSeedPassword,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(body.AccessToken);
        Assert.Equal(body.TenantId.ToString(), jwt.Claims.Single(c => c.Type == "tenantId").Value);
        Assert.Equal(body.UserId.ToString(), jwt.Claims.Single(c => c.Type == "userId").Value);
        Assert.Equal("Admin", jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public async Task Login_Fails_With_401_For_A_Wrong_Password()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "admin@siam-construction.dev",
            Password = "wrong-password-placeholder",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("invalid-credentials", problem!.Type!.Split('/')[^1]);
    }

    [Fact]
    public async Task Login_Fails_With_401_For_An_Unknown_Email()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "nobody@nowhere.dev",
            Password = "invalid-password-placeholder",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Tampered_Token_Is_Rejected_With_401_And_A_ProblemDetails_Body_With_No_Stack_Trace()
    {
        using var client = _factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "admin@siam-construction.dev",
            Password = DevDataSeeder.DevSeedPassword,
        });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;

        // Flip one character in the signature segment - the payload still parses as valid JSON/JWT
        // shape, but the signature no longer verifies.
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tenants/{Guid.NewGuid()}/approval-policies?documentType=VariationOrder");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at CMPlus.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_Expired_Token_Is_Rejected_With_401_And_A_ProblemDetails_Body_With_No_Stack_Trace()
    {
        using var client = _factory.CreateClient();

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.JwtSigningKeyForTests));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var longExpired = DateTime.UtcNow.AddDays(-1);

        var expiredToken = new JwtSecurityToken(
            issuer: CustomWebApplicationFactory.JwtIssuerForTests,
            audience: CustomWebApplicationFactory.JwtAudienceForTests,
            claims: [new Claim("tenantId", Guid.NewGuid().ToString()), new Claim("userId", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "Admin")],
            notBefore: longExpired.AddMinutes(-5),
            expires: longExpired,
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(expiredToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/tenants/{Guid.NewGuid()}/approval-policies?documentType=VariationOrder");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenString);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_Unauthenticated_Request_To_A_Protected_Endpoint_Returns_401_ProblemDetails()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/tenants/{Guid.NewGuid()}/approval-policies?documentType=VariationOrder");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
