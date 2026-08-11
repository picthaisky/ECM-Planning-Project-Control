using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;

public sealed class CloseEvmPeriodCommandHandler(
    EvmComputationService computationService,
    IEvmSnapshotRepository snapshotRepository,
    ITenantProvider tenantProvider,
    ICurrentUserContext currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<CloseEvmPeriodCommand, Result<EvmSnapshotDto>>
{
    public async Task<Result<EvmSnapshotDto>> Handle(CloseEvmPeriodCommand request, CancellationToken cancellationToken)
    {
        var computation = await computationService.ComputeAsync(request.ProjectId, request.DataDate, cancellationToken);
        if (computation is null)
        {
            return Result<EvmSnapshotDto>.Failure(EvmErrorCodes.ProjectNotFound);
        }

        // S7-BE-05 DoD: closing the same dataDate twice -> 409, checked before any write.
        if (await snapshotRepository.ExistsForDataDateAsync(request.ProjectId, request.DataDate, cancellationToken))
        {
            return Result<EvmSnapshotDto>.Failure(EvmErrorCodes.SnapshotAlreadyExists);
        }

        // Always the project's own current default - the close endpoint's request body carries only
        // a data date (design.md §2.1), never a variant override.
        var variant = computation.Settings.EacVariantDefault;
        var variantResult = computation.EacResult.GetVariant(variant);

        // EvmPeriodSnapshot's own constructor applies CMPlus.Domain.Common.RoundingRules - full
        // precision is passed through from the engines up to this one boundary.
        var snapshot = new EvmPeriodSnapshot(
            tenantProvider.TenantId,
            request.ProjectId,
            computation.DataDate,
            computation.Core.Bac,
            computation.Core.Pv,
            computation.Core.Ev,
            computation.Core.Ac,
            variant,
            variantResult.PerformanceFactor,
            variantResult.Eac,
            variantResult.Etc,
            variantResult.Vac,
            clock.UtcNow,
            currentUser.UserId);

        await snapshotRepository.AddAsync(snapshot, cancellationToken);

        return Result<EvmSnapshotDto>.Success(EvmSnapshotDto.From(snapshot));
    }
}
