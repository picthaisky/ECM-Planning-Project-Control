using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Issues.Queries.ListIssues;

/// <summary><c>GET /api/v1/projects/{projectId}/issues</c> (docs/9 §4 route) - the project-scoped
/// issue register, table rows plus the open/doing/closed tile counts in one response
/// (<see cref="IssueListResultDto"/>).</summary>
public sealed record ListIssuesQuery(Guid ProjectId) : IRequest<Result<IssueListResultDto>>;
