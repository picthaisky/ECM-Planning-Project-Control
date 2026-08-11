using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// A site progress photo (S12-BE-01, US-12.1) - "captured/tagged to an Activity/Zone"
/// (backlog-detailed.md §Sprint 12 US-12.1). <see cref="ActivityId"/> is the only taggable concept
/// this codebase's domain model currently has for "Zone" (no separate Zone entity exists anywhere
/// in <c>backend/src</c>); a future dedicated Zone concept, if the product needs one, is additive.
///
/// <para><b>Security-critical invariant this constructor enforces: <see cref="StorageKey"/> is
/// never accepted as a parameter.</b> It is derived here, deterministically, from
/// <see cref="TenantId"/>/<see cref="ProjectId"/>/<see cref="Entity.Id"/>/<see cref="Format"/>
/// only - never from the caller-supplied original filename (which is not even captured anywhere
/// on this entity) and never from any other client input. This closes path traversal and
/// storage-key-collision/guessing at the source: there is no code path by which a request body
/// could influence where a photo lands on disk/in a bucket, or under what key it can later be
/// retrieved (S12-SEC-01's "photo URLs must not be guessable in a way that permits cross-tenant
/// access" - the actual boundary is ADR-0002's tenant query filter on <see cref="FindAsync"/>-style
/// reads, but a caller-influenced key would be a second, independent way to defeat it).</para>
///
/// <para><b>Content-Type is a computed projection of <see cref="Format"/>, never a stored free-text
/// value</b> - the same discipline <c>VariationOrder.Type</c> uses for its own derived sign
/// (domain-rules.md pattern). A serving endpoint that trusted an arbitrary stored string for
/// <c>Content-Type</c> would let a crafted upload claim e.g. <c>text/html</c> and become stored
/// XSS on this origin; because <see cref="Format"/> is a closed two-value enum populated only from
/// server-side magic-byte detection (<c>ImageSignatureValidator</c>), <see cref="ContentType"/> can
/// only ever be <c>image/jpeg</c> or <c>image/png</c>.</para>
/// </summary>
public sealed class Photo : Entity, ITenantOwned
{
    private const int MaxCaptionLength = 500;

    public Guid TenantId { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Optional tag to the schedule activity/zone this photo documents progress against
    /// (US-12.1). Existence-within-this-project is validated at the Application layer (a
    /// constructor cannot see other rows) - see <c>UploadPhotoCommandHandler</c>.</summary>
    public Guid? ActivityId { get; private set; }

    public string? Caption { get; private set; }

    public PhotoImageFormat Format { get; private set; }

    /// <summary>The EXIF-scrubbed content's exact length in bytes, as actually written to
    /// <c>IFileStorage</c> - deliberately the post-scrub size, not the original upload's size,
    /// since the original bytes are never persisted anywhere (S12-BE-01 DoD: "strip before
    /// storing", not "store then clean").</summary>
    public long FileSizeBytes { get; private set; }

    /// <summary>Server-derived storage key/path - see this type's class remarks. Opaque to every
    /// caller outside <see cref="Infrastructure"/>'s <c>IFileStorage</c> adapter; never returned to
    /// the client directly (the serve endpoint streams content through the API, it does not hand
    /// out this key - S12-BE-01 DoD: "serving must not be a static-file passthrough").</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public Guid UploadedByUserId { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>When the photo was actually taken on site, which may differ from
    /// <see cref="UploadedAt"/> for an offline capture synced later (ADR-0005) - same
    /// LogDate-vs-RecordedAt separation <see cref="DailyWeatherLog"/> already establishes. Defaults
    /// to <see cref="UploadedAt"/> when the client does not supply one.</summary>
    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>Computed, never stored - see this type's class remarks.</summary>
    public string ContentType => Format switch
    {
        PhotoImageFormat.Jpeg => "image/jpeg",
        PhotoImageFormat.Png => "image/png",
        _ => throw new DomainException($"Unknown PhotoImageFormat '{Format}'."),
    };

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private Photo()
    {
    }

    public Photo(
        Guid tenantId,
        Guid projectId,
        Guid? activityId,
        string? caption,
        PhotoImageFormat format,
        long fileSizeBytes,
        Guid uploadedByUserId,
        DateTimeOffset uploadedAt,
        DateTimeOffset? capturedAt)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("Photo.ProjectId is required.");
        }

        if (activityId == Guid.Empty)
        {
            throw new DomainException("Photo.ActivityId, when supplied, must not be Guid.Empty.");
        }

        if (uploadedByUserId == Guid.Empty)
        {
            throw new DomainException("Photo.UploadedByUserId is required.");
        }

        if (fileSizeBytes <= 0)
        {
            throw new DomainException("Photo.FileSizeBytes must be positive.");
        }

        if (caption is { Length: > MaxCaptionLength })
        {
            throw new DomainException($"Photo.Caption cannot exceed {MaxCaptionLength} characters.");
        }

        TenantId = tenantId;
        ProjectId = projectId;
        ActivityId = activityId;
        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        Format = format;
        FileSizeBytes = fileSizeBytes;
        UploadedByUserId = uploadedByUserId;
        UploadedAt = uploadedAt;
        CapturedAt = capturedAt ?? uploadedAt;

        // Id is already assigned by the base Entity() constructor by this point (it runs first),
        // so the key can be derived from it here rather than threaded in from outside - see this
        // type's class remarks.
        var extension = format == PhotoImageFormat.Jpeg ? "jpg" : "png";
        StorageKey = $"{tenantId:N}/{projectId:N}/{Id:N}.{extension}";
    }
}
