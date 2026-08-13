using System.Net;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// sprint-10.md L-06 / sprint-16.md S16-SEC-01: the global <c>SecurityHeadersMiddleware</c> sets the
/// baseline security headers on <b>every</b> response. Verified end-to-end against the real middleware
/// pipeline via the anonymous <c>/health/live</c> endpoint (needs no auth or seed). This full-HTTP
/// assertion was impossible before the middleware existed - <c>ProjectPhotosControllerHeaderTests</c>
/// records that until now "this API sets no global header middleware yet, so <c>Headers</c> starts
/// empty on every real request"; that this suite now passes end-to-end is itself the evidence the
/// middleware is wired and effective.
/// </summary>
public class SecurityHeadersTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SecurityHeadersTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "no-referrer")]
    [InlineData("Cross-Origin-Resource-Policy", "same-origin")]
    [InlineData("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'")]
    public async Task Every_Response_Carries_The_Baseline_Security_Header(string name, string expected)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // A custom (non-content) header lands on the message headers; check content headers too so the
        // assertion never mis-fires on HttpClient's header-splitting rules.
        var found = response.Headers.TryGetValues(name, out var values)
            || response.Content.Headers.TryGetValues(name, out values);
        Assert.True(found, $"Response is missing the {name} security header.");
        Assert.Equal(expected, Assert.Single(values!));
    }

    [Fact]
    public async Task NoSniff_Appears_Exactly_Once_Never_Doubled()
    {
        // The indexer-vs-Append property ProjectPhotosControllerHeaderTests proves at the unit level,
        // asserted here end-to-end: exactly one value, never "nosniff,nosniff".
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        var found = response.Headers.TryGetValues("X-Content-Type-Options", out var values)
            || response.Content.Headers.TryGetValues("X-Content-Type-Options", out values);
        Assert.True(found, "Response is missing the X-Content-Type-Options header.");
        Assert.Equal("nosniff", Assert.Single(values!));
    }
}
