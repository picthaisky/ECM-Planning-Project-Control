using CMPlus.Application.Services.Manpower;

namespace CMPlus.Application.Features.Manpower.Queries.GetProductivityIndex;

/// <summary>
/// Wire shape combining <see cref="ProductivityIndexResult"/> and (when the query window is exactly
/// one calendar day) <see cref="ManningRatioResult"/> - deliberately under two distinct field names,
/// <see cref="ProductivityIndex"/>/<see cref="ProductivityIndexNullReason"/> and
/// <see cref="ManningRatio"/>, never merged (domain-rules.md §5.1's ruling; fixture M-02: "the
/// response contains productivityIndex = 0.60 and manningRatio = 1.25 under distinct names; ... no
/// field named productivityIndex ever equals 1.25 for this input").
/// </summary>
public sealed record ProductivityIndexResponseDto(
    Guid ProjectId,
    Guid? WbsNodeId,
    Guid? ActivityId,
    DateTimeOffset? From,
    DateTimeOffset To,
    decimal? ProductivityIndex,
    PiNullReason? ProductivityIndexNullReason,
    decimal EarnedManHours,
    decimal ActualManHoursInScope,
    decimal ActualManHoursTotal,
    decimal ExcludedManHours,
    decimal CoveragePercentage,
    int LogEntryCount,
    IReadOnlyList<PiDataQualityWarning> Warnings,
    decimal? ManningRatio,
    int? ActualWorkerCount,
    int? PlannedWorkerCount)
{
    public static ProductivityIndexResponseDto Create(
        Guid projectId, Guid? wbsNodeId, Guid? activityId, DateTimeOffset? from, DateTimeOffset to,
        ProductivityIndexResult pi, ManningRatioResult? manning) => new(
        projectId, wbsNodeId, activityId, from, to,
        pi.Value, pi.Reason, pi.EarnedManHours, pi.ActualManHoursInScope, pi.ActualManHoursTotal,
        pi.ExcludedManHours, pi.CoveragePercentage, pi.LogEntryCount, pi.Warnings,
        manning?.Value, manning?.ActualWorkerCount, manning?.PlannedWorkerCount);
}
