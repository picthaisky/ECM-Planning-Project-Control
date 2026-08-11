using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Projects.Commands.SetEacVariantDefault;

public sealed class SetEacVariantDefaultCommandHandler(IProjectRepository repository)
    : IRequestHandler<SetEacVariantDefaultCommand, Result<SetEacVariantDefaultResultDto>>
{
    public async Task<Result<SetEacVariantDefaultResultDto>> Handle(
        SetEacVariantDefaultCommand request, CancellationToken cancellationToken)
    {
        var project = await repository.FindAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<SetEacVariantDefaultResultDto>.Failure(ProjectErrorCodes.NotFound);
        }

        // S14-BE-03 (closes ADR-0007(d)'s deferred Sprint 14 UI gap): BottomUpEtc/CustomPf each need
        // their own project-level input already configured before they can be selected - otherwise
        // the EVM screen would switch to a variant that immediately renders "-" with no explanation.
        // Pre-checked here for a specific, actionable error code; Project.SetEacVariantDefault below
        // carries the identical guard as defense in depth (e.g. for a future caller that reaches it
        // directly), which is why this can never actually throw in a well-formed request.
        if (request.Variant == EacVariant.BottomUpEtc && project.EacManualEtc is null)
        {
            return Result<SetEacVariantDefaultResultDto>.Failure(ProjectErrorCodes.EacManualEtcRequiredForBottomUpEtc);
        }

        if (request.Variant == EacVariant.CustomPf && project.EacCustomPerformanceFactor is null)
        {
            return Result<SetEacVariantDefaultResultDto>.Failure(ProjectErrorCodes.EacCustomPerformanceFactorRequiredForCustomPf);
        }

        project.SetEacVariantDefault(request.Variant);

        // A single Project entity change - AuditSaveChangesInterceptor's default per-entity
        // behaviour writes exactly one AuditLog row here with the old/new EacVariantDefault
        // (ADR-0007(f): "changing EacVariantDefault is a mutating domain operation -> audit log
        // entry"), the same pattern UpdateProjectCommandHandler already established - no bespoke
        // audit code needed.
        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<SetEacVariantDefaultResultDto>.Failure(ProjectErrorCodes.ConcurrencyConflict);
        }


        return Result<SetEacVariantDefaultResultDto>.Success(
            new SetEacVariantDefaultResultDto(project.Id, project.EacVariantDefault));
    }
}
