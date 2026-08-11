using CMPlus.Application.Features.Issues;
using CMPlus.Application.Features.Issues.Commands.AdvanceIssueStatus;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Issues;

public class AdvanceIssueStatusCommandHandlerTests
{
    private static IssueLog CreateOpenIssue(Guid? projectId = null) => new(
        Guid.NewGuid(), projectId ?? Guid.NewGuid(), "ทางเข้าไซต์ฝั่งตะวันออกน้ำขัง", "ปรับระดับและลงหินคลุกแล้ว",
        "โฟร์แมนโยธา", DateTimeOffset.Parse("2026-07-10T00:00:00+07:00"), Guid.NewGuid(),
        DateTimeOffset.Parse("2026-07-05T08:00:00+07:00"));

    private static FakeIssueLogRepository RepositoryWith(IssueLog issue)
    {
        var repository = new FakeIssueLogRepository();
        repository.Add(issue);
        return repository;
    }

    [Fact]
    public async Task Handle_Returns_NotFound_For_An_Unknown_Issue_Id()
    {
        var repository = new FakeIssueLogRepository();
        var handler = new AdvanceIssueStatusCommandHandler(repository, new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(new AdvanceIssueStatusCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IssueLogErrorCodes.NotFound, result.Error);
        Assert.Equal(0, repository.SaveCallCount);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_The_Issue_Belongs_To_A_Different_Project_Than_The_Route()
    {
        // The issue resolves (same tenant) but the route names a different project - treated
        // identically to "does not exist", the same indistinguishable-404 discipline ADR-0002
        // already applies across tenants (this command's own remarks).
        var issue = CreateOpenIssue();
        var repository = RepositoryWith(issue);
        var handler = new AdvanceIssueStatusCommandHandler(repository, new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(new AdvanceIssueStatusCommand(Guid.NewGuid(), issue.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IssueLogErrorCodes.NotFound, result.Error);
    }

    [Fact]
    public async Task Handle_Moves_Open_To_Doing_And_Stamps_StartedAt()
    {
        var issue = CreateOpenIssue();
        var repository = RepositoryWith(issue);
        var now = DateTimeOffset.Parse("2026-07-06T10:00:00+07:00");
        var handler = new AdvanceIssueStatusCommandHandler(repository, new FakeClock(now));

        var result = await handler.Handle(new AdvanceIssueStatusCommand(issue.ProjectId, issue.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(IssueStatus.Doing, issue.Status);
        Assert.Equal(now, issue.StartedAt);
        Assert.Null(issue.ClosedAt);
        Assert.Equal(IssueStatus.Doing, result.Value.Status);
        Assert.Equal(now, result.Value.StartedAt);
        Assert.Equal(1, repository.SaveCallCount);
    }

    [Fact]
    public async Task Handle_Moves_Doing_To_Closed_And_Stamps_ClosedAt_Leaving_StartedAt_Unchanged()
    {
        var issue = CreateOpenIssue();
        var startedAt = DateTimeOffset.Parse("2026-07-06T10:00:00+07:00");
        issue.AdvanceStatus(startedAt); // Open -> Doing
        var repository = RepositoryWith(issue);
        var closedAt = DateTimeOffset.Parse("2026-07-09T16:00:00+07:00");
        var handler = new AdvanceIssueStatusCommandHandler(repository, new FakeClock(closedAt));

        var result = await handler.Handle(new AdvanceIssueStatusCommand(issue.ProjectId, issue.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(IssueStatus.Closed, issue.Status);
        Assert.Equal(startedAt, issue.StartedAt); // unchanged from the earlier transition
        Assert.Equal(closedAt, issue.ClosedAt);
        Assert.Equal(closedAt, result.Value.ClosedAt);
    }

    [Fact]
    public async Task Handle_Rejects_Advancing_An_Already_Closed_Issue_And_Never_Calls_TrySaveChanges()
    {
        var issue = CreateOpenIssue();
        issue.AdvanceStatus(DateTimeOffset.UtcNow); // Open -> Doing
        issue.AdvanceStatus(DateTimeOffset.UtcNow); // Doing -> Closed
        var originalClosedAt = issue.ClosedAt;
        var repository = RepositoryWith(issue);
        var handler = new AdvanceIssueStatusCommandHandler(repository, new FakeClock(DateTimeOffset.UtcNow.AddDays(1)));

        var result = await handler.Handle(new AdvanceIssueStatusCommand(issue.ProjectId, issue.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IssueLogErrorCodes.AlreadyClosed, result.Error);
        Assert.Equal(0, repository.SaveCallCount);

        // Unperturbed by the rejected attempt.
        Assert.Equal(IssueStatus.Closed, issue.Status);
        Assert.Equal(originalClosedAt, issue.ClosedAt);
    }

    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_A_Concurrent_Advance_Already_Won_domain_rules_9_2()
    {
        // domain-rules.md §9.2: "two users advancing simultaneously means the second gets 409,
        // never a double-advance."
        var issue = CreateOpenIssue();
        var repository = RepositoryWith(issue);
        repository.SaveShouldSucceed = false;
        var handler = new AdvanceIssueStatusCommandHandler(repository, new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(new AdvanceIssueStatusCommand(issue.ProjectId, issue.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IssueLogErrorCodes.ConcurrencyConflict, result.Error);
    }
}
