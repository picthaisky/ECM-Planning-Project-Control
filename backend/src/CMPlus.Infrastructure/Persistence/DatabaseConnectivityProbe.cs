using CMPlus.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IDatabaseConnectivityProbe"/> for the <c>/health/ready</c>
/// readiness check. <see cref="DatabaseFacade.CanConnectAsync"/> is a cheap "open a connection, don't
/// run a query" check on a relational provider (SQL Server) — it returns <c>false</c> rather than
/// throwing when the server is unreachable, which is exactly the readiness signal an orchestrator's
/// ingress probe needs so it stops routing traffic to an instance whose database is down.
///
/// <para>On the EF Core InMemory provider used by the integration tests, <c>CanConnectAsync</c> is
/// always <c>true</c> (there is nothing to be unreachable), so the *reachable* path is exercised there
/// end to end, while the *unreachable* → 503 path is exercised deterministically by substituting a
/// fake probe — no real database, and no dependence on the storage engine, needed to prove either
/// branch (the standing InMemory-is-not-the-DB discipline).</para>
/// </summary>
public sealed class DatabaseConnectivityProbe(CmPlusDbContext dbContext) : IDatabaseConnectivityProbe
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.CanConnectAsync(cancellationToken);
}
