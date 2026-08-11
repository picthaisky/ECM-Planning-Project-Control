using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.ActualCosts.Commands.RecordActualCost;

/// <summary>
/// S8 (actual-cost.md §9/§12, ADR-0013): posts one manual <see cref="ActualCostEntry"/>. Every
/// existence check happens before anything is constructed (mirrors
/// <c>BatchRecordProgressCommandHandler</c>'s "check everything, then mutate" ordering) so a bad
/// reference never reaches the append-only ledger at all.
/// </summary>
public sealed class RecordActualCostCommandHandler(
    IActualCostRepository repository, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IRequestHandler<RecordActualCostCommand, Result<ActualCostEntryDto>>
{
    public async Task<Result<ActualCostEntryDto>> Handle(RecordActualCostCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<ActualCostEntryDto>.Failure(ActualCostErrorCodes.ProjectNotFound);
        }

        if (request.WbsNodeId is { } wbsNodeId
            && !await repository.WbsNodeExistsAsync(request.ProjectId, wbsNodeId, cancellationToken))
        {
            return Result<ActualCostEntryDto>.Failure(ActualCostErrorCodes.WbsNodeNotFound);
        }

        if (request.ActivityId is { } activityId
            && !await repository.ActivityExistsAsync(request.ProjectId, activityId, cancellationToken))
        {
            return Result<ActualCostEntryDto>.Failure(ActualCostErrorCodes.ActivityNotFound);
        }

        if (request.ReversesEntryId is { } reversesEntryId
            && !await repository.EntryExistsAsync(request.ProjectId, reversesEntryId, cancellationToken))
        {
            return Result<ActualCostEntryDto>.Failure(ActualCostErrorCodes.ReversedEntryNotFound);
        }

        // actual-cost.md §6.6: posting into an already-closed period is allowed (soft, not hard)
        // but requires a Note. This is the one rule in this handler that needs a DB read to decide,
        // so it cannot live in RecordActualCostCommandValidator. EvmPeriodSnapshot itself is never
        // touched - the restatement it implies is surfaced on the next live EVM read, not here.
        if (string.IsNullOrWhiteSpace(request.Note)
            && await repository.ClosedPeriodExistsOnOrAfterAsync(request.ProjectId, request.IncurredDate, cancellationToken))
        {
            return Result<ActualCostEntryDto>.Failure(ActualCostErrorCodes.NoteRequiredForClosedPeriod);
        }

        var entry = new ActualCostEntry(
            tenantProvider.TenantId,
            request.ProjectId,
            request.WbsNodeId,
            request.ActivityId,
            request.CostCategory,
            request.EntryType,
            ActualCostSource.ManualEntry,
            request.Amount,
            request.IncurredDate,
            clock.UtcNow,
            currentUser.UserId ?? Guid.Empty,
            request.ReversesEntryId,
            request.DocumentReference,
            request.CostCode,
            request.VendorName,
            request.Note,
            fileImportJobId: null,
            request.PaidDate,
            request.Quantity,
            request.UnitOfMeasure);

        await repository.AddAsync(entry, cancellationToken);

        return Result<ActualCostEntryDto>.Success(ActualCostEntryDto.From(entry));
    }
}
