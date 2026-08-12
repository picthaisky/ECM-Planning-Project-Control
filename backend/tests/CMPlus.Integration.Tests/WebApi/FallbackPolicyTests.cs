using System.Net;
using System.Net.Http.Json;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// Security review sprint-15.md L-05: proves <c>AddAuthorization</c>'s <c>FallbackPolicy</c>
/// (<c>RequireAuthenticatedUser()</c>, <c>Program.cs</c>) actually applies to an endpoint that
/// carries no authorization metadata of its own - the exact "a future controller forgets
/// [Authorize]" scenario the finding describes - while the two routes that must stay reachable
/// without a token (<c>/auth/login</c>, <c>/health/*</c>) remain unaffected.
/// </summary>
public class FallbackPolicyTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string UnprotectedTestOnlyRoute = "/__test-only/no-explicit-authorization-metadata";

    private readonly CustomWebApplicationFactory _factory;

    public FallbackPolicyTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    /// <summary>Registers, via an <see cref="IStartupFilter"/> (the standard supported way to add
    /// endpoints from a test without touching production <c>Program.cs</c>), one minimal-API GET
    /// with zero <c>[Authorize]</c>/<c>[AllowAnonymous]</c> metadata of its own - simulating a
    /// future controller action nobody remembered to annotate.</summary>
    private sealed class AddUnprotectedTestEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseEndpoints(endpoints =>
                endpoints.MapGet(UnprotectedTestOnlyRoute, () => Results.Ok("reachable-if-fallback-policy-is-missing")));
        };
    }

    [Fact]
    public async Task An_Endpoint_With_No_Authorization_Metadata_Of_Its_Own_Requires_Authentication_Via_The_FallbackPolicy()
    {
        using var client = _factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                    services.AddSingleton<IStartupFilter, AddUnprotectedTestEndpointStartupFilter>()))
            .CreateClient();

        var response = await client.GetAsync(UnprotectedTestOnlyRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Remains_Reachable_Anonymously_Despite_The_FallbackPolicy()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "admin@siam-construction.dev",
            Password = DevDataSeeder.DevSeedPassword,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_Endpoints_Remain_Reachable_Anonymously_Despite_The_FallbackPolicy(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
