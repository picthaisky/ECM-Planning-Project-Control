using CMPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// Creates <see cref="CmPlusDbContext"/> instances backed by a shared, named EF Core InMemory
/// database for DbContext-level integration tests (S1-BE-03/04) - no real MSSQL container
/// required. See CMPlus.Integration.Tests.csproj for why InMemory was chosen over Sqlite.
/// </summary>
internal sealed class TestDbContextFactory
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public FakeTenantProvider TenantProvider { get; }

    public TestDbContextFactory(Guid initialTenantId)
    {
        TenantProvider = new FakeTenantProvider(initialTenantId);
    }

    public CmPlusDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(_databaseName)
            .Options;

        return new CmPlusDbContext(options, TenantProvider);
    }
}
