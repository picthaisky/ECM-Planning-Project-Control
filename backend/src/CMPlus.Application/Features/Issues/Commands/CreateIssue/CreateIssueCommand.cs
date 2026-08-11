using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Issues.Commands.CreateIssue;

/// <summary><c>POST /api/v1/projects/{projectId}/issues</c> (S11-BE-03, docs/9 §4 route). Always
/// created <c>Status = Open</c> (US-11.2) - <c>CreatedByUserId</c>/<c>CreatedAt</c> are resolved
/// server-side, never trusted from the request.</summary>
public sealed record CreateIssueCommand(
    Guid ProjectId,
    string Title,
    string? Detail,
    string? Owner,
    DateTimeOffset? DueDate) : IRequest<Result<IssueLogDto>>;
