using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Import.Commands.ImportScheduleFile;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Parsers.Mspdi;
using CMPlus.Infrastructure.Parsers.Xer;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Integration.Tests.Parsers;

/// <summary>
/// S3-BE-01/04 end-to-end: the golden-file XER schedule persisted through the real
/// <see cref="ImportScheduleFileCommandHandler"/> + <see cref="ImportRepository"/> + InMemory
/// <see cref="CmPlusDbContext"/>, proving the bulk-persist path (AddRange + a single SaveChanges,
/// <see cref="CmPlusDbContext.SuppressPerEntityAudit"/>) actually gets exercised end to end - not
/// just unit-tested on the parser in isolation - and that it produces exactly one summarizing
/// <see cref="AuditLog"/> row for the whole import (db-conventions.md §8), never one row per
/// WBS node/activity/relation. Also covers tenant scoping (S3-BE-04 DoD: "every job is
/// TenantId-scoped") for the persisted <see cref="FileImportJob"/>/schedule rows.
/// </summary>
public class XerScheduleImportRoundTripTests
{
    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class UnlimitedImportOptions : IImportOptionsProvider
    {
        public long MaxFileSizeBytes => long.MaxValue;

        public long MaxDecompressedSizeBytes => long.MaxValue;

        public long MaxEntityCount => long.MaxValue;
    }

    [Fact]
    public async Task Importing_The_Golden_Xer_File_Bulk_Persists_Everything_With_One_Summarizing_Audit_Row()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);

        var project = Domain.Entities.Project.Create(
            tenantId, "Sample Project", "P-1", "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6), bac: 1_000_000m, dataDate: DateTimeOffset.UtcNow);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            await seedContext.SaveChangesAsync();
        }

        using var fixtureStream = FixtureFiles.OpenRead("xer/sample-schedule.xer");
        using var fixtureBuffer = new MemoryStream();
        await fixtureStream.CopyToAsync(fixtureBuffer);
        var content = fixtureBuffer.ToArray();

        using var context = factory.CreateContext();
        var repository = new ImportRepository(
            context, factory.TenantProvider, new FixedCurrentUser(userId),
            new FixedClock(DateTimeOffset.Parse("2026-07-28T00:00:00Z")));
        var handler = new ImportScheduleFileCommandHandler(
            repository, new XerScheduleParser(new UnlimitedImportOptions()), new MspdiScheduleParser(), new UnlimitedImportOptions(),
            factory.TenantProvider, new FixedCurrentUser(userId), new FixedClock(DateTimeOffset.Parse("2026-07-28T00:00:00Z")));

        var result = await handler.Handle(
            new ImportScheduleFileCommand(project.Id, ImportFileFormat.Xer, "sample-schedule.xer", content),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(ImportJobStatus.Succeeded, result.Value.Status);
        Assert.Equal(7, result.Value.RowsImported); // 2 WBS + 3 activities + 2 relations, per the golden-file test's hand-computed values.

        using var verifyContext = factory.CreateContext();

        var wbsNodes = await verifyContext.WBSNodes.AsNoTracking().ToListAsync();
        Assert.Equal(2, wbsNodes.Count);
        Assert.All(wbsNodes, n => Assert.Equal(tenantId, n.TenantId)); // S3-BE-04 DoD: every row is TenantId-scoped.
        Assert.All(wbsNodes, n => Assert.Equal(project.Id, n.ProjectId));

        var activities = await verifyContext.Activities.AsNoTracking().ToListAsync();
        Assert.Equal(3, activities.Count);
        Assert.All(activities, a => Assert.Equal(tenantId, a.TenantId));

        var relations = await verifyContext.ActivityRelations.AsNoTracking().ToListAsync();
        Assert.Equal(2, relations.Count);
        Assert.All(relations, r => Assert.Equal(tenantId, r.TenantId));

        // Exactly one FileImportJob row and exactly one AuditLog row for the whole operation -
        // never one audit row per WBS node/activity/relation (db-conventions.md §8).
        var jobs = await verifyContext.FileImportJobs.AsNoTracking().ToListAsync();
        Assert.Single(jobs);

        var auditLogs = await verifyContext.AuditLogs.AsNoTracking().ToListAsync();
        var auditLog = Assert.Single(auditLogs);
        Assert.Equal(nameof(FileImportJob), auditLog.EntityName);
    }
}
