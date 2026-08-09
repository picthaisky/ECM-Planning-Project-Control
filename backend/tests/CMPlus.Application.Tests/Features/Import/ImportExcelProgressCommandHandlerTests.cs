using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Import.Commands.ImportExcelProgress;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Import;

public class ImportExcelProgressCommandHandlerTests
{
    private sealed class FakeImportRepository : IImportRepository
    {
        public bool ProjectExists { get; set; } = true;
        public IReadOnlyDictionary<Guid, Activity> ActivitiesToReturn { get; set; } = new Dictionary<Guid, Activity>();
        public FileImportJob? SavedFailedJob { get; private set; }
        public (FileImportJob Job, IReadOnlyList<ActivityProgressLog> Entries)? SavedProgressImport { get; private set; }

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(ProjectExists);

        public Task SaveFailedJobAsync(FileImportJob job, CancellationToken cancellationToken = default)
        {
            SavedFailedJob = job;
            return Task.CompletedTask;
        }

        public Task SaveScheduleImportAsync(FileImportJob job, ParsedSchedule schedule, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, Activity>> LoadActivitiesForProjectAsync(
            Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivitiesToReturn);

        public Task SaveProgressImportAsync(FileImportJob job, IReadOnlyList<ActivityProgressLog> newEntries, CancellationToken cancellationToken = default)
        {
            SavedProgressImport = (job, newEntries);
            return Task.CompletedTask;
        }

        public Task<FileImportJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<FileImportJob>> GetJobHistoryAsync(Guid projectId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ActivityProgressTemplateRow>> GetActivityTemplateRowsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ImporterMustNotBeCalled : IExcelProgressImporter
    {
        public Result<IReadOnlyList<ParsedProgressRow>> Parse(Stream excelContent) =>
            throw new InvalidOperationException("The Excel parser must never be invoked once the size cap has already rejected the file.");
    }

    private sealed class StubImporter : IExcelProgressImporter
    {
        public Result<IReadOnlyList<ParsedProgressRow>> ResultToReturn { get; set; } = Result<IReadOnlyList<ParsedProgressRow>>.Success([]);

        public Result<IReadOnlyList<ParsedProgressRow>> Parse(Stream excelContent) => ResultToReturn;
    }

    private sealed class FakeImportOptionsProvider(long maxFileSizeBytes) : IImportOptionsProvider
    {
        public long MaxFileSizeBytes { get; } = maxFileSizeBytes;

        public long MaxDecompressedSizeBytes => long.MaxValue;

        public long MaxEntityCount => long.MaxValue;
    }

    // S3-SEC-01 DoD item 2: the XLSX ZIP local-file-header signature - the minimal content that
    // satisfies FileSignatureValidator.IsXlsx, so a stub-importer test's placeholder content isn't
    // rejected by the magic-byte gate before it ever reaches the stub.
    private static readonly byte[] XlsxMagicBytes = [0x50, 0x4B, 0x03, 0x04];

    private sealed class FakeTenantProvider(Guid tenantId) : ITenantProvider
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class FakeCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;

        public UserRole Role => UserRole.PM;
    }

    private sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static ImportExcelProgressCommandHandler CreateHandler(
        FakeImportRepository repository, IExcelProgressImporter importer, long maxFileSizeBytes = 1024) =>
        new(
            repository, importer, new FakeImportOptionsProvider(maxFileSizeBytes),
            new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()),
            new FakeClock(DateTimeOffset.Parse("2026-07-28T00:00:00+00:00")));

    [Fact]
    public async Task An_Oversized_File_Is_Rejected_Before_The_Importer_Is_Ever_Invoked()
    {
        var repository = new FakeImportRepository();
        var importerThatMustNotRun = new ImporterMustNotBeCalled();
        var handler = CreateHandler(repository, importerThatMustNotRun, maxFileSizeBytes: 10);

        var result = await handler.Handle(
            new ImportExcelProgressCommand(Guid.NewGuid(), "big.xlsx", new byte[11]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportJobStatus.Failed, result.Value.Status);
        Assert.Contains(ImportErrorCodes.FileTooLarge, result.Value.ErrorJson);
    }

    [Fact]
    public async Task A_Row_Referencing_An_Unknown_ActivityId_Fails_The_Whole_Import()
    {
        var repository = new FakeImportRepository { ActivitiesToReturn = new Dictionary<Guid, Activity>() };
        var unknownActivityId = Guid.NewGuid();
        var importer = new StubImporter
        {
            ResultToReturn = Result<IReadOnlyList<ParsedProgressRow>>.Success(
                [new ParsedProgressRow(unknownActivityId, "A-999", DateTimeOffset.UtcNow, 50m, null, SourceRowNumber: 2)]),
        };
        var handler = CreateHandler(repository, importer);

        var result = await handler.Handle(
            new ImportExcelProgressCommand(Guid.NewGuid(), "progress.xlsx", XlsxMagicBytes), CancellationToken.None);

        Assert.Equal(ImportJobStatus.Failed, result.Value.Status);
        Assert.Contains(ImportErrorCodes.UnknownActivity, result.Value.ErrorJson);
        Assert.Null(repository.SavedProgressImport);
    }

    [Fact]
    public async Task Each_Valid_Row_Produces_One_ActivityProgressLog_Entry_Via_RecordProgress()
    {
        var activity = new Activity(
            Guid.NewGuid(), Guid.NewGuid(), "A-1", "Formwork",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10), 10, 100_000m);
        var repository = new FakeImportRepository
        {
            ActivitiesToReturn = new Dictionary<Guid, Activity> { [activity.Id] = activity },
        };
        var importer = new StubImporter
        {
            ResultToReturn = Result<IReadOnlyList<ParsedProgressRow>>.Success(
                [new ParsedProgressRow(activity.Id, "A-1", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), 40m, 12.5m, SourceRowNumber: 2)]),
        };
        var handler = CreateHandler(repository, importer);

        var result = await handler.Handle(
            new ImportExcelProgressCommand(Guid.NewGuid(), "progress.xlsx", XlsxMagicBytes), CancellationToken.None);

        Assert.Equal(ImportJobStatus.Succeeded, result.Value.Status);
        Assert.Equal(1, result.Value.RowsImported);
        Assert.NotNull(repository.SavedProgressImport);
        var entry = Assert.Single(repository.SavedProgressImport!.Value.Entries);
        Assert.Equal(activity.Id, entry.ActivityId);
        Assert.Equal(40m, entry.ProgressPercentage);
        Assert.Equal(12.5m, entry.ActualQuantity);
        Assert.Equal(ProgressSource.Import, entry.Source);
        // The cache moved too (RecordProgress's normal effect for the newest entry) - proves the
        // write went through Activity.RecordProgress, never a direct cache assignment (ADR-0009).
        Assert.Equal(40m, activity.ProgressPercentage);
    }

    // ------------------------------------------------------------------------------------
    // S3-SEC-01 DoD item 2 (magic-byte / content-type check) - the two remaining combinations of
    // the "each format's bytes on each of the other two routes" matrix (xer/mspdi -> xer, xer/mspdi
    // -> mspdi covered in ImportScheduleFileCommandHandlerTests).
    // ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("ERMHDR\t18.8\tschedule")] // XER-shaped content posted to the xlsx route.
    [InlineData("<?xml version=\"1.0\"?><Project></Project>")] // MSPDI-shaped content posted to the xlsx route.
    public async Task A_Non_Xlsx_Shaped_File_Is_Rejected_Before_The_Importer_Runs(string disguisedText)
    {
        var repository = new FakeImportRepository();
        var importerThatMustNotRun = new ImporterMustNotBeCalled();
        var handler = CreateHandler(repository, importerThatMustNotRun);

        var result = await handler.Handle(
            new ImportExcelProgressCommand(Guid.NewGuid(), "progress.xlsx", System.Text.Encoding.UTF8.GetBytes(disguisedText)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportJobStatus.Failed, result.Value.Status);
        Assert.Contains(ImportErrorCodes.FormatMismatch, result.Value.ErrorJson);
    }

    // ------------------------------------------------------------------------------------
    // S3-SEC-01 M-01 layer 2: an unforeseen importer exception is a safety-netted Failed job, never
    // an unhandled 500, and never echoes the exception's own message into ErrorJson.
    // ------------------------------------------------------------------------------------

    private sealed class ThrowingImporter : IExcelProgressImporter
    {
        public Result<IReadOnlyList<ParsedProgressRow>> Parse(Stream excelContent) =>
            throw new InvalidOperationException("some internal detail that must never reach the client");
    }

    [Fact]
    public async Task An_Unforeseen_Importer_Exception_Produces_A_Failed_Job_Never_A_Raw_Exception()
    {
        var repository = new FakeImportRepository();
        var importer = new ThrowingImporter();
        var handler = CreateHandler(repository, importer);

        var result = await handler.Handle(
            new ImportExcelProgressCommand(Guid.NewGuid(), "boom.xlsx", XlsxMagicBytes), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportJobStatus.Failed, result.Value.Status);
        Assert.Contains(ImportErrorCodes.MalformedFile, result.Value.ErrorJson);
        Assert.DoesNotContain("some internal detail", result.Value.ErrorJson);
        Assert.NotNull(repository.SavedFailedJob);
    }
}
