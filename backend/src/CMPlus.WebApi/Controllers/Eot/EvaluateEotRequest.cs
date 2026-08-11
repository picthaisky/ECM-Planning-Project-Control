namespace CMPlus.WebApi.Controllers.Eot;

/// <summary>Request body for <c>POST /api/v1/projects/{projectId}/eot-evaluations</c>. Both fields
/// optional - <see langword="null"/> defaults to the project's <c>ContractStart</c>/<c>DataDate</c>
/// (domain-rules.md weather-eot §1). An empty/absent body is valid and uses both defaults.</summary>
public sealed record EvaluateEotRequest(DateTimeOffset? WindowStart, DateTimeOffset? WindowEnd);
