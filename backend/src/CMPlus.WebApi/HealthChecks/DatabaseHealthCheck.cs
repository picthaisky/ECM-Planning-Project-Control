using CMPlus.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CMPlus.WebApi.HealthChecks;

/// <summary>
/// The <c>/health/ready</c> readiness check (S16 deploy-prep; the gap S15-DO-02's cloud runbook
/// flagged: <c>AddHealthChecks()</c> previously registered <b>zero</b> checks, so <c>/health/ready</c>
/// returned Healthy even with the database unreachable — an ECS target-group or Container Apps ingress
/// probe could not then tell "API up" from "API up but DB down", and would keep routing traffic to a
/// broken instance).
///
/// <para>Depends only on the Application-layer <see cref="IDatabaseConnectivityProbe"/> — never on EF
/// Core — so WebApi's assembly manifest stays EF-free (ADR-0001,
/// <c>LayeringTests.WebApi_Assembly_Manifest_References_No_EfCore_Assembly</c>). It carries no health-
/// check tag, so it runs on <c>/health/ready</c> (mapped with <c>Predicate = _ =&gt; true</c>) and is
/// deliberately excluded from <c>/health/live</c> (mapped with <c>Predicate = _ =&gt; false</c>):
/// liveness must not fail just because a dependency is down, or an orchestrator would kill an instance
/// that only needs its database to come back.</para>
/// </summary>
public sealed class DatabaseHealthCheck(IDatabaseConnectivityProbe probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await probe.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database is reachable.")
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception exception)
        {
            // An unexpected failure while probing is itself a not-ready signal, not a 500. The message
            // is a fixed string; the exception is attached for the health-report logger only, never
            // rendered to an anonymous /health/ready caller.
            return HealthCheckResult.Unhealthy("Database connectivity probe failed.", exception);
        }
    }
}
