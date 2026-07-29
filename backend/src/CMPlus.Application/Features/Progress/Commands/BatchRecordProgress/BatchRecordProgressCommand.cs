using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Progress.Commands.BatchRecordProgress;

/// <summary>One activity's new progress within a batch submission (US-4.5).</summary>
public sealed record BatchProgressEntry(Guid ActivityId, decimal ProgressPercentage, decimal? ActualQuantity);

/// <summary>
/// S4-BE-03 (US-4.5): the WBS &amp; Activity screen's "Update Progress" grid batch submission - many
/// (Activity, new %) pairs sharing one caller-supplied <see cref="PeriodEndDate"/> (the reporting
/// cutoff the site engineer selected), saved as one all-or-nothing transaction with exactly one
/// summarizing <c>AuditLog</c> entry. Every entry is written through <c>Activity.RecordProgress</c>
/// (ADR-0009) - never a direct cache write. The client-side "confirm before accepting a regression"
/// gate (US-4.5) is a frontend UX concern (S4-FE-03) - this command always executes exactly the
/// batch it is given.
/// </summary>
public sealed record BatchRecordProgressCommand(
    Guid ProjectId,
    DateTimeOffset PeriodEndDate,
    IReadOnlyList<BatchProgressEntry> Entries) : IRequest<Result<BatchRecordProgressResultDto>>;
