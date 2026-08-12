namespace CMPlus.Application.Abstractions;

/// <summary>
/// A minimal "can the app reach its database right now?" probe, for the <c>/health/ready</c> readiness
/// check. Deliberately a tiny Application-layer abstraction with no EF Core surface so the WebApi
/// health check can depend on it without WebApi ever referencing EF Core (ADR-0001; enforced by
/// <c>LayeringTests.WebApi_Assembly_Manifest_References_No_EfCore_Assembly</c>). The real
/// implementation lives in Infrastructure and issues a cheap connectivity check against the database;
/// a test can substitute a fake to exercise the unreachable-database path deterministically.
/// </summary>
public interface IDatabaseConnectivityProbe
{
    /// <summary>Returns <c>true</c> if the database is reachable, <c>false</c> otherwise. Never throws
    /// for an ordinary "cannot connect" — a thrown exception is for genuinely unexpected failures and
    /// is surfaced by the health check as unhealthy just the same.</summary>
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
}
