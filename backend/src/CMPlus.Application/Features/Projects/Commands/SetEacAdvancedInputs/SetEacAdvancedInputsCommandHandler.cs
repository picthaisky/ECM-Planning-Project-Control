using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Projects.Commands.SetEacAdvancedInputs;

public sealed class SetEacAdvancedInputsCommandHandler(IProjectRepository repository)
    : IRequestHandler<SetEacAdvancedInputsCommand, Result<SetEacAdvancedInputsResultDto>>
{
    public async Task<Result<SetEacAdvancedInputsResultDto>> Handle(
        SetEacAdvancedInputsCommand request, CancellationToken cancellationToken)
    {
        var project = await repository.FindAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<SetEacAdvancedInputsResultDto>.Failure(ProjectErrorCodes.NotFound);
        }

        // S14-BE-03: the symmetric half of SetEacVariantDefaultCommandHandler's pre-check - this
        // command approaches the same invariant from the other direction (clearing an input while
        // its variant is already selected, rather than selecting the variant while its input is
        // unset). Pre-checked here for the same specific, actionable error codes;
        // Project.SetEacManualEtc/SetEacCustomPerformanceFactor below carry the identical guards as
        // defense in depth, so neither can actually throw for a well-formed request reaching here.
        if (request.EacManualEtc is null && project.EacVariantDefault == EacVariant.BottomUpEtc)
        {
            return Result<SetEacAdvancedInputsResultDto>.Failure(ProjectErrorCodes.EacManualEtcRequiredForBottomUpEtc);
        }

        if (request.EacCustomPerformanceFactor is null && project.EacVariantDefault == EacVariant.CustomPf)
        {
            return Result<SetEacAdvancedInputsResultDto>.Failure(ProjectErrorCodes.EacCustomPerformanceFactorRequiredForCustomPf);
        }

        // Full-representation PUT (see SetEacAdvancedInputsCommand's remarks): both setters always
        // run, never conditionally, so a caller that omits one field genuinely clears it rather than
        // leaving it silently untouched.
        project.SetEacManualEtc(request.EacManualEtc);
        project.SetEacCustomPerformanceFactor(request.EacCustomPerformanceFactor);

        // A single Project entity change - AuditSaveChangesInterceptor's default per-entity
        // behaviour writes exactly one AuditLog row here with the old/new scalar values (CLAUDE.md:
        // "every mutating domain operation writes an audit log entry"), the same pattern
        // SetEacVariantDefaultCommandHandler/UpdateProjectCommandHandler already establish - no
        // bespoke audit code needed.
        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<SetEacAdvancedInputsResultDto>.Failure(ProjectErrorCodes.ConcurrencyConflict);
        }

        return Result<SetEacAdvancedInputsResultDto>.Success(new SetEacAdvancedInputsResultDto(
            project.Id, project.EacManualEtc, project.EacCustomPerformanceFactor, project.EacManualEtcStaleSince));
    }
}
