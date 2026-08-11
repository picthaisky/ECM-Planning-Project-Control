using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Application.Features.Issues;

/// <summary>
/// Wire shape for an <see cref="IssueLog"/>. <see cref="SequenceNo"/> is a derived, presentational
/// display number (oldest issue in the project = 1), NOT a persisted business key - see
/// <see cref="IssueLog"/>'s own class remarks for why a real, gap-free <c>IssueNo</c> is deliberately
/// not persisted this sprint. It is <see langword="null"/> on <c>CreateIssueCommand</c>'s response
/// (computing it there would cost an extra query the hot write path does not need) and populated
/// only by <c>ListIssuesQueryHandler</c>, which already has the full per-project ordering in hand.
/// </summary>
public sealed record IssueLogDto(
    Guid Id,
    Guid ProjectId,
    int? SequenceNo,
    string Title,
    string? Detail,
    string? Owner,
    DateTimeOffset? DueDate,
    IssueStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ClosedAt,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt)
{
    public static IssueLogDto From(IssueLog issue, int? sequenceNo) => new(
        issue.Id,
        issue.ProjectId,
        sequenceNo,
        issue.Title,
        issue.Detail,
        issue.Owner,
        issue.DueDate,
        issue.Status,
        issue.StartedAt,
        issue.ClosedAt,
        issue.CreatedByUserId,
        issue.CreatedAt);
}
