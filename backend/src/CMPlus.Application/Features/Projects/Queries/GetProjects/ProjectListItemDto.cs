namespace CMPlus.Application.Features.Projects.Queries.GetProjects;

/// <summary>
/// Lean per-project row for <c>GET /api/v1/projects</c> (S4-BE-04, fast-follow to close the
/// "no way to discover which projects exist" gap frontend-developer hit this sprint - see
/// backend-developer's Sprint 4 gap-closure report). Deliberately excludes every commercial-terms
/// field <c>ProjectDto</c> (<c>UpdateProjectCommand</c>) carries (BAC/ContractValue/Retention*/
/// Advance*/EAC*) - a project *picker* list has no reason to pull 17 columns per row when 6 are
/// enough to render one, and a tenant could plausibly have many projects where that difference
/// actually matters. Field names/order intentionally mirror <c>ProjectDto</c>'s naming for the
/// properties both share (Id/Name/Code/Owner/ContractStart/ContractFinish) so the two contracts
/// read as one consistent Project vocabulary rather than two independently-invented shapes.
/// </summary>
public sealed record ProjectListItemDto(
    Guid Id,
    string Name,
    string Code,
    string Owner,
    DateTimeOffset ContractStart,
    DateTimeOffset ContractFinish);
