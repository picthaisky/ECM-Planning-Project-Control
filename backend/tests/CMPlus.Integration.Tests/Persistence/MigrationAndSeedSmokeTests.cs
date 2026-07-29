using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S1-QA-03 DoD: a CI job spins up a fresh MSSQL container, applies the Sprint 1 migration, and
/// runs the dev seed successfully on every PR. This is the test that job actually executes
/// (`.github/workflows/ci.yml`'s `migration-smoke` job sets <c>CMPLUS_TEST_MSSQL_CONNECTION</c>
/// against a real `sqlservr` service container); it exercises the exact same public entry points
/// (<see cref="CmPlusDbContext.Database"/>.<c>MigrateAsync</c> and
/// <see cref="DevDataSeeder.SeedAsync"/>) a real deployment/dev-onboarding run would use - not a
/// re-implementation of either.
///
/// Everywhere else in this project uses the EF Core InMemory provider deliberately (see
/// <see cref="TestDbContextFactory"/>'s remarks - Sqlite can't translate the DateTimeOffset
/// comparisons ADR-0009 needs, and InMemory never touches a real SQL Server anyway), so this is
/// the *only* place in the solution that talks to a real SQL Server. Outside that CI job (i.e. a
/// normal local/dev `dotnet test` run with no database reachable) both tests below soft-skip:
/// they intentionally return without asserting anything when neither
/// <c>CMPLUS_TEST_MSSQL_CONNECTION</c> nor <c>ConnectionStrings__CmPlusDatabase</c> is set, so the
/// Sprint 1 baseline suite (`dotnet test backend/CMPlus.sln`) stays fast/hermetic and this file
/// does not turn every contributor's machine into a hard MSSQL dependency.
/// </summary>
public class MigrationAndSeedSmokeTests
{
    /// <summary>
    /// Same two env var names <see cref="CmPlusDbContextFactory"/> already recognises for
    /// design-time `dotnet ef` commands, plus a QA-specific override
    /// (<c>CMPLUS_TEST_MSSQL_CONNECTION</c>) so the CI job can point this test at its service
    /// container without disturbing the design-time factory's own resolution order.
    /// </summary>
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("CMPLUS_TEST_MSSQL_CONNECTION")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__CmPlusDatabase");

    private static CmPlusDbContext CreateContext(string connectionString, SeedTenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new CmPlusDbContext(options, tenantContext);
    }

    [Fact]
    public async Task Migration_Applies_Cleanly_To_A_Fresh_Database()
    {
        var connectionString = ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return; // Soft-skip: see class remarks. Only the CI migration-smoke job sets this.
        }

        await using var context = CreateContext(connectionString, new SeedTenantContext());

        // US-1.3 DoD: "dotnet ef database update on an empty DB succeeds with no manual
        // intervention". Database.MigrateAsync() is the code-level equivalent of that CLI
        // command - it applies every migration not yet in __EFMigrationsHistory.
        await context.Database.MigrateAsync();

        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Contains(appliedMigrations, m => m.Contains("InitialCreate", StringComparison.Ordinal));

        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();
        Assert.Empty(pendingMigrations);
    }

    [Fact]
    public async Task Migrate_Then_Seed_Creates_Two_Tenants_With_Every_Role_And_Is_Idempotent_On_Rerun()
    {
        var connectionString = ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return; // Soft-skip: see class remarks. Only the CI migration-smoke job sets this.
        }

        await using (var migrateContext = CreateContext(connectionString, new SeedTenantContext()))
        {
            await migrateContext.Database.MigrateAsync();
        }

        // First pass: a brand-new database has neither seed tenant yet. A fresh DbContext/
        // SeedTenantContext per pass (rather than one reused across both) deliberately mirrors two
        // separate process invocations (e.g. a container restart between CI steps), which is a
        // stronger idempotency proof than reusing one EF change tracker across both calls.
        var firstRunTenantContext = new SeedTenantContext();
        DevSeedResult firstRun;
        await using (var firstRunContext = CreateContext(connectionString, firstRunTenantContext))
        {
            firstRun = await DevDataSeeder.SeedAsync(firstRunContext, firstRunTenantContext);
        }

        // docs/10 §6 S1-DB-03 DoD: "2 tenant x ผู้ใช้ทุก role" - DevSeedData seeds exactly 2
        // tenants, one user per CMPlus.Domain.Enums.UserRole value (7 roles, see ADR-0008) each,
        // and one sample project each.
        Assert.Equal(2, firstRun.TenantsCreated);
        Assert.Equal(14, firstRun.UsersCreated);
        Assert.Equal(2, firstRun.ProjectsCreated);

        // Second pass against the same database: S1-DB-03 DoD explicitly requires "รันซ้ำได้
        // (idempotent)" - a rerun (e.g. a redeployed/rebuilt CI service container image, or a
        // developer re-running the seed against an already-seeded dev DB) must create nothing new
        // and must not throw (e.g. a unique-constraint violation on Tenant.Name/User.Email/
        // Project.Code would fail this).
        var secondRunTenantContext = new SeedTenantContext();
        DevSeedResult secondRun;
        await using (var secondRunContext = CreateContext(connectionString, secondRunTenantContext))
        {
            secondRun = await DevDataSeeder.SeedAsync(secondRunContext, secondRunTenantContext);
        }

        Assert.Equal(0, secondRun.TenantsCreated);
        Assert.Equal(0, secondRun.UsersCreated);
        Assert.Equal(0, secondRun.ProjectsCreated);

        // Verify the actual persisted state independently of the seeder's own return values -
        // Tenant is explicitly exempt from tenant scoping (docs/db-conventions.md §2 rule 5), so
        // this query legitimately sees both tenants without IgnoreQueryFilters().
        await using var verifyContext = CreateContext(connectionString, new SeedTenantContext());
        var tenantNames = await verifyContext.Tenants.Select(t => t.Name).ToListAsync();

        Assert.Contains("Siam Construction Co., Ltd.", tenantNames);
        Assert.Contains("Bangkok Infra Builders Ltd.", tenantNames);
        Assert.Equal(2, tenantNames.Count(n =>
            n is "Siam Construction Co., Ltd." or "Bangkok Infra Builders Ltd."));
    }
}
