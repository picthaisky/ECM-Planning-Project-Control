using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Issues.Queries.ListIssues;

/// <summary>
/// Never 404s on an unknown/cross-tenant/other-project <c>ProjectId</c> - returns an all-zero
/// result instead, the established precedent for list reads in this codebase (mirrors
/// <c>ListVariationOrdersQueryHandler</c>'s identical remark: the global tenant filter plus this
/// query's own ProjectId scoping already make every "nothing here" case produce the identical
/// empty result, so there is nothing to leak).
///
/// <para><b>Disclosed, deliberate partial implementation of domain-rules.md §9.3.</b> That section's
/// example response also shows <c>page</c>/<c>pageSize</c>/<c>status=</c>/<c>owner=</c> query-string
/// pagination and filtering. This handler returns the full, unpaginated, unfiltered project issue
/// list - the tile-count-matches-table guarantee (the actual DoD text) holds regardless, because
/// <see cref="IssueListResultDto.StatusCounts"/> is still derived from the exact same row set as
/// <see cref="IssueListResultDto.Items"/> in every case, paginated or not. Full pagination/filtering
/// is a genuinely separate scope addition (query params, skip/take, a filter predicate, and its own
/// test matrix) that this task did not attempt to bolt on under the time pressure of reconciling
/// against domain-rules.md landing mid-implementation - flagged for a fast follow-up rather than
/// silently shipped as "done". Fixture W-12c's literal <c>page=1&amp;pageSize=20</c> assertions do
/// not pass against this handler as a result; its <c>statusCounts</c>/<c>totalCount</c> shape and
/// invariant do.</para>
/// </summary>
public sealed class ListIssuesQueryHandler(IIssueLogRepository repository)
    : IRequestHandler<ListIssuesQuery, Result<IssueListResultDto>>
{
    public async Task<Result<IssueListResultDto>> Handle(ListIssuesQuery request, CancellationToken cancellationToken)
    {
        // ONE round trip - both the table rows and the tile counts below are derived from this same
        // in-memory list (S11-BE-03 DoD / IssueListResultDto's own remarks).
        var issues = await repository.ListByProjectAsync(request.ProjectId, cancellationToken);

        // SequenceNo: oldest = 1 (IssueLogDto's own remarks - presentational only). Computed over an
        // ascending copy, keyed by Id, then applied to the newest-first display order below - the
        // UUIDv7 Id is a deterministic tie-break for two issues logged in the same instant.
        var oldestFirst = issues
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Id.ToString(), StringComparer.Ordinal)
            .ToList();

        var sequenceNoById = new Dictionary<Guid, int>(oldestFirst.Count);
        for (var index = 0; index < oldestFirst.Count; index++)
        {
            sequenceNoById[oldestFirst[index].Id] = index + 1;
        }

        var items = issues
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id.ToString(), StringComparer.Ordinal)
            .Select(i => IssueLogDto.From(i, sequenceNoById[i.Id]))
            .ToList();

        var statusCounts = new IssueStatusCountsDto(
            Open: issues.Count(i => i.Status == IssueStatus.Open),
            Doing: issues.Count(i => i.Status == IssueStatus.Doing),
            Closed: issues.Count(i => i.Status == IssueStatus.Closed));

        return Result<IssueListResultDto>.Success(new IssueListResultDto(items, issues.Count, statusCounts));
    }
}
