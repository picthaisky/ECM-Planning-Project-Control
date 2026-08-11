using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.Issues.Commands.CreateIssue;

public sealed class CreateIssueCommandHandler(
    IIssueLogRepository repository, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IRequestHandler<CreateIssueCommand, Result<IssueLogDto>>
{
    public async Task<Result<IssueLogDto>> Handle(CreateIssueCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<IssueLogDto>.Failure(IssueLogErrorCodes.ProjectNotFound);
        }

        // Sprint 10 L-01 fix pattern (this task's brief): fail closed on a null actor id rather than
        // fabricating Guid.Empty.
        if (currentUser.UserId is not { } createdByUserId)
        {
            return Result<IssueLogDto>.Failure(IssueLogErrorCodes.ActorRequired);
        }

        var issue = new IssueLog(
            tenantProvider.TenantId, request.ProjectId, request.Title, request.Detail, request.Owner,
            request.DueDate, createdByUserId, clock.UtcNow);

        repository.Add(issue);

        // A brand-new row can never itself lose a RowVersion race - TrySaveChangesAsync is used for
        // shape-consistency with AdvanceIssueStatusCommandHandler, not because a conflict is
        // realistically reachable here.
        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<IssueLogDto>.Failure(IssueLogErrorCodes.ConcurrencyConflict);
        }

        // No SequenceNo here - see IssueLogDto's own remarks.
        return Result<IssueLogDto>.Success(IssueLogDto.From(issue, sequenceNo: null));
    }
}
