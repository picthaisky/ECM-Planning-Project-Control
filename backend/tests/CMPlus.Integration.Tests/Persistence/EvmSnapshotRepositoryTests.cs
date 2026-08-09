using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>S7-BE-05: <see cref="EvmSnapshotRepository"/> against a real <see cref="CmPlusDbContext"/> -
/// proves the duplicate-dataDate check and that a successful add is actually durable (survives a
/// fresh context, not just the in-memory tracked instance).</summary>
public class EvmSnapshotRepositoryTests
{
    private static EvmPeriodSnapshot CreateSnapshot(Guid tenantId, Guid projectId, DateTimeOffset dataDate) => new(
        tenantId, projectId, dataDate,
        bac: 1_000_000.00m, pv: 400_000.00m, ev: 300_000.00m, ac: 350_000.00m,
        eacVariant: EacVariant.CpiBased, performanceFactor: 1.166667m, eac: 1_166_666.67m,
        etc: 816_666.67m, vac: -166_666.67m, createdAt: DateTimeOffset.UtcNow, createdByUserId: Guid.NewGuid());

    [Fact]
    public async Task ExistsForDataDateAsync_Is_False_When_Nothing_Has_Been_Closed_Yet()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        using var context = factory.CreateContext();
        var repository = new EvmSnapshotRepository(context);

        var exists = await repository.ExistsForDataDateAsync(Guid.NewGuid(), DateTimeOffset.Parse("2026-06-30T00:00:00Z"));

        Assert.False(exists);
    }

    [Fact]
    public async Task AddAsync_Persists_The_Snapshot_And_ExistsForDataDateAsync_Then_Finds_It()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectId = Guid.NewGuid();
        var dataDate = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");

        using (var writeContext = factory.CreateContext())
        {
            var repository = new EvmSnapshotRepository(writeContext);
            await repository.AddAsync(CreateSnapshot(tenantId, projectId, dataDate));
        }

        using var readContext = factory.CreateContext();
        var readRepository = new EvmSnapshotRepository(readContext);

        Assert.True(await readRepository.ExistsForDataDateAsync(projectId, dataDate));
        Assert.False(await readRepository.ExistsForDataDateAsync(projectId, dataDate.AddDays(1)));

        var persisted = Assert.Single(readContext.EvmPeriodSnapshots);
        Assert.Equal(1_166_666.67m, persisted.Eac);
    }

    [Fact]
    public async Task ExistsForDataDateAsync_Is_Scoped_To_The_Requested_Project_Only()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);
        var projectOne = Guid.NewGuid();
        var projectTwo = Guid.NewGuid();
        var dataDate = DateTimeOffset.Parse("2026-06-30T00:00:00+07:00");

        using (var writeContext = factory.CreateContext())
        {
            var repository = new EvmSnapshotRepository(writeContext);
            await repository.AddAsync(CreateSnapshot(tenantId, projectOne, dataDate));
        }

        using var readContext = factory.CreateContext();
        var readRepository = new EvmSnapshotRepository(readContext);

        Assert.True(await readRepository.ExistsForDataDateAsync(projectOne, dataDate));
        Assert.False(await readRepository.ExistsForDataDateAsync(projectTwo, dataDate));
    }
}
