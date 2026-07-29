using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Projects.Commands.UpdateProject;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Persistence;

/// <summary>
/// S4-BE-02 DoD: "a successful edit writes an AuditLog entry with old/new values" - verified against
/// a real <see cref="CmPlusDbContext"/> + the real <see cref="AuditSaveChangesInterceptor"/> (not
/// assumed from the Application-layer unit tests, which use a fake repository and never exercise the
/// interceptor at all). Uses the actual <see cref="ProjectRepository"/> and
/// <see cref="UpdateProjectCommandHandler"/>, end to end.
/// </summary>
public class UpdateProjectCommandHandlerIntegrationTests
{
    private sealed class FixedDateTimeProvider(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
    }

    private static CmPlusDbContext CreateContext(Guid tenantId, Guid? actorUserId, string databaseName)
    {
        var tenantProvider = new FakeTenantProvider(tenantId);
        var currentUser = new FixedCurrentUserContext(actorUserId);
        var clock = new FixedDateTimeProvider(new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.FromHours(7)));
        var interceptor = new AuditSaveChangesInterceptor(tenantProvider, currentUser, clock);

        var options = new DbContextOptionsBuilder<CmPlusDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(interceptor)
            .Options;

        return new CmPlusDbContext(options, tenantProvider);
    }

    private static UpdateProjectCommand BuildCommand(Project project) => new(
        ProjectId: project.Id,
        Name: project.Name,
        Code: project.Code,
        Owner: project.Owner,
        ContractStart: project.ContractStart,
        ContractFinish: project.ContractFinish,
        Bac: project.BAC,
        ContractValue: project.ContractValue,
        RetentionRate: 7.50m,
        AdvanceRate: project.AdvanceRate,
        RetentionCapPercentage: project.RetentionCapPercentage,
        RetentionRelease1Percentage: project.RetentionRelease1Percentage,
        DefectsLiabilityMonths: project.DefectsLiabilityMonths,
        AdvanceAmountPaid: project.AdvanceAmountPaid,
        AdvanceRecoveryMethod: project.AdvanceRecoveryMethod,
        AdvanceRecoveryStartPct: project.AdvanceRecoveryStartPct,
        AdvanceRecoveryRatePct: project.AdvanceRecoveryRatePct,
        AdvanceRecoveryEndPct: project.AdvanceRecoveryEndPct);

    [Fact]
    public async Task Handle_Writes_Exactly_One_AuditLog_Row_With_Old_And_New_RetentionRate()
    {
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        Project project;
        using (var context = CreateContext(tenantId, actorUserId, databaseName))
        {
            project = Project.Create(
                tenantId, "Integration Project", "P-INT-1", "Owner Co.",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
                bac: 2_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"), retentionRate: 5.00m);
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        using (var context = CreateContext(tenantId, actorUserId, databaseName))
        {
            var repository = new ProjectRepository(context);
            var handler = new UpdateProjectCommandHandler(repository);

            var result = await handler.Handle(BuildCommand(project), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(7.50m, result.Value.RetentionRate);
        }

        using (var verify = CreateContext(tenantId, actorUserId, databaseName))
        {
            var logs = await verify.AuditLogs.Where(l => l.EntityName == nameof(Project)).ToListAsync();

            // Created (seed) + Updated (the command) - exactly one Updated row, not one per
            // Project.SetXxx call (a single tracked-entity SaveChanges only ever produces one
            // Modified-state row for that entity, regardless of how many of its properties changed).
            Assert.Equal(2, logs.Count);
            var updateLog = Assert.Single(logs, l => l.Action == AuditAction.Updated);

            Assert.Equal(actorUserId, updateLog.UserId);
            Assert.Contains("\"RetentionRate\":5.00", updateLog.BeforeJson);
            Assert.Contains("\"RetentionRate\":7.50", updateLog.AfterJson);
        }
    }

    [Fact]
    public async Task Handle_Writes_Old_And_New_Values_For_Every_Field_Changed_In_One_Edit()
    {
        // S4-QA-02 gap: the existing audit test above only ever changes RetentionRate. This proves
        // the AuditSaveChangesInterceptor's before/after snapshot captures *every* changed scalar
        // property in the same single AuditLog row when several fields are edited at once (Name,
        // RetentionRate and AdvanceRate together) - not just whichever one field a narrower test
        // happens to touch.
        var tenantId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        Project project;
        using (var context = CreateContext(tenantId, actorUserId, databaseName))
        {
            project = Project.Create(
                tenantId, "Original Name", "P-INT-3", "Owner Co.",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
                bac: 2_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                retentionRate: 5.00m, advanceRate: 10.00m);
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        using (var context = CreateContext(tenantId, actorUserId, databaseName))
        {
            var repository = new ProjectRepository(context);
            var handler = new UpdateProjectCommandHandler(repository);
            var command = BuildCommand(project) with
            {
                Name = "Renamed Project",
                RetentionRate = 7.50m,
                AdvanceRate = 15.00m,
            };

            var result = await handler.Handle(command, CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        using (var verify = CreateContext(tenantId, actorUserId, databaseName))
        {
            var updateLog = await verify.AuditLogs.SingleAsync(
                l => l.EntityName == nameof(Project) && l.Action == AuditAction.Updated);

            Assert.Equal(actorUserId, updateLog.UserId);

            Assert.Contains("\"Name\":\"Original Name\"", updateLog.BeforeJson);
            Assert.Contains("\"Name\":\"Renamed Project\"", updateLog.AfterJson);

            Assert.Contains("\"RetentionRate\":5.00", updateLog.BeforeJson);
            Assert.Contains("\"RetentionRate\":7.50", updateLog.AfterJson);

            Assert.Contains("\"AdvanceRate\":10.00", updateLog.BeforeJson);
            Assert.Contains("\"AdvanceRate\":15.00", updateLog.AfterJson);
        }
    }

    [Fact]
    public async Task Handle_Writes_No_AuditLog_Row_When_The_Domain_Rejects_An_Invalid_Date_Range()
    {
        // Complements the RetentionRate happy-path audit test above with the failure path: when
        // Project.SetContractDates throws (US-4.3's "no silent acceptance of an invalid date
        // range", defense-in-depth against a validator bypass - mirrors
        // UpdateProjectCommandHandlerTests.Handle_Propagates_The_Domain_Invariant_When_ContractFinish_Precedes_ContractStart),
        // SaveChanges is never reached, so the AuditSaveChangesInterceptor must never fire and no
        // Updated row should exist - only the original Created row from seeding.
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        Project project;
        using (var context = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            project = Project.Create(
                tenantId, "Project", "P-INT-4", "Owner",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
                bac: 1_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        using (var context = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            var repository = new ProjectRepository(context);
            var handler = new UpdateProjectCommandHandler(repository);
            var command = BuildCommand(project) with
            {
                ContractStart = DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                ContractFinish = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            };

            await Assert.ThrowsAsync<CMPlus.Domain.Common.DomainException>(
                () => handler.Handle(command, CancellationToken.None));
        }

        using (var verify = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            var logs = await verify.AuditLogs.Where(l => l.EntityName == nameof(Project)).ToListAsync();

            var createdLog = Assert.Single(logs); // only the seed's Created row
            Assert.Equal(AuditAction.Created, createdLog.Action);
            Assert.DoesNotContain(logs, l => l.Action == AuditAction.Updated);
        }
    }

    [Fact]
    public async Task Handle_Never_Recalculates_Anything_Outside_The_Project_Table()
    {
        // US-4.4 DoD: rate changes must never retroactively touch an already-issued Payment
        // Certificate. No such entity exists yet (Sprint 9) - this test documents, against a real
        // DbContext, that the only table this command's SaveChanges touches is Projects (+ its own
        // AuditLog row) - never any other table, proving the invariant holds structurally.
        var tenantId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        Project project;
        using (var context = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            project = Project.Create(
                tenantId, "Project", "P-INT-2", "Owner",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-31T00:00:00Z"),
                bac: 1_000_000m, dataDate: DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        using (var context = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            var repository = new ProjectRepository(context);
            var handler = new UpdateProjectCommandHandler(repository);
            await handler.Handle(BuildCommand(project), CancellationToken.None);
        }

        using (var verify = CreateContext(tenantId, actorUserId: null, databaseName))
        {
            Assert.Empty(await verify.ActivityProgressLogs.ToListAsync());
            Assert.Empty(await verify.WBSNodes.ToListAsync());
            Assert.Empty(await verify.Activities.ToListAsync());
        }
    }
}
