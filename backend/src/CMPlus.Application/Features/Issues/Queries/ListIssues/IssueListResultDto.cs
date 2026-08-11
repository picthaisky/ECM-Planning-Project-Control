namespace CMPlus.Application.Features.Issues.Queries.ListIssues;

/// <summary>
/// S11-BE-03 DoD / domain-rules.md §9.3: "the tile counter must match the real data" - counts are
/// computed by <c>ListIssuesQueryHandler</c> from the exact same in-memory row set as
/// <see cref="Items"/> - one repository call, one list, both the table and the tiles read off it.
/// There is no independent counter anywhere that could drift from the table (this task's brief: the
/// class of bug S8-QA's cross-screen consistency tests exist for). Response shape matches
/// domain-rules.md §9.3's example verbatim (<c>items</c>/<c>totalCount</c>/<c>statusCounts</c>).
/// </summary>
public sealed record IssueListResultDto(
    IReadOnlyList<IssueLogDto> Items,
    int TotalCount,
    IssueStatusCountsDto StatusCounts);

/// <summary>Invariant asserted by domain-rules.md §9.3: <c>Open + Doing + Closed == TotalCount</c>,
/// and there is no fourth status.</summary>
public sealed record IssueStatusCountsDto(int Open, int Doing, int Closed);
