using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Issues.Commands.AdvanceIssueStatus;

/// <summary><c>POST /api/v1/projects/{projectId}/issues/{issueId}/advance-status</c> (S11-BE-03,
/// US-11.2, domain-rules.md weather-eot §9.1 - nested under the project route, superseding this
/// task's earlier flat-route judgement call once that document landed). <see cref="ProjectId"/> is
/// cross-checked against the loaded issue's own <c>ProjectId</c> so an issue id that resolves (same
/// tenant) but belongs to a DIFFERENT project than the one named in the route is treated the same as
/// "does not exist" - the same indistinguishable-404 discipline ADR-0002 already applies across
/// tenants, extended here across projects within one tenant now that the route carries a project id
/// to cross-check against.</summary>
public sealed record AdvanceIssueStatusCommand(Guid ProjectId, Guid IssueId) : IRequest<Result<IssueLogDto>>;
