using CMPlus.Domain.Entities;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Persistence boundary for editing a <see cref="Project"/>'s master data (S4-BE-02). Deliberately
/// exposes only <see cref="Project"/> - no other aggregate is reachable through this interface -
/// which is what makes "changing a rate never retroactively touches an already-issued Payment
/// Certificate" true by construction for <c>UpdateProjectCommand</c>, not merely by convention
/// (there is nothing else this repository lets a handler reach).
/// </summary>
public interface IProjectRepository
{
    /// <summary>Tracked (not <c>AsNoTracking</c>) - the caller mutates the returned instance via its
    /// domain setters and this same instance is what <see cref="SaveChangesAsync"/> persists.
    /// Tenant-scoped by the global EF query filter (ADR-0002) - a project id belonging to another
    /// tenant is indistinguishable from "does not exist".</summary>
    Task<Project?> FindAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A single <c>Project</c> entity change - the default one-row-per-changed-entity
    /// <c>AuditSaveChangesInterceptor</c> behaviour is exactly right here (not the S3-BE-04 bulk
    /// escape hatch), producing one <c>AuditLog</c> row with the old/new scalar values (S4-BE-02
    /// DoD).
    ///
    /// <para>Returns <c>false</c> on a concurrency conflict rather than throwing, matching
    /// <c>IVariationOrderRepository</c>/<c>IPaymentCertificateRepository</c>'s existing
    /// <c>TrySaveChangesAsync</c> shape. This became load-bearing when
    /// <see cref="Project.RowVersion"/> was added to close S10-SEC-01 finding H-03: before the
    /// token existed a concurrent edit silently lost one write, and afterwards an uncaught
    /// <c>DbUpdateConcurrencyException</c> would have surfaced as a 500. Neither is acceptable for
    /// an ordinary <c>PUT /api/v1/projects/{id}</c>, so the conflict is translated to a 409 and the
    /// caller retries against fresh state.</para>
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default);
}
