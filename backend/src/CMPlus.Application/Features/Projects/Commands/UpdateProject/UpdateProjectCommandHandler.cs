using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Projects.Commands.UpdateProject;

public sealed class UpdateProjectCommandHandler(IProjectRepository repository)
    : IRequestHandler<UpdateProjectCommand, Result<ProjectDto>>
{
    public async Task<Result<ProjectDto>> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await repository.FindAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<ProjectDto>.Failure(ProjectErrorCodes.NotFound);
        }

        // US-4.3/4.4: every field the Project Info screen exposes for edit. Each setter enforces
        // its own domain invariant independently - a DomainException here (e.g. a ContractFinish
        // before ContractStart that somehow reached the handler) propagates to
        // GlobalExceptionHandler and becomes a 400 domain-rule-violation, the defense-in-depth layer
        // alongside UpdateProjectCommandValidator's client-visible rejection. This handler reaches
        // only the Project aggregate (IProjectRepository exposes nothing else), so a rate change
        // here can never retroactively touch an already-issued Payment Certificate (none exist yet
        // - Sprint 9) by construction, not merely by convention.
        project.Rename(request.Name);
        project.SetCode(request.Code);
        project.SetOwner(request.Owner);
        project.SetContractDates(request.ContractStart, request.ContractFinish);
        project.SetBac(request.Bac);
        project.SetContractValue(request.ContractValue);
        project.SetRetentionRate(request.RetentionRate);
        project.SetAdvanceRate(request.AdvanceRate);
        project.SetRetentionCapPercentage(request.RetentionCapPercentage);
        project.SetRetentionRelease1Percentage(request.RetentionRelease1Percentage);
        project.SetDefectsLiabilityMonths(request.DefectsLiabilityMonths);
        project.SetAdvanceAmountPaid(request.AdvanceAmountPaid);
        project.SetAdvanceRecoveryMethod(
            request.AdvanceRecoveryMethod,
            request.AdvanceRecoveryStartPct,
            request.AdvanceRecoveryRatePct,
            request.AdvanceRecoveryEndPct);

        // A single Project entity change - AuditSaveChangesInterceptor's default per-entity
        // behaviour writes exactly one AuditLog row here with the before/after scalar snapshot
        // (S4-BE-02 DoD: "a successful edit writes an AuditLog entry with old/new values") with no
        // extra code needed - verified in UpdateProjectCommandHandlerTests /
        // AuditSaveChangesInterceptorTests, not merely assumed.
        await repository.SaveChangesAsync(cancellationToken);

        return Result<ProjectDto>.Success(ProjectDto.From(project));
    }
}
