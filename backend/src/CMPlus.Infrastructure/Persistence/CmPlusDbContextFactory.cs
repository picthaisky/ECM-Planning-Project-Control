using CMPlus.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add/script</c> and <c>dotnet ef database
/// update</c> can build <see cref="CmPlusDbContext"/> without going through the WebApi host's
/// runtime DI composition (which does not exist independently of a running ASP.NET Core app, and
/// whose real <c>ITenantProvider</c> reads a JWT claim that does not exist at migration time -
/// ADR-0002/S1-BE-03). Not used at application runtime; the WebApi composition root
/// (<c>AddInfrastructure</c>) wires the real, JWT-backed <c>ITenantProvider</c> instead.
/// </summary>
public sealed class CmPlusDbContextFactory : IDesignTimeDbContextFactory<CmPlusDbContext>
{
    /// <summary>
    /// Design-time-only stub. The EF Core CLI never executes application queries through the
    /// tenant query filter - it only builds the model to diff/apply schema - so this value is
    /// never actually read for a real request; it exists solely because
    /// <see cref="CmPlusDbContext"/>'s constructor requires an <see cref="ITenantProvider"/>.
    /// </summary>
    private sealed class DesignTimeTenantProvider : ITenantProvider
    {
        public Guid TenantId => Guid.Empty;
    }

    public CmPlusDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__CmPlusDatabase")
            ?? Environment.GetEnvironmentVariable("CMPLUS_DB_CONNECTION")
            // Local dev fallback - matches infra/docker/mssql/.env.mssql.example defaults
            // (MSSQL_APP_DB/MSSQL_APP_USER/MSSQL_APP_PASSWORD/MSSQL_HOST_PORT), which are
            // themselves placeholder "ChangeMe" dev-only credentials already committed to that
            // example file, never real secrets (ADR-0010, docs/security/secrets-policy.md).
            ?? "Server=localhost,1433;Database=CMPlusDb;User Id=cmplus_app;"
               + "Password=Ch4ngeMe!AppLocalDevOnly;TrustServerCertificate=True;";

        var optionsBuilder = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseSqlServer(connectionString);

        return new CmPlusDbContext(optionsBuilder.Options, new DesignTimeTenantProvider());
    }
}
