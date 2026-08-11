using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Projects.Commands.SetEacAdvancedInputs;

/// <summary>
/// S14-BE-03 (US-14.3*, ADR-0007(d)'s deferred Sprint 14 gap): `PUT
/// /api/v1/projects/{projectId}/eac-advanced-inputs` (mirrors <c>SetEacVariantDefaultCommand</c>'s
/// route/verb shape). Sets both <c>Project.EacManualEtc</c> (the <see cref="Domain.Enums.EacVariant.BottomUpEtc"/>
/// input) and <c>Project.EacCustomPerformanceFactor</c> (the <see cref="Domain.Enums.EacVariant.CustomPf"/>
/// input) - the five-variant EAC engine (<c>EacCalculator</c>, Sprint 7) already reads both; this
/// command is only the missing write path, not a new calculation.
///
/// <para><b>Full-representation PUT, like <c>UpdateProjectCommand</c> - not a partial patch.</b>
/// Both fields are always supplied (either may be <see langword="null"/>, meaning "not configured");
/// a caller that wants to change only one must echo the other's current value, exactly like
/// <c>UpdateProjectCommand</c> already requires for its own many fields. This is what keeps
/// <c>Project.SetEacManualEtc</c>/<c>SetEacCustomPerformanceFactor</c>'s "cannot clear while that
/// variant is active" guard meaningful rather than ambiguous between "the caller wants this cleared"
/// and "the caller simply did not mention it".</para>
///
/// <para>Restricted to PM/QS/Executive at the WebApi <c>[Authorize(Roles=...)]</c> boundary -
/// identical to <c>SetEacVariantDefaultCommand</c>'s gate (ADR-0007(f) names exactly these three
/// roles for the variant decision; these two inputs feed directly into that same decision, so the
/// same audience applies) - and audited automatically by <c>AuditSaveChangesInterceptor</c>, the
/// same as every other single-<c>Project</c>-field edit in this codebase.</para>
/// </summary>
public sealed record SetEacAdvancedInputsCommand(
    Guid ProjectId,
    decimal? EacManualEtc,
    decimal? EacCustomPerformanceFactor) : IRequest<Result<SetEacAdvancedInputsResultDto>>;
