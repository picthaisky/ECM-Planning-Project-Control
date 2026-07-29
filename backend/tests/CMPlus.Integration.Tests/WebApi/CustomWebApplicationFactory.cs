using CMPlus.Infrastructure.Auth;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// Hosts the real <c>CMPlus.WebApi</c> app (S2-BE-01/03/07 end-to-end tests) with two
/// substitutions, both standard for this kind of test and neither weakening what is actually
/// being verified: (1) <see cref="CmPlusDbContext"/> is repointed at a uniquely-named EF Core
/// InMemory database instead of a real SQL Server (same rationale as
/// <c>TestDbContextFactory</c> elsewhere in this project - no MSSQL container required to run
/// these); (2) <c>Jwt:SigningKey</c>/<c>Issuer</c>/<c>Audience</c> are overridden to fixed,
/// test-only values (never a production secret) so a test can construct/verify tokens
/// deterministically without needing a real <c>infra/docker/.env</c>.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string JwtSigningKeyForTests = "integration-test-only-signing-key-never-a-real-secret-0123456789";
    public const string JwtIssuerForTests = "CMPlus-IntegrationTests";
    public const string JwtAudienceForTests = "CMPlus-IntegrationTests-Clients";

    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = JwtSigningKeyForTests,
                ["Jwt:Issuer"] = JwtIssuerForTests,
                ["Jwt:Audience"] = JwtAudienceForTests,
                ["Jwt:ExpiryMinutes"] = "60",
            });
        });

        builder.ConfigureServices(services =>
        {
            // AddInfrastructure() already registered CmPlusDbContext against SqlServer. Removing
            // only the single DbContextOptions<CmPlusDbContext> descriptor is not enough - EF Core
            // also registers one IDbContextOptionsConfiguration<CmPlusDbContext> descriptor per
            // AddDbContext call, and those accumulate (they are not replaced), so leaving the old
            // one in place makes both UseSqlServer and UseInMemoryDatabase apply to the same
            // options object ("only a single database provider can be registered"). Removing every
            // descriptor generic over CmPlusDbContext before re-registering is the robust fix.
            var descriptorsToRemove = services
                .Where(d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(CmPlusDbContext)))
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<CmPlusDbContext>((provider, options) =>
                options.UseInMemoryDatabase(_databaseName)
                       .AddInterceptors(provider.GetRequiredService<Infrastructure.Persistence.Interceptors.AuditSaveChangesInterceptor>()));
        });
    }

    /// <summary>Runs the real dev seeder (S1-DB-03/S2-BE-01) against this factory's InMemory
    /// database - the same tenants/users/passwords a real dev environment would have, so login
    /// tests exercise the real seeded credentials rather than a bespoke test-only user.</summary>
    public async Task<DevSeedResult> SeedAsync()
    {
        var tenantContext = new SeedTenantContext();
        var options = new DbContextOptionsBuilder<CmPlusDbContext>().UseInMemoryDatabase(_databaseName).Options;
        await using var context = new CmPlusDbContext(options, tenantContext);
        return await DevDataSeeder.SeedAsync(context, tenantContext, new Pbkdf2PasswordHasher());
    }
}
