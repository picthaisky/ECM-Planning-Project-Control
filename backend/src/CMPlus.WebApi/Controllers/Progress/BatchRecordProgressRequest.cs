namespace CMPlus.WebApi.Controllers.Progress;

/// <summary>Request body for <c>POST /api/v1/projects/{projectId}/progress/batch</c> (S4-BE-03).</summary>
public sealed record BatchRecordProgressRequest(
    DateTimeOffset PeriodEndDate,
    IReadOnlyList<BatchRecordProgressEntryRequest> Entries);

public sealed record BatchRecordProgressEntryRequest(Guid ActivityId, decimal ProgressPercentage, decimal? ActualQuantity);
