using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>S2-BE-02 DoD: one <see cref="AuditLog"/> row per successful Create/Update/Delete;
/// nothing is written when a command never reaches <c>SaveChanges</c> (e.g. domain validation
/// throws first).</summary>
public class AuditSaveChangesInterceptorTests
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

    private static CmPlusDbContext CreateContext(Guid tenantId, Guid? actorUserId, string databaseName)
    {
        var tenantProvider = new FakeTenantProvider(tenantId);
        var currentUser = new FixedCurrentUserContext(actorUserId);
        var clock = new FixedDateTimeProvider(new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.FromHours(7)));
        var interceptor = new AuditSaveChangesInterceptor(tenantProvider, currentUser, clock);

        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptor)
            .Options;

        return new CmPlusDbContext(options, tenantProvider);
    }

    [Fact]
    public async Task SaveChanges_Writes_One_AuditLog_Row_For_A_Created_Entity()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        using (var context = CreateContext(tenantId, actorUserId, databaseName))
        {
            context.Projects.Add(Project.Create(
                tenantId, "Project", "P-1", "Owner", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
                100_000m, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        using (var verify = CreateContext(tenantId, actorUserId, databaseName))
        {
            var logs = await verify.AuditLogs.ToListAsync();

            var projectLog = Assert.Single(logs, l => l.EntityName == nameof(Project));
            Assert.Equal(AuditAction.Created, projectLog.Action);
            Assert.Equal(actorUserId, projectLog.UserId);
            Assert.Null(projectLog.BeforeJson);
            Assert.NotNull(projectLog.AfterJson);
            Assert.Contains("\"Name\":\"Project\"", projectLog.AfterJson);
        }
    }

    [Fact]
    public async Task SaveChanges_Writes_A_Before_And_After_Snapshot_For_An_Updated_Entity()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        Project project;
        using (var context = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            project = Project.Create(
                tenantId, "Original Name", "P-2", "Owner", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
                100_000m, DateTimeOffset.UtcNow);
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        using (var context = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            var tracked = await context.Projects.SingleAsync(p => p.Id == project.Id);
            tracked.Rename("Updated Name");
            await context.SaveChangesAsync();
        }

        using (var verify = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            var updateLog = await verify.AuditLogs.SingleAsync(l => l.EntityName == nameof(Project) && l.Action == AuditAction.Updated);

            Assert.Contains("\"Name\":\"Original Name\"", updateLog.BeforeJson);
            Assert.Contains("\"Name\":\"Updated Name\"", updateLog.AfterJson);
            Assert.Null(updateLog.UserId); // system/no-actor context, distinguishable from a real user
        }
    }

    [Fact]
    public async Task SaveChanges_Writes_Nothing_When_Domain_Validation_Throws_Before_SaveChanges_Is_Reached()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        // User's constructor validates email format and throws DomainException synchronously,
        // before the entity ever reaches the change tracker/SaveChanges - simulating a command
        // whose validation fails upstream of persistence entirely.
        Assert.Throws<DomainException>(() => new User(tenantId, "not-an-email", UserRole.PM, "hash"));

        using var verify = CreateContext(tenantId, actorUserId: null, databaseName);
        Assert.Empty(await verify.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task PasswordHash_Is_Redacted_From_Audit_Snapshots_Never_Stored_In_Cleartext()
    {
        // S2-SEC-01 finding M-01: an audit trail leak/query must not become a second place
        // credential material is recoverable from, even hashed - AuditLog has none of the access
        // restrictions User.PasswordHash's own column has.
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        const string realHash = "pbkdf2$210000$not-a-real-hash-but-shaped-like-one";

        User user;
        using (var context = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            user = new User(tenantId, "redaction-test@example.com", UserRole.PM, realHash);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        using (var context = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            var tracked = await context.Users.SingleAsync(u => u.Id == user.Id);
            tracked.ChangePasswordHash("pbkdf2$210000$a-different-hash-after-reset");
            await context.SaveChangesAsync();
        }

        using var verify = CreateContext(tenantId, actorUserId: null, databaseName);
        var logs = await verify.AuditLogs.Where(l => l.EntityName == nameof(User)).ToListAsync();

        Assert.Equal(2, logs.Count); // Created + Updated
        foreach (var log in logs)
        {
            Assert.DoesNotContain(realHash, log.BeforeJson ?? string.Empty);
            Assert.DoesNotContain(realHash, log.AfterJson ?? string.Empty);
            Assert.DoesNotContain("a-different-hash-after-reset", log.AfterJson ?? string.Empty);
        }

        var updateLog = Assert.Single(logs, l => l.Action == AuditAction.Updated);
        Assert.Contains("\"PasswordHash\":\"***REDACTED***\"", updateLog.BeforeJson);
        Assert.Contains("\"PasswordHash\":\"***REDACTED***\"", updateLog.AfterJson);
    }

    [Fact]
    public async Task AuditLog_Rows_Are_Never_Self_Audited()
    {
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        using (var context = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            context.Projects.Add(Project.Create(
                tenantId, "Project", "P-3", "Owner", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6),
                100_000m, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        using var verify = CreateContext(tenantId, actorUserId: null, databaseName);
        var logs = await verify.AuditLogs.ToListAsync();

        // Exactly one row (the Project) - no second row auditing the AuditLog insert itself.
        Assert.Single(logs);
        Assert.DoesNotContain(logs, l => l.EntityName == nameof(AuditLog));
    }
}
