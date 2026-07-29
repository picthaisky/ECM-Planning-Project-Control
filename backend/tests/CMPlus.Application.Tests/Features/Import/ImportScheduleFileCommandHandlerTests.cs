using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Import.Commands.ImportScheduleFile;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Import;

/// <summary>
/// S3-BE-01/02/04: <see cref="ImportScheduleFileCommandHandler"/> orchestration, with fakes for
/// every dependency so the size-cap DoD ("rejected before parsing begins") can prove the parser is
/// never even invoked - not just that the eventual outcome is a rejection.
/// </summary>
public class ImportScheduleFileCommandHandlerTests
{
    private sealed class FakeImportRepository : IImportRepository
    {
        public bool ProjectExists { get; set; } = true;
        public FileImportJob? SavedFailedJob { get; private set; }
        public (FileImportJob Job, ParsedSchedule Schedule)? SavedSuccessfulImport { get; private set; }

        public Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectExists);

        public Task SaveFailedJobAsync(FileImportJob job, CancellationToken cancellationToken = default)
        {
            SavedFailedJob = job;
            return Task.CompletedTask;
        }

        public Task SaveScheduleImportAsync(FileImportJob job, ParsedSchedule schedule, CancellationToken cancellationToken = default)
        {
            SavedSuccessfulImport = (job, schedule);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<Guid, Activity>> LoadActivitiesForProjectAsync(
            Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by the schedule-import handler.");

        public Task SaveProgressImportAsync(FileImportJob job, IReadOnlyList<ActivityProgressLog> newEntries, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by the schedule-import handler.");

        public Task<FileImportJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FileImportJob>> GetJobHistoryAsync(Guid projectId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ActivityProgressTemplateRow>> GetActivityTemplateRowsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Throws if ever called - proves "rejected before parsing begins" structurally, not
    /// just by outcome.</summary>
    private sealed class ParserMustNotBeCalled : IXerScheduleParser, IMspdiScheduleParser
    {
        public Result<ParsedSchedule> Parse(Stream content, Guid tenantId, Guid projectId) =>
            throw new InvalidOperationException("The parser must never be invoked once the size cap has already rejected the file.");
    }

    private sealed class StubParser : IXerScheduleParser, IMspdiScheduleParser
    {
        public Result<ParsedSchedule> ResultToReturn { get; set; } = Result<ParsedSchedule>.Success(
            new ParsedSchedule([], [], []));

        public bool WasCalled { get; private set; }

        public Result<ParsedSchedule> Parse(Stream content, Guid tenantId, Guid projectId)
        {
            WasCalled = true;
            return ResultToReturn;
        }
    }

    private sealed class FakeImportOptionsProvider(long maxFileSizeBytes) : IImportOptionsProvider
    {
        public long MaxFileSizeBytes { get; } = maxFileSizeBytes;

        public long MaxDecompressedSizeBytes => long.MaxValue;

        public long MaxEntityCount => long.MaxValue;
    }

    // S3-SEC-01 DoD item 2: minimal leading bytes that satisfy FileSignatureValidator for each
    // format, so the handler's magic-byte gate doesn't reject a stub-parser test's placeholder
    // content before it ever reaches the stub - these tests are about handler orchestration, not
    // real file structure, so the bytes need only be format-plausible, not fully well-formed.
    private static byte[] MagicBytesFor(ImportFileFormat format) =>
        format == ImportFileFormat.Xer
            ? "%T\tPROJWBS"u8.ToArray()
            : "<Project></Project>"u8.ToArray();

    private sealed class FakeTenantProvider(Guid tenantId) : ITenantProvider
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class FakeCurrentUserContext(Guid? userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
    }

    private sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static ImportScheduleFileCommandHandler CreateHandler(
        FakeImportRepository repository, IXerScheduleParser xerParser, IMspdiScheduleParser mspdiParser, long maxFileSizeBytes = 1024) =>
        new(
            repository, xerParser, mspdiParser, new FakeImportOptionsProvider(maxFileSizeBytes),
            new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(Guid.NewGuid()),
            new FakeClock(DateTimeOffset.Parse("2026-07-28T00:00:00+00:00")));

    // S3-BE-01/02 DoD both require "a file exceeding the size cap is rejected before parsing
    // begins" - parametrized over both formats the handler dispatches between (Xer/Mspdi), not
    // just Xer, since the two formats resolve to genuinely different IXerScheduleParser/
    // IMspdiScheduleParser instances below the shared size-cap check; proving the check fires
    // (and the corresponding format-specific parser is never invoked) for only one format would
    // leave the other format's wiring unverified.
    [Theory]
    [InlineData(ImportFileFormat.Xer, "big.xer")]
    [InlineData(ImportFileFormat.Mspdi, "big.xml")]
    public async Task An_Oversized_File_Is_Rejected_Before_The_Parser_Is_Ever_Invoked(ImportFileFormat format, string fileName)
    {
        var repository = new FakeImportRepository();
        var parserThatMustNotRun = new ParserMustNotBeCalled();
        var handler = CreateHandler(repository, parserThatMustNotRun, parserThatMustNotRun, maxFileSizeBytes: 10);

        var command = new ImportScheduleFileCommand(Guid.NewGuid(), format, fileName, new byte[11]);

        // ParserMustNotBeCalled.Parse throws if reached - the assertion IS that this does not throw.
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportJobStatus.Failed, result.Value.Status);
        Assert.Contains(ImportErrorCodes.FileTooLarge, result.Value.ErrorJson);
        Assert.NotNull(repository.SavedFailedJob);
        Assert.Null(repository.SavedSuccessfulImport);
    }

    [Theory]
    [InlineData(ImportFileFormat.Xer, "exact.xer")]
    [InlineData(ImportFileFormat.Mspdi, "exact.xml")]
    public async Task A_File_At_Exactly_The_Cap_Is_Not_Rejected_For_Size(ImportFileFormat format, string fileName)
    {
        var repository = new FakeImportRepository();
        var parser = new StubParser();
        // Exactly 10 bytes, format-plausible leading bytes padded to the cap - proves the size check
        // (at the cap, not over it) AND the magic-byte gate can both pass on the same content.
        var content = format == ImportFileFormat.Xer ? "%Txxxxxxxx"u8.ToArray() : "<xxxxxxxxx"u8.ToArray();
        var handler = CreateHandler(repository, parser, parser, maxFileSizeBytes: content.Length);

        var command = new ImportScheduleFileCommand(Guid.NewGuid(), format, fileName, content);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(parser.WasCalled);
        Assert.Equal(ImportJobStatus.Succeeded, result.Value.Status);
    }

    [Fact]
    public async Task A_Relation_Cycle_Failure_From_The_Parser_Produces_A_Failed_Job_Not_An_Exception()
    {
        var repository = new FakeImportRepository();
        var parser = new StubParser
        {
            ResultToReturn = Result<ParsedSchedule>.Failure($"{ImportErrorCodes.RelationCycleDetected}: A1010 -> A1020 -> A1010"),
        };
        var handler = CreateHandler(repository, parser, parser);

        var result = await handler.Handle(
            new ImportScheduleFileCommand(Guid.NewGuid(), ImportFileFormat.Xer, "cycle.xer", MagicBytesFor(ImportFileFormat.Xer)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportJobStatus.Failed, result.Value.Status);
        Assert.Contains(ImportErrorCodes.RelationCycleDetected, result.Value.ErrorJson);
        Assert.Contains("A1010 -> A1020 -> A1010", result.Value.ErrorJson);
        Assert.Null(repository.SavedSuccessfulImport); // all-or-nothing: nothing was ever handed to the bulk-persist path.
    }

    [Fact]
    public async Task An_Unknown_Project_Is_A_Result_Failure_Not_A_Job()
    {
        var repository = new FakeImportRepository { ProjectExists = false };
        var parser = new StubParser();
        var handler = CreateHandler(repository, parser, parser);

        var result = await handler.Handle(
            new ImportScheduleFileCommand(Guid.NewGuid(), ImportFileFormat.Xer, "x.xer", [1]), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ImportErrorCodes.ProjectNotFound, result.Error);
        Assert.Null(repository.SavedFailedJob);
        Assert.False(parser.WasCalled);
    }

    [Fact]
    public async Task A_Successful_Parse_Marks_The_Job_Succeeded_With_The_Parsed_Row_Count()
    {
        var repository = new FakeImportRepository();
        var wbsNode = new WBSNode(Guid.NewGuid(), Guid.NewGuid(), "C1", "Civil", 0m);
        var activity = new Activity(
            Guid.NewGuid(), wbsNode.Id, "A1010", "Excavation",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(5), 5, 100_000m);
        var parser = new StubParser
        {
            ResultToReturn = Result<ParsedSchedule>.Success(new ParsedSchedule([wbsNode], [activity], [])),
        };
        var handler = CreateHandler(repository, parser, parser);

        var result = await handler.Handle(
            new ImportScheduleFileCommand(Guid.NewGuid(), ImportFileFormat.Xer, "ok.xer", MagicBytesFor(ImportFileFormat.Xer)), CancellationToken.None);

        Assert.Equal(ImportJobStatus.Succeeded, result.Value.Status);
        Assert.Equal(2, result.Value.RowsImported); // 1 WBS node + 1 activity
        Assert.NotNull(repository.SavedSuccessfulImport);
    }

    // ------------------------------------------------------------------------------------
    // S3-SEC-01 DoD item 2 (magic-byte / content-type check) - one test per "disguised" format
    // posted to each of the other two routes this handler serves.
    // ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ImportFileFormat.Xer, "schedule.xer")] // an MSPDI-shaped file posted to the xer route.
    [InlineData(ImportFileFormat.Mspdi, "schedule.xml")] // an XER-shaped file posted to the mspdi route.
    public async Task A_File_Whose_Content_Does_Not_Match_The_Other_Text_Format_Is_Rejected_Before_The_Parser_Runs(
        ImportFileFormat format, string fileName)
    {
        var repository = new FakeImportRepository();
        var parserThatMustNotRun = new ParserMustNotBeCalled();
        var handler = CreateHandler(repository, parserThatMustNotRun, parserThatMustNotRun);

        // The OTHER text format's magic bytes - e.g. Mspdi content posted as if it were Xer.
        var disguisedContent = format == ImportFileFormat.Xer ? MagicBytesFor(ImportFileFormat.Mspdi) : MagicBytesFor(ImportFileFormat.Xer);

        var result = await handler.Handle(
            new ImportScheduleFileCommand(Guid.NewGuid(), format, fileName, disguisedContent), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportJobStatus.Failed, result.Value.Status);
        Assert.Contains(ImportErrorCodes.FormatMismatch, result.Value.ErrorJson);
    }

    [Theory]
    [InlineData(ImportFileFormat.Xer, "schedule.xer")]
    [InlineData(ImportFileFormat.Mspdi, "schedule.xml")]
    public async Task An_Xlsx_Shaped_File_Posted_To_The_Schedule_Routes_Is_Rejected_Before_The_Parser_Runs(
        ImportFileFormat format, string fileName)
    {
        var repository = new FakeImportRepository();
        var parserThatMustNotRun = new ParserMustNotBeCalled();
        var handler = CreateHandler(repository, parserThatMustNotRun, parserThatMustNotRun);

        // XLSX's ZIP local-file-header signature - matches neither IsXer nor IsMspdi.
        byte[] xlsxMagicBytes = [0x50, 0x4B, 0x03, 0x04];

        var result = await handler.Handle(
            new ImportScheduleFileCommand(Guid.NewGuid(), format, fileName, xlsxMagicBytes), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportJobStatus.Failed, result.Value.Status);
        Assert.Contains(ImportErrorCodes.FormatMismatch, result.Value.ErrorJson);
    }

    // ------------------------------------------------------------------------------------
    // S3-SEC-01 M-01 layer 2: an unforeseen parser exception is a safety-netted Failed job, never
    // an unhandled 500, and never echoes the exception's own message into ErrorJson.
    // ------------------------------------------------------------------------------------

    private sealed class ThrowingParser : IXerScheduleParser, IMspdiScheduleParser
    {
        public Result<ParsedSchedule> Parse(Stream content, Guid tenantId, Guid projectId) =>
            throw new InvalidOperationException("some internal detail that must never reach the client");
    }

    [Fact]
    public async Task An_Unforeseen_Parser_Exception_Produces_A_Failed_Job_Never_A_Raw_Exception()
    {
        var repository = new FakeImportRepository();
        var parser = new ThrowingParser();
        var handler = CreateHandler(repository, parser, parser);

        var result = await handler.Handle(
            new ImportScheduleFileCommand(Guid.NewGuid(), ImportFileFormat.Xer, "boom.xer", MagicBytesFor(ImportFileFormat.Xer)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ImportJobStatus.Failed, result.Value.Status);
        Assert.Contains(ImportErrorCodes.MalformedFile, result.Value.ErrorJson);
        Assert.DoesNotContain("some internal detail", result.Value.ErrorJson);
        Assert.NotNull(repository.SavedFailedJob);
    }
}
