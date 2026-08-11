using CMPlus.Application.Features.Issues;
using CMPlus.Application.Features.Issues.Commands.CreateIssue;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Tests.Features.Issues;

public class CreateIssueCommandHandlerTests
{
    private static CreateIssueCommand DefaultCommand(Guid? projectId = null) => new(
        projectId ?? Guid.NewGuid(), "เหล็กเส้น DB25 ส่งช้า", "ซัพพลายเออร์แจ้งเลื่อน 5 วัน", "จัดซื้อ",
        DateTimeOffset.Parse("2026-07-15T00:00:00+07:00"));

    /// <summary>Convenience factory for the common case: a valid, non-null actor. <c>userId: null</c>
    /// here means "don't care, generate one" (via <c>?? Guid.NewGuid()</c> below) - it does NOT
    /// simulate an unauthenticated caller. The ActorRequired test below deliberately bypasses this
    /// helper and constructs the handler directly for that reason.</summary>
    private static (CreateIssueCommandHandler Handler, FakeIssueLogRepository Repository) CreateHandler(
        Guid? tenantId = null, Guid? userId = null, DateTimeOffset? now = null)
    {
        var repository = new FakeIssueLogRepository();
        var handler = new CreateIssueCommandHandler(
            repository,
            new FakeTenantProvider(tenantId ?? Guid.NewGuid()),
            new FakeCurrentUserContext(userId ?? Guid.NewGuid()),
            new FakeClock(now ?? DateTimeOffset.UtcNow));

        return (handler, repository);
    }

    [Fact]
    public async Task Handle_Returns_ProjectNotFound_When_The_Project_Does_Not_Exist()
    {
        var (handler, repository) = CreateHandler();
        repository.ProjectExists = false;

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IssueLogErrorCodes.ProjectNotFound, result.Error);
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Handle_Fails_Closed_With_ActorRequired_On_A_Null_UserId_Never_Fabricates_Guid_Empty()
    {
        // Constructed directly (not via CreateHandler) so the explicit `null` cannot be masked by
        // CreateHandler's own "generate one if absent" convenience fallback - mirrors
        // RemainingCommandAndQueryHandlerTests's identical direct-construction style for the same reason.
        var repository = new FakeIssueLogRepository();
        var handler = new CreateIssueCommandHandler(
            repository, new FakeTenantProvider(Guid.NewGuid()), new FakeCurrentUserContext(null), new FakeClock(DateTimeOffset.UtcNow));

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IssueLogErrorCodes.ActorRequired, result.Error);
        Assert.Empty(repository.Added);
        Assert.Equal(0, repository.SaveCallCount);
    }

    [Fact]
    public async Task Handle_Creates_An_Open_Issue_Stamped_With_Server_Side_Tenant_CreatedAt_And_CreatedBy()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-08T09:00:00+07:00");
        var projectId = Guid.NewGuid();
        var (handler, repository) = CreateHandler(tenantId, userId, now);

        var result = await handler.Handle(DefaultCommand(projectId: projectId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var issue = Assert.Single(repository.Added);
        Assert.Equal(tenantId, issue.TenantId);
        Assert.Equal(projectId, issue.ProjectId);
        Assert.Equal(now, issue.CreatedAt);
        Assert.Equal(userId, issue.CreatedByUserId);
        Assert.Equal(IssueStatus.Open, issue.Status);
        Assert.Equal(1, repository.SaveCallCount);

        // US-11.2 AC: appears with Status = Open - the DTO reflects it too.
        Assert.Equal(IssueStatus.Open, result.Value.Status);
        Assert.Null(result.Value.StartedAt);
        Assert.Null(result.Value.ClosedAt);
        Assert.Null(result.Value.SequenceNo); // not computed on the Create path - see IssueLogDto's remarks.
    }

    [Fact]
    public async Task Handle_Returns_ConcurrencyConflict_When_The_Save_Fails()
    {
        var (handler, repository) = CreateHandler();
        repository.SaveShouldSucceed = false;

        var result = await handler.Handle(DefaultCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IssueLogErrorCodes.ConcurrencyConflict, result.Error);
    }
}
