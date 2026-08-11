using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Manpower;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Manpower.Queries.GetProductivityIndex;

/// <summary>
/// S12-BE-02: resolves <see cref="IProductivityIndexReader.GetAggregateAsync"/>'s already-aggregated
/// scalars into a <see cref="ProductivityIndexResult"/> via the pure
/// <see cref="ProductivityIndexCalculator"/>, plus - for exactly a one-calendar-day window - the
/// manning ratio (fixture M-02) under its own distinct field name. This handler contains no
/// arithmetic of its own beyond assembling inputs: every rule in domain-rules.md §5 lives either in
/// the reader (DB-shaped work) or the calculator (pure math).
/// </summary>
public sealed class GetProductivityIndexQueryHandler(IProductivityIndexReader reader)
    : IRequestHandler<GetProductivityIndexQuery, Result<ProductivityIndexResponseDto>>
{
    public async Task<Result<ProductivityIndexResponseDto>> Handle(GetProductivityIndexQuery request, CancellationToken cancellationToken)
    {
        if (!await reader.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<ProductivityIndexResponseDto>.Failure(ManpowerLogErrorCodes.ProjectNotFound);
        }

        if (request.WbsNodeId is { } wbsNodeId
            && !await reader.WbsNodeExistsInProjectAsync(request.ProjectId, wbsNodeId, cancellationToken))
        {
            // ADR-0002/fixture M-06i: cross-tenant and unknown are indistinguishable - always 404.
            return Result<ProductivityIndexResponseDto>.Failure(ManpowerLogErrorCodes.WbsNodeNotFound);
        }

        if (request.ActivityId is { } activityId
            && !await reader.ActivityExistsInProjectAsync(request.ProjectId, activityId, cancellationToken))
        {
            return Result<ProductivityIndexResponseDto>.Failure(ManpowerLogErrorCodes.ActivityNotFound);
        }

        if (request.From is { } from && from > request.To)
        {
            return Result<ProductivityIndexResponseDto>.Failure(ManpowerLogErrorCodes.InvalidDateRange);
        }

        // §5.2's cumulative form: a null 'from' reads from project start, i.e. a sentinel far enough
        // in the past that every activity's ADR-0009 step-function progress reads 0 there.
        var periodStartExclusive = request.From ?? DateTimeOffset.MinValue;

        var aggregate = await reader.GetAggregateAsync(
            request.ProjectId, request.WbsNodeId, request.ActivityId, periodStartExclusive, request.To, cancellationToken);

        var piResult = ProductivityIndexCalculator.Compute(
            aggregate.EarnedManHours,
            aggregate.ActualManHoursInScope,
            aggregate.ActualManHoursTotal,
            aggregate.LogEntryCount,
            aggregate.AnyActivityInScope,
            aggregate.AnyBudgetedActivityInScope,
            aggregate.HasProgressObservationInPeriod,
            aggregate.HasExplicitZeroBudgetWithMatchedHours);

        ManningRatioResult? manning = null;
        if (request.From is { } dayStart && request.To - dayStart == TimeSpan.FromDays(1))
        {
            // Fixture M-02: the manning ratio is only ever computed for a single calendar day - §5.1
            // gives no aggregation rule across multiple days, so a period query deliberately omits it
            // rather than inventing one.
            var manningInputs = await reader.GetManningInputsAsync(request.ProjectId, request.WbsNodeId, request.To, cancellationToken);
            manning = ManningRatioCalculator.Compute(manningInputs.ActualWorkerCount, manningInputs.PlannedWorkerCount);
        }

        return Result<ProductivityIndexResponseDto>.Success(
            ProductivityIndexResponseDto.Create(
                request.ProjectId, request.WbsNodeId, request.ActivityId, request.From, request.To, piResult, manning));
    }
}
