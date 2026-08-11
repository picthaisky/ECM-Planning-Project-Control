using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Evm.Queries.ListEvmSnapshots;

/// <summary>
/// Returns an empty list - never a 404 - for a project id that does not exist or belongs to another
/// tenant. This is the established precedent for list reads in this codebase (`GetJobHistoryAsync`,
/// S3-BE-04, explicitly confirmed non-leaking in `docs/security/reviews/sprint-03.md` §5a) and is
/// tenant-safe by construction: the global query filter (ADR-0002) means another tenant's snapshots
/// can never be returned, so an empty result discloses nothing about whether the id exists. It also
/// avoids paying for a full EVM computation purely to decide between 404 and 200.
/// </summary>
public sealed class ListEvmSnapshotsQueryHandler(IEvmSnapshotRepository snapshots)
    : IRequestHandler<ListEvmSnapshotsQuery, Result<IReadOnlyList<EvmSnapshotDto>>>
{
    public async Task<Result<IReadOnlyList<EvmSnapshotDto>>> Handle(
        ListEvmSnapshotsQuery request, CancellationToken cancellationToken)
    {
        if (request.From is not null && request.To is not null && request.From > request.To)
        {
            return Result<IReadOnlyList<EvmSnapshotDto>>.Failure(EvmErrorCodes.InvalidSnapshotRange);
        }

        var rows = await snapshots.ListAsync(request.ProjectId, request.From, request.To, cancellationToken);

        return Result<IReadOnlyList<EvmSnapshotDto>>.Success(
            rows.Select(EvmSnapshotDto.From).ToList());
    }
}
