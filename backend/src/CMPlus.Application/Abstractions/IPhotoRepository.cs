using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for <c>UploadPhotoCommand</c>/<c>GetPhotoContentQuery</c> (S12-BE-01,
/// US-12.1). Every existence check is <b>project</b>-scoped, not merely tenant-scoped - mirrors
/// <see cref="IDailyWeatherLogRepository"/>'s own discipline (an <c>ActivityId</c> belonging to a
/// different project in the same tenant must be rejected, never silently accepted). Tenant scoping
/// itself still comes from <c>CmPlusDbContext</c>'s global query filter (ADR-0002).
/// </summary>
public interface IPhotoRepository
{
    Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>True iff <paramref name="activityId"/> is a real <see cref="Activity"/> belonging to
    /// <paramref name="projectId"/> (and, via the ambient tenant filter, this tenant).</summary>
    Task<bool> ActivityExistsAsync(Guid projectId, Guid activityId, CancellationToken cancellationToken = default);

    void Add(Photo photo);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Tenant-scoped by the global EF query filter (ADR-0002) - a cross-tenant or unknown
    /// id is indistinguishable, both simply resolve to <see langword="null"/>. The caller
    /// additionally checks <see cref="Photo.ProjectId"/> against the route's <c>projectId</c>
    /// (S12-SEC-01: "photo URLs must not be guessable in a way that permits cross-tenant access" -
    /// this closes the narrower same-tenant-wrong-project guess too, at no extra cost since the
    /// route already carries a project id).</summary>
    Task<Photo?> FindAsync(Guid photoId, CancellationToken cancellationToken = default);
}
