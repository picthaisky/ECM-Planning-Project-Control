using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Progress.Commands.BatchRecordProgress;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S4-BE-03 DoD, verified end to end against a real <see cref="CmPlusDbContext"/> with the real
/// <see cref="AuditSaveChangesInterceptor"/> wired in (not just a fake repository, which would never
/// exercise <see cref="CmPlusDbContext.SuppressPerEntityAudit"/> at all): submitting 20 activities
/// creates exactly 20 <see cref="ActivityProgressLog"/> rows sharing one <see cref="Project"/>'s
/// <c>PeriodEndDate</c>, and exactly one summarizing <see cref="AuditLog"/> row for the whole batch -
/// proving the interceptor's default one-row-per-changed-entity behaviour really is suppressed for
/// the 20 log inserts + 20 activity updates, not merely that the repository's own manual row is added
/// alongside 40 more from the interceptor.
/// </summary>
public class BatchRecordProgressCommandHandlerIntegrationTests
{
    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private static (CmPlusDbContext Context, FakeTenantProvider TenantProvider) CreateContext(
        Guid tenantId, Guid? actorUserId, string databaseName, DateTimeOffset now)
    {
        var tenantProvider = new FakeTenantProvider(tenantId);
        var currentUser = new FixedCurrentUserContext(actorUserId);
        var clock = new FixedDateTimeProvider(now);
        var interceptor = new AuditSaveChangesInterceptor(tenantProvider, currentUser, clock);

        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptor)
            .Options;

        return (new CmPlusDbContext(options, tenantProvider), tenantProvider);
    }

    [Fact]
    public async Task Handle_Creates_Twenty_Log_Rows_And_Exactly_One_Audit_Row_For_The_Whole_Batch()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var seedTime = DateTimeOffset.Parse("2026-07-29T08:00:00Z");
        var batchTime = DateTimeOffset.Parse("2026-07-29T09:00:00Z");
        var periodEndDate = DateTimeOffset.Parse("2026-07-25T00:00:00Z");

        Project project;
        var activities = new List<Activity>();
        {
            var (context, _) = CreateContext(tenantId, actorUserId, databaseName, seedTime);
            using (context)
            {
                project = Project.Create(
                    tenantId, "Batch Progress Project", "P-BATCH-1", "Owner",
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
                    bac: 5_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
                context.Projects.Add(project);

                var wbsNode = new WBSNode(tenantId, project.Id, "C1", "Civil Works", 100m);
                context.WBSNodes.Add(wbsNode);

                for (var i = 0; i < 20; i++)
                {
                    var activity = new Activity(
                        tenantId, wbsNode.Id, $"A-{i:D2}", $"Activity {i}",
                        DateTimeOffset.Parse("2026-07-01T00:00:00Z"), DateTimeOffset.Parse("2026-07-31T00:00:00Z"), 30, 10_000m);
                    activities.Add(activity);
                    context.Activities.Add(activity);
                }

                await context.SaveChangesAsync();
            }
        }

        int auditLogCountBeforeBatch;
        {
            var (context, _) = CreateContext(tenantId, actorUserId, databaseName, batchTime);
            using (context)
            {
                auditLogCountBeforeBatch = await context.AuditLogs.CountAsync();
            }
        }

        {
            var (context, tenantProvider) = CreateContext(tenantId, actorUserId, databaseName, batchTime);
            using (context)
            {
                var repository = new BatchProgressRepository(
                    context, tenantProvider, new FixedCurrentUserContext(actorUserId), new FixedDateTimeProvider(batchTime));
                var handler = new BatchRecordProgressCommandHandler(
                    repository, new FixedCurrentUserContext(actorUserId), new FixedDateTimeProvider(batchTime));

                var entries = activities.Select((a, i) => new BatchProgressEntry(a.Id, 5m * (i + 1), null)).ToList();
                var result = await handler.Handle(
                    new BatchRecordProgressCommand(project.Id, periodEndDate, entries), CancellationToken.None);

                Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
                Assert.Equal(20, result.Value.EntriesRecorded);
            }
        }

        {
            var (verify, _) = CreateContext(tenantId, actorUserId, databaseName, batchTime);
            using (verify)
            {
                var logs = await verify.ActivityProgressLogs.AsNoTracking().ToListAsync();
                Assert.Equal(20, logs.Count);
                Assert.All(logs, l => Assert.Equal(periodEndDate, l.PeriodEndDate));
                Assert.All(logs, l => Assert.Equal(ProgressSource.Manual, l.Source));

                var reloadedActivities = await verify.Activities.AsNoTracking().ToListAsync();
                Assert.All(reloadedActivities, a => Assert.True(a.ProgressPercentage > 0m));

                var auditLogCountAfterBatch = await verify.AuditLogs.CountAsync();
                // Exactly one new AuditLog row for the whole batch - not 20 (log inserts) + 20
                // (activity updates) more on top of it, proving SuppressPerEntityAudit really
                // suppressed the interceptor's own default per-entity behaviour for this
                // SaveChanges call.
                Assert.Equal(1, auditLogCountAfterBatch - auditLogCountBeforeBatch);
            }
        }
    }
}
