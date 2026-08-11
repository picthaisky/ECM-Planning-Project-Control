using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Import.Commands.ImportExcelProgress;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Parsers.Excel;
using CMPlus.Infrastructure.Persistence;
using CMPlus.Integration.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace CMPlus.Integration.Tests.Parsers;

/// <summary>
/// S3-BE-03/US-3.3 end-to-end: export the progress template for a project's activities, fill it in
/// exactly the way a field team would, re-import it through the real
/// <see cref="ImportExcelProgressCommandHandler"/> + <see cref="ImportRepository"/> + InMemory
/// <see cref="CmPlusDbContext"/> - proving the DoD literally ("each row creates an
/// ActivityProgressLog entry... rather than only updating the cached field") rather than only unit-
/// testing the importer/writer in isolation.
/// </summary>
public class ExcelProgressImportRoundTripTests
{
    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private sealed class UnlimitedImportOptions : IImportOptionsProvider
    {
        public long MaxFileSizeBytes => long.MaxValue;

        public long MaxDecompressedSizeBytes => long.MaxValue;

        public long MaxEntityCount => long.MaxValue;
    }

    [Fact]
    public async Task Export_Then_Fill_In_Then_ReImport_Creates_Exactly_One_ProgressLog_Row_Per_Activity()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var factory = new TestDbContextFactory(tenantId);

        var project = Domain.Entities.Project.Create(
            tenantId, "Sample Project", "P-1", "Owner",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(6), bac: 1_000_000m, dataDate: DateTimeOffset.UtcNow);
        var wbsNode = new WBSNode(tenantId, project.Id, "C1", "Civil Works", 0m);
        var activityA = new Activity(
            tenantId, wbsNode.Id, "A-1", "Excavation",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 100_000m);
        var activityB = new Activity(
            tenantId, wbsNode.Id, "A-2", "Foundation",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 200_000m);

        using (var seedContext = factory.CreateContext())
        {
            seedContext.Projects.Add(project);
            seedContext.WBSNodes.Add(wbsNode);
            seedContext.Activities.AddRange(activityA, activityB);
            await seedContext.SaveChangesAsync();
        }

        // --- Export -------------------------------------------------------------------------
        using var exportContext = factory.CreateContext();
        var exportRepository = new ImportRepository(
            exportContext, factory.TenantProvider, new FixedCurrentUser(userId),
            new FixedClock(DateTimeOffset.Parse("2026-07-28T00:00:00Z")));
        var templateRows = await exportRepository.GetActivityTemplateRowsAsync(project.Id, CancellationToken.None);
        Assert.Equal(2, templateRows.Count);

        var exportedBytes = new ExcelProgressTemplateWriter().WriteTemplate(templateRows);

        // --- Field team fills in PeriodEndDate/ProgressPercentage/ActualQuantity ------------
        using var package = new ExcelPackage(new MemoryStream(exportedBytes));
        var sheet = package.Workbook.Worksheets.First();
        for (var row = 2; row <= sheet.Dimension.End.Row; row++)
        {
            sheet.Cells[row, 5].Value = new DateTime(2026, 7, 20); // PeriodEndDate
            sheet.Cells[row, 6].Value = 45m; // ProgressPercentage
            sheet.Cells[row, 7].Value = 4.5m; // ActualQuantity
        }

        using var filledStream = new MemoryStream();
        package.SaveAs(filledStream);
        var filledBytes = filledStream.ToArray();

        // --- Re-import through the real command handler -------------------------------------
        using var importContext = factory.CreateContext();
        var importRepository = new ImportRepository(
            importContext, factory.TenantProvider, new FixedCurrentUser(userId),
            new FixedClock(DateTimeOffset.Parse("2026-07-28T01:00:00Z")));
        var importOptions = new UnlimitedImportOptions();
        var handler = new ImportExcelProgressCommandHandler(
            importRepository, new ExcelProgressImporter(importOptions), importOptions,
            factory.TenantProvider, new FixedCurrentUser(userId),
            new FixedClock(DateTimeOffset.Parse("2026-07-28T01:00:00Z")));

        var result = await handler.Handle(
            new ImportExcelProgressCommand(project.Id, "progress-update.xlsx", filledBytes), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : string.Empty);
        Assert.Equal(ImportJobStatus.Succeeded, result.Value.Status);
        Assert.Equal(2, result.Value.RowsImported);

        // --- Verify: one ActivityProgressLog row per activity - never a raw cache overwrite --
        using var verifyContext = factory.CreateContext();
        var logs = await verifyContext.ActivityProgressLogs.AsNoTracking().ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal(45m, l.ProgressPercentage));
        Assert.All(logs, l => Assert.Equal(4.5m, l.ActualQuantity));
        Assert.All(logs, l => Assert.Equal(ProgressSource.Import, l.Source));
        Assert.Contains(logs, l => l.ActivityId == activityA.Id);
        Assert.Contains(logs, l => l.ActivityId == activityB.Id);

        // The cache also moved (RecordProgress's normal effect, since this is the newest entry) -
        // confirms the write path really is Activity.RecordProgress end to end, not a bypass.
        var reloadedActivities = await verifyContext.Activities.AsNoTracking().ToListAsync();
        Assert.All(reloadedActivities, a => Assert.Equal(45m, a.ProgressPercentage));

        // Exactly one summarizing FileImportJob row, plus exactly one summarizing AuditLog row for
        // the whole batch (db-conventions.md §8) - never one audit row per activity/log.
        var jobs = await verifyContext.FileImportJobs.AsNoTracking().ToListAsync();
        var job = Assert.Single(jobs);
        Assert.Equal(ImportFileFormat.Excel, job.Format);

        var auditLogs = await verifyContext.AuditLogs.AsNoTracking().ToListAsync();
        Assert.Single(auditLogs);
    }
}
