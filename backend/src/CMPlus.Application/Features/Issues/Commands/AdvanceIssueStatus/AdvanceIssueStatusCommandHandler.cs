using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Issues.Commands.AdvanceIssueStatus;

/// <summary>
/// <c>Open -&gt; Doing -&gt; Closed</c>, one step per call (S11-BE-03 DoD). The <c>Closed</c>
/// pre-check is redundant with <see cref="Domain.Entities.IssueLog.AdvanceStatus"/>'s own guard but
/// kept anyway - this codebase's established discipline (see
/// <c>ApproveVariationOrderCommandHandler</c>) is a typed <see cref="Result"/> failure checked
/// BEFORE any domain method runs, never a caught <see cref="Domain.Common.DomainException"/>.
/// </summary>
public sealed class AdvanceIssueStatusCommandHandler(IIssueLogRepository repository, IDateTimeProvider clock)
    : IRequestHandler<AdvanceIssueStatusCommand, Result<IssueLogDto>>
{
    public async Task<Result<IssueLogDto>> Handle(AdvanceIssueStatusCommand request, CancellationToken cancellationToken)
    {
        var issue = await repository.FindAsync(request.IssueId, cancellationToken);

        // An issue id that resolves (same tenant) but belongs to a different project than the route
        // named is treated identically to "does not exist" - see this command's own remarks.
        if (issue is null || issue.ProjectId != request.ProjectId)
        {
            return Result<IssueLogDto>.Failure(IssueLogErrorCodes.NotFound);
        }

        if (issue.Status == IssueStatus.Closed)
        {
            return Result<IssueLogDto>.Failure(IssueLogErrorCodes.AlreadyClosed);
        }

        issue.AdvanceStatus(clock.UtcNow);

        // domain-rules.md §9.2: two users advancing simultaneously - the second gets 409, never a
        // double-advance.
        if (!await repository.TrySaveChangesAsync(cancellationToken))
        {
            return Result<IssueLogDto>.Failure(IssueLogErrorCodes.ConcurrencyConflict);
        }

        return Result<IssueLogDto>.Success(IssueLogDto.From(issue, sequenceNo: null));
    }
}
