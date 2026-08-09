using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.ActualCosts.Commands.RecordActualCost;

/// <summary>Response shape for a successfully recorded <see cref="ActualCostEntry"/> - lets the
/// caller render the just-posted row (and its id, for a future reversal's <c>ReversesEntryId</c>)
/// without a second round trip.</summary>
public sealed record ActualCostEntryDto(
    Guid Id,
    Guid ProjectId,
    Guid? WbsNodeId,
    Guid? ActivityId,
    CostCategory CostCategory,
    ActualCostEntryType EntryType,
    ActualCostSource Source,
    decimal Amount,
    DateTimeOffset IncurredDate,
    DateTimeOffset PostedAt,
    Guid PostedByUserId,
    Guid? ReversesEntryId,
    string? DocumentReference,
    string? CostCode,
    string? VendorName,
    string? Note,
    DateTimeOffset? PaidDate,
    decimal? Quantity,
    string? UnitOfMeasure)
{
    public static ActualCostEntryDto From(ActualCostEntry entry) => new(
        entry.Id,
        entry.ProjectId,
        entry.WbsNodeId,
        entry.ActivityId,
        entry.CostCategory,
        entry.EntryType,
        entry.Source,
        entry.Amount,
        entry.IncurredDate,
        entry.PostedAt,
        entry.PostedByUserId,
        entry.ReversesEntryId,
        entry.DocumentReference,
        entry.CostCode,
        entry.VendorName,
        entry.Note,
        entry.PaidDate,
        entry.Quantity,
        entry.UnitOfMeasure);
}
