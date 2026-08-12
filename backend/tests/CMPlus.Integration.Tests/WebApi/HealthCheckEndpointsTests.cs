using System.Net;
using CMPlus.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S16 deploy-prep / S15-DO-02 gap closure: <c>/health/ready</c> must actually probe the database, so
/// an orchestrator's ingress can tell "API up" from "API up but DB unreachable"; <c>/health/live</c>
/// must NOT depend on the database, or a transient DB outage would get a healthy instance killed.
/// Both endpoints are <c>AllowAnonymous</c>, so no login is needed. The reachable path runs against the
/// factory's real InMemory context (whose <c>CanConnectAsync</c> is true); the unreachable → 503 path
/// substitutes a fake <see cref="IDatabaseConnectivityProbe"/> via <c>WithWebHostBuilder</c> (never
/// touching the shared factory), which is the only way to exercise DB-down without a real database.
/// </summary>
public class HealthCheckEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HealthCheckEndpointsTests(CustomWebApplicationFactory factory) => _factory = factory;

    private sealed class FakeProbe(bool canConnect) : IDatabaseConnectivityProbe
    {
        public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) => Task.FromResult(canConnect);
    }

    private WebApplicationFactory<Program> WithProbe(bool canConnect) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDatabaseConnectivityProbe>();
                services.AddScoped<IDatabaseConnectivityProbe>(_ => new FakeProbe(canConnect));
            }));

    [Fact]
    public async Task Ready_Returns_200_When_The_Database_Is_Reachable()
    {
        using var client = _factory.CreateClient(); // real InMemory context → CanConnectAsync is true

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_Returns_503_When_The_Database_Is_Not_Reachable()
    {
        using var factory = WithProbe(canConnect: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        // This is the whole point of the check: an unreachable DB must make readiness fail so traffic
        // is not routed to this instance. Without the DatabaseHealthCheck this returned 200.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Live_Returns_200_Even_When_The_Database_Is_Not_Reachable()
    {
        // Liveness must not depend on the database — the DB check is untagged and /health/live runs no
        // checks (Predicate = _ => false), so a DB outage must never turn liveness red.
        using var factory = WithProbe(canConnect: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
