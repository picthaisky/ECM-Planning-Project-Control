using CMPlus.Application.Features.Issues.Queries.ListIssues;
using CMPlus.Domain.Entities;

namespace CMPlus.Application.Tests.Features.Issues;

/// <summary>
/// S11-BE-03 DoD / domain-rules.md §9.3: "the tile counter must match the real data" - this task's
/// brief singles this out as not decoration ("a dashboard tile that disagrees with its own table is
/// the class of bug S8-QA's cross-screen consistency tests were written for"). These tests assert
/// the tile counts against the SAME materialised <see cref="IssueListResultDto.Items"/> the table
/// renders, on every case below - not merely that some plausible-looking number came back.
/// </summary>
public class ListIssuesQueryHandlerTests
{
    private static IssueLog CreateIssue(Guid projectId, string title, DateTimeOffset createdAt) => new(
        Guid.NewGuid(), projectId, title, null, null, null, Guid.NewGuid(), createdAt);

    private static FakeIssueLogRepository RepositoryWith(params IssueLog[] issues)
    {
        var repository = new FakeIssueLogRepository();
        foreach (var issue in issues)
        {
            repository.Add(issue);
        }

        return repository;
    }

    [Fact]
    public async Task Handle_Returns_All_Zero_Counts_And_An_Empty_List_For_A_Project_With_No_Issues()
    {
        var repository = new FakeIssueLogRepository();
        var handler = new ListIssuesQueryHandler(repository);

        var result = await handler.Handle(new ListIssuesQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.StatusCounts.Open);
        Assert.Equal(0, result.Value.StatusCounts.Doing);
        Assert.Equal(0, result.Value.StatusCounts.Closed);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task Handle_Tile_Counts_Exactly_Match_The_Statuses_Present_In_Items_Mixed_Case()
    {
        var projectId = Guid.NewGuid();
        var baseTime = DateTimeOffset.Parse("2026-07-05T08:00:00+07:00");

        var open1 = CreateIssue(projectId, "ISS open 1", baseTime);
        var open2 = CreateIssue(projectId, "ISS open 2", baseTime.AddDays(1));
        var doing1 = CreateIssue(projectId, "ISS doing 1", baseTime.AddDays(2));
        doing1.AdvanceStatus(baseTime.AddDays(2).AddHours(1));
        var closed1 = CreateIssue(projectId, "ISS closed 1", baseTime.AddDays(3));
        closed1.AdvanceStatus(baseTime.AddDays(3).AddHours(1));
        closed1.AdvanceStatus(baseTime.AddDays(4));

        var repository = RepositoryWith(open1, open2, doing1, closed1);
        var handler = new ListIssuesQueryHandler(repository);

        var result = await handler.Handle(new ListIssuesQuery(projectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value;

        Assert.Equal(4, dto.Items.Count);
        Assert.Equal(4, dto.TotalCount);

        // The structural proof: every count equals what you get by counting Items yourself - the
        // tile can never disagree with the table because both come from the same materialised list.
        Assert.Equal(dto.Items.Count(i => i.Status == Domain.Enums.IssueStatus.Open), dto.StatusCounts.Open);
        Assert.Equal(dto.Items.Count(i => i.Status == Domain.Enums.IssueStatus.Doing), dto.StatusCounts.Doing);
        Assert.Equal(dto.Items.Count(i => i.Status == Domain.Enums.IssueStatus.Closed), dto.StatusCounts.Closed);

        // domain-rules.md §9.3's invariant.
        Assert.Equal(dto.TotalCount, dto.StatusCounts.Open + dto.StatusCounts.Doing + dto.StatusCounts.Closed);

        Assert.Equal(2, dto.StatusCounts.Open);
        Assert.Equal(1, dto.StatusCounts.Doing);
        Assert.Equal(1, dto.StatusCounts.Closed);
    }

    /// <summary>
    /// Mutation-style proof the counts are genuinely derived, not a hardcoded/stale value: advancing
    /// one issue's status and re-running the query moves the counts by exactly one in each direction
    /// - if Open/Doing were computed from two different queries (or one were cached), this is
    /// exactly the scenario that would let them drift.
    /// </summary>
    [Fact]
    public async Task Handle_Counts_Shift_By_Exactly_One_When_A_Single_Issues_Status_Changes_Between_Calls()
    {
        var projectId = Guid.NewGuid();
        var issue = CreateIssue(projectId, "เหล็กเส้น DB25 ส่งช้า", DateTimeOffset.Parse("2026-07-05T08:00:00+07:00"));
        var repository = RepositoryWith(issue);
        var handler = new ListIssuesQueryHandler(repository);

        var before = await handler.Handle(new ListIssuesQuery(projectId), CancellationToken.None);
        Assert.Equal(1, before.Value.StatusCounts.Open);
        Assert.Equal(0, before.Value.StatusCounts.Doing);

        issue.AdvanceStatus(DateTimeOffset.Parse("2026-07-06T09:00:00+07:00")); // Open -> Doing

        var after = await handler.Handle(new ListIssuesQuery(projectId), CancellationToken.None);
        Assert.Equal(0, after.Value.StatusCounts.Open);
        Assert.Equal(1, after.Value.StatusCounts.Doing);
        Assert.Equal(1, after.Value.TotalCount); // total is unaffected by a status move
    }

    [Fact]
    public async Task Handle_Orders_Items_Newest_First_And_Assigns_SequenceNo_Oldest_Equals_One()
    {
        var projectId = Guid.NewGuid();
        var oldest = CreateIssue(projectId, "oldest", DateTimeOffset.Parse("2026-07-05T08:00:00+07:00"));
        var middle = CreateIssue(projectId, "middle", DateTimeOffset.Parse("2026-07-06T08:00:00+07:00"));
        var newest = CreateIssue(projectId, "newest", DateTimeOffset.Parse("2026-07-07T08:00:00+07:00"));

        // Deliberately seeded out of chronological order - the handler must sort, not trust input order.
        var repository = RepositoryWith(middle, newest, oldest);
        var handler = new ListIssuesQueryHandler(repository);

        var result = await handler.Handle(new ListIssuesQuery(projectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["newest", "middle", "oldest"], result.Value.Items.Select(i => i.Title));
        Assert.Equal([3, 2, 1], result.Value.Items.Select(i => i.SequenceNo));
    }

    [Fact]
    public async Task Handle_Only_Counts_Issues_Belonging_To_The_Requested_Project()
    {
        // FakeIssueLogRepository already mirrors the real repository's project-scoping contract
        // (ListByProjectAsync only ever returns rows for the requested project) - this test proves
        // the handler does not additionally re-aggregate across whatever it is handed, i.e. it trusts
        // and uses exactly the rows returned for the requested project, nothing more.
        var projectId = Guid.NewGuid();
        var issue = CreateIssue(projectId, "belongs here", DateTimeOffset.UtcNow);
        var repository = RepositoryWith(issue, CreateIssue(Guid.NewGuid(), "different project", DateTimeOffset.UtcNow));
        var handler = new ListIssuesQueryHandler(repository);

        var result = await handler.Handle(new ListIssuesQuery(projectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.All(result.Value.Items, i => Assert.Equal(projectId, i.ProjectId));
    }
}
