using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.ActualCosts.Commands.RecordActualCost;

/// <summary>
/// A single, manually-entered <c>ActualCostEntry</c> posting (actual-cost.md §9/§12, ADR-0013) -
/// the minimum write path this task builds. Excel/CSV import and the QS grid UI are explicitly
/// later work (Sprint 9+); every entry produced by this command carries
/// <c>Source = ActualCostSource.ManualEntry</c>, decided by the handler rather than the caller, the
/// same server-authority discipline <c>TenantId</c>/<c>PostedAt</c>/<c>PostedByUserId</c> already
/// get - a client cannot claim its own manually-typed entry came from an ERP/import feed.
///
/// <para><see cref="IncurredDate"/> is the only date on this command - the valid-time business
/// fact a QS/PM actually knows and enters (actual-cost.md §6.1). <c>PostedAt</c> (transaction time)
/// and <c>PostedByUserId</c> are resolved server-side by the handler from
/// <c>IDateTimeProvider</c>/<c>ICurrentUserContext</c>, never trusted from the request, mirroring
/// every other write path in this codebase (e.g. <c>BatchRecordProgressCommandHandler</c>'s
/// <c>RecordedAt</c>/actor resolution).</para>
/// </summary>
public sealed record RecordActualCostCommand(
    Guid ProjectId,
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
    string? UnitOfMeasure) : IRequest<Result<ActualCostEntryDto>>;
