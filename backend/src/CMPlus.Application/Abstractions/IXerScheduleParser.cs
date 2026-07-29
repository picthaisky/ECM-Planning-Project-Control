using CMPlus.Application.Import;
using CMPlus.Domain.Common;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Parses a Primavera P6 <c>.XER</c> export (tab-delimited, <c>%T</c>/<c>%F</c>/<c>%R</c> record
/// tags) into <see cref="ParsedSchedule"/> (S3-BE-01, US-3.1). Implemented in
/// <c>CMPlus.Infrastructure.Parsers.Xer</c> - Application only ever depends on this interface, per
/// ADR-0001. A relation cycle or any structural malformation is a <see cref="Result{T}.Failure"/>,
/// never an entity that is partially constructed/persisted (S3-BE-01 DoD: all-or-nothing) - a
/// failure result here is guaranteed to carry zero entities to add to the change tracker.
/// </summary>
public interface IXerScheduleParser
{
    /// <summary>
    /// <paramref name="xerContent"/> must already have passed the caller's file-size-cap check
    /// (that check happens before this method is ever invoked - "rejected before parsing begins",
    /// S3-BE-01 DoD). <paramref name="tenantId"/>/<paramref name="projectId"/> are stamped onto the
    /// constructed entities for readability only; <c>CmPlusDbContext.SaveChanges</c> re-stamps
    /// <c>TenantId</c> unconditionally on insert regardless (ADR-0002).
    /// </summary>
    Result<ParsedSchedule> Parse(Stream xerContent, Guid tenantId, Guid projectId);
}
