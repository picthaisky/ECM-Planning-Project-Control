namespace CMPlus.WebApi.Controllers.Issues;

/// <summary>Request body for <c>POST /api/v1/projects/{projectId}/issues</c>.</summary>
public sealed record CreateIssueRequest(
    string Title,
    string? Detail,
    string? Owner,
    DateTimeOffset? DueDate);
