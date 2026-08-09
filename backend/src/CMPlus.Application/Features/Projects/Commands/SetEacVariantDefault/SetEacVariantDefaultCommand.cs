using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Projects.Commands.SetEacVariantDefault;

/// <summary>
/// S7-BE-04 (US-7.2, ADR-0007(f)): `PUT /api/v1/projects/{projectId}/eac-default` (design.md §2.1).
/// A mutating domain operation - restricted to PM/QS/Executive at the WebApi
/// <c>[Authorize(Roles=...)]</c> boundary (other roles -&gt; 403, the same layering
/// <c>UpdateProjectCommand</c> already uses; this Application handler itself does not re-check the
/// role) and audited automatically by <c>AuditSaveChangesInterceptor</c> - no bespoke audit code
/// needed, the same as every other single-`Project`-field edit in this codebase.
/// </summary>
public sealed record SetEacVariantDefaultCommand(Guid ProjectId, EacVariant Variant)
    : IRequest<Result<SetEacVariantDefaultResultDto>>;
