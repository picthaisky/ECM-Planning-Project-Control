using CMPlus.Application.Features.Photos;
using CMPlus.Application.Features.Photos.Commands.UploadPhoto;
using CMPlus.Application.Tests.Photos;

namespace CMPlus.Application.Tests.Features.Photos;

public class UploadPhotoCommandHandlerTests
{
    private static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    private static (UploadPhotoCommandHandler Handler, FakePhotoRepository Repository, FakeFileStorage Storage) CreateHandler(
        Guid? tenantId = null, Guid? userId = null, DateTimeOffset? now = null, long maxFileSizeBytes = 10 * 1024 * 1024)
    {
        var repository = new FakePhotoRepository();
        var storage = new FakeFileStorage();
        var handler = new UploadPhotoCommandHandler(
            repository,
            new FakePhotoOptionsProvider(maxFileSizeBytes),
            storage,
            new FakeTenantProvider(tenantId ?? Guid.NewGuid()),
            new FakeCurrentUserContext(userId ?? Guid.NewGuid()),
            new FakeClock(now ?? DateTimeOffset.UtcNow));

        return (handler, repository, storage);
    }

    private static UploadPhotoCommand DefaultCommand(
        Guid? projectId = null, Guid? activityId = null, string? caption = "โซน B", byte[]? content = null) =>
        new(
            projectId ?? Guid.NewGuid(),
            activityId,
            caption,
            CapturedAt: null,
            content ?? PhotoImageFixtures.JpegWithNoMetadata());

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var (handler, repository, storage) = CreateHandler();
        repository.ProjectExists = false;

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.ProjectNotFound, result.Error);
        Assert.Empty(repository.Added);
        Assert.Empty(storage.SavedContent);
    }

    [Fact]
    public async Task Handle_Fails_Closed_With_ActorRequired_On_A_Null_UserId_Never_Fabricates_Guid_Empty()
    {
        // Constructed directly (not via CreateHandler) so the explicit null cannot be masked by a
        // "generate one if absent" fallback - same discipline as RecordWeatherLogCommandHandlerTests.
        var repository = new FakePhotoRepository();
        var storage = new FakeFileStorage();
        var handler = new UploadPhotoCommandHandler(
            repository, new FakePhotoOptionsProvider(), storage,
            new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(null), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.ActorRequired, result.Error);
        Assert.Empty(repository.Added);
        Assert.Empty(storage.SavedContent);
    }

    [Fact]
    public async Task Handle_Returns_UnknownActivity_When_ActivityId_Does_Not_Belong_To_The_Project()
    {
        var (handler, repository, _) = CreateHandler();

        var result = await handler.Handle(DefaultCommand(activityId: Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.UnknownActivity, result.Error);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Handle_Succeeds_When_ActivityId_Is_A_Real_Activity_In_This_Project()
    {
        var (handler, repository, _) = CreateHandler();
        var activityId = Guid.NewGuid();
        repository.ExistingActivityIds = [activityId];

        var result = await handler.Handle(DefaultCommand(activityId: activityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(activityId, result.Value.ActivityId);
    }

    [Fact]
    public async Task Handle_Returns_FileRequired_For_Empty_Content()
    {
        var (handler, repository, _) = CreateHandler();

        var result = await handler.Handle(DefaultCommand(content: []), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.FileRequired, result.Error);
        Assert.Empty(repository.Added);
    }

    // ------------------------------------------------------------------------------------
    // Mutation-evidence case #1 (this task's brief, verbatim): the oversize limit trips - and,
    // specifically, trips on the DECLARED length even when the buffer handed to the handler is a
    // tiny placeholder (the shape UploadPhotoCommand carries when the controller already decided not
    // to buffer the real oversized bytes) - so a handler regression that started trusting
    // Content.LongLength instead of DeclaredContentLength would silently let an oversized upload
    // through and this test would catch it.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Returns_FileTooLarge_When_The_Declared_Length_Exceeds_The_Cap_Even_With_A_Tiny_Placeholder_Buffer()
    {
        var (handler, repository, storage) = CreateHandler(maxFileSizeBytes: 1_000);
        var command = new UploadPhotoCommand(
            Guid.NewGuid(), null, null, null, Content: [0], DeclaredContentLength: 5_000);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.FileTooLarge, result.Error);
        Assert.Empty(repository.Added);
        Assert.Empty(storage.SavedContent);
    }

    [Fact]
    public async Task Handle_Returns_FileTooLarge_When_The_Actual_Content_Exceeds_The_Cap_And_No_Declared_Length_Was_Given()
    {
        var (handler, repository, _) = CreateHandler(maxFileSizeBytes: 10);
        var oversized = PhotoImageFixtures.JpegWithNoMetadata(); // well over 10 bytes.

        var result = await handler.Handle(DefaultCommand(content: oversized), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.FileTooLarge, result.Error);
        Assert.Empty(repository.Added);
    }

    // ------------------------------------------------------------------------------------
    // L-01 (security review sprint-12.md): the exact opposite of the case above - a SMALL/lying
    // DeclaredContentLength must not mask a genuinely oversized actual buffer. Pre-fix, the check
    // was `DeclaredContentLength ?? Content.LongLength`, so a caller-supplied "5 bytes" hid an 11
    // MiB `Content` array from the cap entirely. Math.Max(declared, actual) closes that.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Returns_FileTooLarge_When_The_Actual_Content_Exceeds_The_Cap_Even_Though_The_Declared_Length_Understates_It()
    {
        var (handler, repository, storage) = CreateHandler(maxFileSizeBytes: 10);
        var oversized = PhotoImageFixtures.JpegWithNoMetadata(); // well over 10 bytes.
        var command = new UploadPhotoCommand(
            Guid.NewGuid(), null, null, null, Content: oversized, DeclaredContentLength: 5);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.FileTooLarge, result.Error);
        Assert.Empty(repository.Added);
        Assert.Empty(storage.SavedContent);
    }

    // ------------------------------------------------------------------------------------
    // Mutation-evidence case #2: a file whose magic bytes disagree with what it claims to be is
    // rejected. UploadPhotoCommand carries no filename/Content-Type field at all, so this is
    // necessarily proven purely from content.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Returns_UnsupportedFormat_For_Content_Whose_Magic_Bytes_Are_Not_Jpeg_Or_Png()
    {
        var (handler, repository, storage) = CreateHandler();
        var htmlDisguisedAsPhoto = Utf8("<html><body><script>alert(1)</script></body></html>");

        var result = await handler.Handle(DefaultCommand(content: htmlDisguisedAsPhoto), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.UnsupportedFormat, result.Error);
        Assert.Empty(repository.Added);
        Assert.Empty(storage.SavedContent);
    }

    [Fact]
    public async Task Handle_Returns_MalformedImage_When_The_Structure_Cannot_Be_Safely_Parsed_Fails_Closed()
    {
        var (handler, repository, storage) = CreateHandler();
        // Valid JPEG magic bytes, but a truncated/corrupt segment structure beyond that.
        byte[] corrupt = [0xFF, 0xD8, 0xFF, 0xE1, 0xFF, 0xFF, 0x00];

        var result = await handler.Handle(DefaultCommand(content: corrupt), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PhotoErrorCodes.MalformedImage, result.Error);
        Assert.Empty(repository.Added);
        Assert.Empty(storage.SavedContent);
    }

    // ------------------------------------------------------------------------------------
    // Mutation-evidence case #3 (this task's brief, verbatim): EXIF GPS is genuinely gone from
    // WHAT WAS STORED - asserted against FakeFileStorage's recorded bytes, i.e. exactly what a real
    // IFileStorage.SaveAsync call received, not merely that ExifScrubber was invoked.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_Stores_Content_With_Gps_And_Device_Markers_Genuinely_Removed()
    {
        var (handler, repository, storage) = CreateHandler();
        var original = PhotoImageFixtures.JpegWithGpsExifAndDeviceInfo(out var gpsMarker, out var deviceMarker, out _);

        var result = await handler.Handle(DefaultCommand(content: original), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var photo = Assert.Single(repository.Added);
        var stored = storage.SavedContent[photo.StorageKey];

        // The original, un-scrubbed bytes were never even handed to storage under any key.
        Assert.DoesNotContain(storage.SavedContent.Values, v => v.SequenceEqual(original));

        var storedText = System.Text.Encoding.ASCII.GetString(stored);
        Assert.DoesNotContain(gpsMarker, storedText, StringComparison.Ordinal);
        Assert.DoesNotContain(deviceMarker, storedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Exif", storedText, StringComparison.Ordinal);

        // The DB row's own FileSizeBytes matches the STORED (scrubbed) bytes, not the original
        // upload's size - Photo's own documented contract.
        Assert.Equal(stored.LongLength, photo.FileSizeBytes);
    }

    [Fact]
    public async Task Handle_Saves_Under_The_Photos_Own_Server_Derived_StorageKey_With_The_Correct_ContentType()
    {
        var (handler, repository, storage) = CreateHandler();

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var photo = Assert.Single(repository.Added);
        Assert.True(storage.SavedContent.ContainsKey(photo.StorageKey));
        Assert.Equal("image/jpeg", storage.SavedContentTypes[photo.StorageKey]);
        Assert.Equal("image/jpeg", result.Value.ContentType);
    }

    [Fact]
    public async Task Handle_Persists_A_Photo_Stamped_With_Server_Side_Tenant_UploadedAt_And_UploadedBy()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");
        var projectId = Guid.NewGuid();
        var (handler, repository, _) = CreateHandler(tenantId, userId, now);

        var result = await handler.Handle(DefaultCommand(projectId: projectId, caption: "หล่อเสา C4"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var photo = Assert.Single(repository.Added);
        Assert.Equal(tenantId, photo.TenantId);
        Assert.Equal(projectId, photo.ProjectId);
        Assert.Equal(now, photo.UploadedAt);
        Assert.Equal(now, photo.CapturedAt); // defaults to UploadedAt when not supplied.
        Assert.Equal(userId, photo.UploadedByUserId);
        Assert.Equal("หล่อเสา C4", photo.Caption);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_Preserves_A_Client_Supplied_CapturedAt_Distinct_From_UploadedAt()
    {
        var uploadedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");
        var capturedAt = DateTimeOffset.Parse("2026-08-08T07:30:00+07:00"); // offline capture, synced later.
        var (handler, repository, _) = CreateHandler(now: uploadedAt);

        var command = DefaultCommand() with { CapturedAt = capturedAt };
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var photo = Assert.Single(repository.Added);
        Assert.Equal(capturedAt, photo.CapturedAt);
        Assert.Equal(uploadedAt, photo.UploadedAt);
    }
}
