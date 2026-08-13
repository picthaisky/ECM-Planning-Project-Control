using CMPlus.Application.Abstractions;
using CMPlus.Domain.Entities;
using CMPlus.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence.Seed;

/// <summary>Summary of one <see cref="DevDataSeeder.SeedAsync"/> pass, returned so a CI/dev runner
/// can log what happened without a second round of queries.</summary>
public sealed record DevSeedResult(int TenantsCreated, int UsersCreated, int ProjectsCreated);

/// <summary>
/// Idempotent dev/CI seed data (S1-DB-01..03, docs/10 §6 S1-DB-03): two clearly-distinguishable
/// tenants, each with one user per <see cref="CMPlus.Domain.Enums.UserRole"/> value and one sample
/// project, so <c>qa-engineer</c>'s Sprint 1 tenant-isolation tests (S1-QA-01) have real
/// cross-tenant data to assert against. Every insert is check-before-insert on a natural key
/// (docs/db-conventions.md §5) - safe to run any number of times against the same database without
/// creating duplicates or throwing.
///
/// Not part of any request pipeline: the caller (a console/CI runner, never the WebApi host)
/// supplies a <see cref="SeedTenantContext"/> - a mutable <c>ITenantProvider</c> - so this seeder
/// can legitimately write rows for more than one tenant in a single pass while still going through
/// the same global tenant query filter and server-side TenantId stamp (ADR-0002) every other write
/// path uses; no <c>IgnoreQueryFilters()</c> escape hatch is used anywhere in this class.
///
/// S2-BE-01/06: every seeded user now gets a real, hashed dev-only password (via
/// <see cref="IPasswordHasher"/>) so login can be exercised end-to-end, replacing Sprint 1's
/// <c>dev-seed-placeholder-hash::&lt;role&gt;</c> string that could never authenticate anything.
/// Every newly-created tenant also gets the two default approval policies seeded
/// (<see cref="ApprovalPolicySeeder"/>) - dev/CI seeding and "real" tenant provisioning share this
/// one code path today (S1 baseline), so wiring it in here covers both.
/// </summary>
public static class DevDataSeeder
{
    /// <summary>Fixed, documented dev/CI-only credential - never a production secret. Every
    /// seeded user (any tenant, any role) shares this password so QA/dev can log in as any
    /// seeded account by email alone.</summary>
    public const string DevSeedPassword = "Dev@CMPlus2026!";

    public static async Task<DevSeedResult> SeedAsync(
        CmPlusDbContext dbContext,
        SeedTenantContext tenantContext,
        IPasswordHasher? passwordHasher = null,
        CancellationToken cancellationToken = default)
    {
        passwordHasher ??= new Pbkdf2PasswordHasher();

        var tenantsCreated = 0;
        var usersCreated = 0;
        var projectsCreated = 0;

        foreach (var tenantSeed in DevSeedData.Tenants)
        {
            var (tenant, tenantWasCreated) = await GetOrCreateTenantAsync(dbContext, tenantSeed.Name, cancellationToken);
            if (tenantWasCreated)
            {
                tenantsCreated++;
            }

            // Every subsequent read/write in this iteration must be scoped to this tenant - both
            // for the global query filter (so the "does this already exist" checks below only see
            // this tenant's rows) and for the server-side TenantId stamp on insert (ADR-0002).
            tenantContext.TenantId = tenant.Id;

            if (tenantWasCreated)
            {
                await ApprovalPolicySeeder.SeedDefaultPoliciesAsync(dbContext, tenant.Id, tenantSeed.Project.ContractStart, cancellationToken);
                await WorkCategorySeeder.SeedDefaultCategoriesAsync(dbContext, tenant.Id, cancellationToken);
            }

            foreach (var role in DevSeedData.AllRoles)
            {
                var email = $"{role.ToString().ToLowerInvariant()}@{tenantSeed.EmailDomain}";
                var userWasCreated = await GetOrCreateUserAsync(dbContext, tenant.Id, email, role, passwordHasher, cancellationToken);
                if (userWasCreated)
                {
                    usersCreated++;
                }
            }

            var projectWasCreated = await GetOrCreateProjectAsync(dbContext, tenant.Id, tenantSeed.Project, cancellationToken);
            if (projectWasCreated)
            {
                projectsCreated++;
            }
        }

        return new DevSeedResult(tenantsCreated, usersCreated, projectsCreated);
    }

    private static async Task<(Tenant Tenant, bool Created)> GetOrCreateTenantAsync(
        CmPlusDbContext dbContext, string name, CancellationToken cancellationToken)
    {
        // Tenant is explicitly exempt from tenant scoping (docs/db-conventions.md §2 rule 5), so
        // this query is never filtered - exactly what we want, since we are looking the tenant up
        // by name across the whole table, not within a tenant.
        var existing = await dbContext.Tenants.SingleOrDefaultAsync(t => t.Name == name, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var tenant = new Tenant(name);
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (tenant, true);
    }

    private static async Task<bool> GetOrCreateUserAsync(
        CmPlusDbContext dbContext,
        Guid tenantId,
        string email,
        Domain.Enums.UserRole role,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken)
    {
        // Scoped by the active tenant via the global query filter (tenantContext.TenantId was just
        // set to `tenantId` by the caller) - matches the (TenantId, Email) unique index exactly.
        var exists = await dbContext.Users.AnyAsync(u => u.Email == email, cancellationToken);
        if (exists)
        {
            return false;
        }

        var passwordHash = passwordHasher.Hash(DevSeedPassword);

        var user = new User(tenantId, email, role, passwordHash);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static async Task<bool> GetOrCreateProjectAsync(
        CmPlusDbContext dbContext, Guid tenantId, ProjectSeed seed, CancellationToken cancellationToken)
    {
        // Scoped by the active tenant via the global query filter - matches the (TenantId, Code)
        // unique index exactly.
        var exists = await dbContext.Projects.AnyAsync(p => p.Code == seed.Code, cancellationToken);
        if (exists)
        {
            return false;
        }

        var project = Project.Create(
            tenantId,
            seed.Name,
            seed.Code,
            seed.Owner,
            seed.ContractStart,
            seed.ContractFinish,
            seed.Bac,
            seed.DataDate,
            seed.RetentionRate,
            seed.AdvanceRate);

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
