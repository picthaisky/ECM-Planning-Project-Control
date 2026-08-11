using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
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
