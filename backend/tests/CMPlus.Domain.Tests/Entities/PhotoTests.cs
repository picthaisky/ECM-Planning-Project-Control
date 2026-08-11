using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>S12-BE-01 (US-12.1): construction, validation, and the server-derived
/// <see cref="Photo.StorageKey"/>/<see cref="Photo.ContentType"/> invariants.</summary>
public class PhotoTests
{
    private static Photo CreatePhoto(
        Guid? tenantId = null,
        Guid? projectId = null,
        Guid? activityId = null,
        string? caption = "โซน B ชั้น 3 เทคอนกรีตพื้นแล้วเสร็จ",
        PhotoImageFormat format = PhotoImageFormat.Jpeg,
        long fileSizeBytes = 12_345,
        Guid? uploadedByUserId = null,
        DateTimeOffset? uploadedAt = null,
        DateTimeOffset? capturedAt = null) =>
        new(
            tenantId ?? Guid.NewGuid(),
            projectId ?? Guid.NewGuid(),
            activityId,
            caption,
            format,
            fileSizeBytes,
            uploadedByUserId ?? Guid.NewGuid(),
            uploadedAt ?? DateTimeOffset.Parse("2026-08-10T09:00:00+07:00"),
            capturedAt);

    [Fact]
    public void Constructor_Assigns_All_Fields()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var uploadedByUserId = Guid.NewGuid();
        var uploadedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");

        var photo = new Photo(
            tenantId, projectId, activityId, "หล่อเสาโครงสร้าง C4", PhotoImageFormat.Jpeg, 500_000,
            uploadedByUserId, uploadedAt, capturedAt: null);

        Assert.Equal(tenantId, photo.TenantId);
        Assert.Equal(projectId, photo.ProjectId);
        Assert.Equal(activityId, photo.ActivityId);
        Assert.Equal("หล่อเสาโครงสร้าง C4", photo.Caption);
        Assert.Equal(PhotoImageFormat.Jpeg, photo.Format);
        Assert.Equal(500_000, photo.FileSizeBytes);
        Assert.Equal(uploadedByUserId, photo.UploadedByUserId);
        Assert.Equal(uploadedAt, photo.UploadedAt);
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_ProjectId()
    {
        Assert.Throws<DomainException>(() => CreatePhoto(projectId: Guid.Empty));
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_UploadedByUserId()
    {
        Assert.Throws<DomainException>(() => CreatePhoto(uploadedByUserId: Guid.Empty));
    }

    [Fact]
    public void Constructor_Rejects_An_Empty_Guid_ActivityId_When_Supplied()
    {
        // Guid.Empty is not the same thing as "no activity" (null) - a caller that means "none"
        // must pass null, never Guid.Empty (mirrors this codebase's general "no ?? Guid.Empty
        // fabrication" discipline, applied here to an optional FK rather than an actor id).
        Assert.Throws<DomainException>(() => CreatePhoto(activityId: Guid.Empty));
    }

    [Fact]
    public void Constructor_Rejects_A_Non_Positive_FileSizeBytes()
    {
        Assert.Throws<DomainException>(() => CreatePhoto(fileSizeBytes: 0));
        Assert.Throws<DomainException>(() => CreatePhoto(fileSizeBytes: -1));
    }

    [Fact]
    public void Constructor_Rejects_A_Caption_Over_500_Characters()
    {
        Assert.Throws<DomainException>(() => CreatePhoto(caption: new string('ก', 501)));
    }

    [Fact]
    public void Constructor_Trims_Whitespace_And_Normalizes_A_Blank_Caption_To_Null()
    {
        Assert.Equal("มีช่องว่างรอบข้อความ", CreatePhoto(caption: "  มีช่องว่างรอบข้อความ  ").Caption);
        Assert.Null(CreatePhoto(caption: "   ").Caption);
        Assert.Null(CreatePhoto(caption: null).Caption);
    }

    [Fact]
    public void CapturedAt_Defaults_To_UploadedAt_When_Not_Supplied()
    {
        var uploadedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");
        var photo = CreatePhoto(uploadedAt: uploadedAt, capturedAt: null);

        Assert.Equal(uploadedAt, photo.CapturedAt);
    }

    [Fact]
    public void CapturedAt_Is_Preserved_When_It_Differs_From_UploadedAt_Offline_Capture_Synced_Later()
    {
        // ADR-0005: an offline capture's real capture time can legitimately predate the sync/upload
        // time by hours/days - the same LogDate-vs-RecordedAt separation DailyWeatherLog uses.
        var capturedAt = DateTimeOffset.Parse("2026-08-08T07:30:00+07:00");
        var uploadedAt = DateTimeOffset.Parse("2026-08-10T09:00:00+07:00");
        var photo = CreatePhoto(capturedAt: capturedAt, uploadedAt: uploadedAt);

        Assert.Equal(capturedAt, photo.CapturedAt);
        Assert.NotEqual(uploadedAt, photo.CapturedAt);
    }

    // ------------------------------------------------------------------------------------
    // StorageKey/ContentType: server-derived only, never a caller-supplied value (S12-SEC-01) -
    // see Photo's own class remarks.
    // ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(PhotoImageFormat.Jpeg, "image/jpeg", ".jpg")]
    [InlineData(PhotoImageFormat.Png, "image/png", ".png")]
    public void StorageKey_And_ContentType_Are_Derived_From_Format_Never_Accepted_As_Input(
        PhotoImageFormat format, string expectedContentType, string expectedExtension)
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var photo = new Photo(tenantId, projectId, null, null, format, 1, Guid.NewGuid(), DateTimeOffset.UtcNow, null);

        Assert.Equal(expectedContentType, photo.ContentType);
        Assert.StartsWith($"{tenantId:N}/{projectId:N}/{photo.Id:N}", photo.StorageKey, StringComparison.Ordinal);
        Assert.EndsWith(expectedExtension, photo.StorageKey, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageKey_Is_Unique_Per_Instance_Even_With_Otherwise_Identical_Inputs()
    {
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var uploadedByUserId = Guid.NewGuid();
        var uploadedAt = DateTimeOffset.UtcNow;

        var first = new Photo(tenantId, projectId, null, null, PhotoImageFormat.Jpeg, 1, uploadedByUserId, uploadedAt, null);
        var second = new Photo(tenantId, projectId, null, null, PhotoImageFormat.Jpeg, 1, uploadedByUserId, uploadedAt, null);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.StorageKey, second.StorageKey);
    }
}
