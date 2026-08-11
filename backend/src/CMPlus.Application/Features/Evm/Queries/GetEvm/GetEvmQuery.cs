using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Evm.Queries.GetEvm;

/// <summary>
/// S7-BE-03 (US-7.1/7.2): `GET /api/v1/projects/{projectId}/evm?dataDate=&amp;eacVariant=`
/// (design.md §2.1).
///
/// <para><paramref name="DataDate"/> is optional (resolved-ambiguity: design.md's own DoD only
/// specifies the query shape, not whether the parameter is mandatory). Omitting it defaults to
/// `Project.DataDate` - the same "current reporting date" every other screen already treats as the
/// project's live data date (e.g. the Gantt's own data-date line, S6-FE-01) - rather than requiring
/// every caller to first read the project just to pass its own current date back.</para>
///
/// <para><paramref name="EacVariant"/> is the raw query string (not yet parsed to the enum) so an
/// unrecognised value can be rejected as a specific `400 invalid-eac-variant` (design.md §2.1)
/// instead of the framework's generic query-string-model-binding validation-problem response -
/// see <see cref="EacVariantParser"/>. <see langword="null"/>/empty means "use the project's own
/// `EacVariantDefault"; this only ever changes <c>selectedVariant</c> in the response - every
/// computable variant is always returned regardless (design.md §1.1).</para>
/// </summary>
public sealed record GetEvmQuery(Guid ProjectId, DateTimeOffset? DataDate, string? EacVariant)
    : IRequest<Result<EvmResponseDto>>;
