using System.Net;
using System.Net.Http.Json;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// sprint-15-owasp.md M-1: proves the login rate limiter actually returns 429 (with a
/// <c>Retry-After</c> header and an <c>application/problem+json</c> body) once the per-IP window is
/// exhausted, and — critically — that it counts <b>failed</b> attempts, so it throttles the exact
/// online-brute-force pattern it exists to stop.
///
/// <para>The shared <see cref="CustomWebApplicationFactory"/> disables the limiter (every other test
/// class logs in far more than the per-IP window would allow from one loopback IP). It is
/// <c>sealed</c>, so this test does not subclass it — it uses <c>WithWebHostBuilder</c> to append a
/// config source that re-enables the limiter and shrinks the per-IP permit to 3, without mutating the
/// shared factory. Proving the limiter <i>on</i> here, rather than merely assuming it off elsewhere,
/// is the same "don't verify a guard by its absence" discipline the idempotency/append-only tests
/// follow.</para>
/// </summary>
public class LoginRateLimiterTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LoginRateLimiterTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.SeedAsync().GetAwaiter().GetResult();
    }

    // WithWebHostBuilder appends after the base factory's config (which set Enabled=false), and later
    // in-memory sources win — so the limiter comes back on for clients created from this factory,
    // while the shared factory the other 570 tests use is untouched. The same _databaseName-keyed
    // InMemory store is reused, so the users seeded above are visible here.
    private WebApplicationFactory<Program> RateLimitEnabled() =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Login:Enabled"] = "true",
                    ["RateLimiting:Login:PermitLimitPerIp"] = "3",
                    ["RateLimiting:Login:WindowSecondsPerIp"] = "60",
                    ["RateLimiting:Login:PermitLimitPerAccount"] = "50",
                })));

    [Fact]
    public async Task Repeated_Failed_Logins_From_One_Ip_Are_Throttled_With_A_429_And_Retry_After()
    {
        using var factory = RateLimitEnabled();
        using var client = factory.CreateClient();

        // PermitLimitPerIp = 3. The first three attempts pass the limiter and are answered on their own
        // merits (401, wrong password); the fourth is rejected by the limiter before the handler runs.
        HttpResponseMessage? throttled = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
            {
                Email = "admin@siam-construction.dev",
                Password = "wrong-password-so-the-attempt-fails",
            });

            if (attempt <= 3)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); // counted, but still handled
            }
            else
            {
                throttled = response;
            }
        }

        Assert.NotNull(throttled);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled!.StatusCode); // 429
        Assert.True(throttled.Headers.Contains("Retry-After"), "429 must carry a Retry-After header.");
        Assert.Equal("application/problem+json", throttled.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_Valid_Login_Within_The_Window_Still_Succeeds()
    {
        // The limiter must not break the happy path: a single correct login is well within the window.
        using var factory = RateLimitEnabled();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            Email = "qs@bkk-infra.dev",
            Password = DevDataSeeder.DevSeedPassword,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
