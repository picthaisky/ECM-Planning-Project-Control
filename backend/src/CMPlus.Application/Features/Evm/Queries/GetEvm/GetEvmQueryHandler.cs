using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Evm.Queries.GetEvm;

public sealed class GetEvmQueryHandler(EvmComputationService computationService)
    : IRequestHandler<GetEvmQuery, Result<EvmResponseDto>>
{
    public async Task<Result<EvmResponseDto>> Handle(GetEvmQuery request, CancellationToken cancellationToken)
    {
        EacVariant? variantOverride = null;
        if (!string.IsNullOrWhiteSpace(request.EacVariant))
        {
            if (!EacVariantParser.TryParse(request.EacVariant, out var parsed))
            {
                // Dynamic-suffix error code (mirrors CpmValidationErrorCodes.CycleDetected) so
                // ResultProblemMapper can surface the actual offending value while still mapping to
                // the stable "invalid-eac-variant" type - design.md §2.1: never a silent fallback.
                return Result<EvmResponseDto>.Failure($"{EvmErrorCodes.InvalidEacVariantPrefix}: {request.EacVariant}");
            }

            variantOverride = parsed;
        }

        var computation = await computationService.ComputeAsync(request.ProjectId, request.DataDate, cancellationToken);
        if (computation is null)
        {
            return Result<EvmResponseDto>.Failure(EvmErrorCodes.ProjectNotFound);
        }

        // eacVariantOverride only ever changes `selectedVariant` - every computable variant is
        // always present in the response regardless (design.md §1.1).
        var selectedVariant = variantOverride ?? computation.Settings.EacVariantDefault;

        return Result<EvmResponseDto>.Success(EvmResponseDto.From(
            request.ProjectId, computation.DataDate, computation.Core, computation.EacResult, selectedVariant));
    }
}
