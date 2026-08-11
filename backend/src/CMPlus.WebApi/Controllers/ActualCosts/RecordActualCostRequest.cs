using CMPlus.Domain.Enums;

namespace CMPlus.WebApi.Controllers.ActualCosts;

/// <summary>Request body for <c>POST /api/v1/projects/{projectId}/actual-costs</c> (actual-cost.md
/// §9/§12). <c>Source</c> is deliberately not a request field - the handler always stamps
/// <c>ManualEntry</c> for this endpoint (see <c>RecordActualCostCommand</c>'s remarks).</summary>
public sealed record RecordActualCostRequest(
    Guid? WbsNodeId,
    Guid? ActivityId,
    CostCategory CostCategory,
    ActualCostEntryType EntryType,
    decimal Amount,
    DateTimeOffset IncurredDate,
    Guid? ReversesEntryId,
    string? DocumentReference,
    string? CostCode,
    string? VendorName,
    string? Note,
    DateTimeOffset? PaidDate,
    decimal? Quantity,
    string? UnitOfMeasure);
