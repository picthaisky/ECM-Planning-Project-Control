using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Dashboard.Queries.GetDashboard;

/// <summary>
/// S8-BE-02 (US-8.1/US-8.3): `GET /api/v1/projects/{projectId}/dashboard?dataDate=` - the Executive
/// Dashboard's KPI tiles.
///
/// <para><paramref name="DataDate"/> is the same optional "as of" concept <c>GetEvmQuery</c> already
/// establishes, defaulting to `Project.DataDate` - it governs every EVM-sourced figure in the
/// response (BAC/PV/EV/AC/SV/CV/SPI/CPI/EAC/ETC/VAC). <see cref="DashboardProgressRollupDto.ProgressPercentage"/>
/// is deliberately <b>not</b> a function of this date - see <c>GetDashboardQueryHandler</c>'s
/// remarks for why.</para>
///
/// <para>There is deliberately no `eacVariant` override parameter, unlike <c>GetEvmQuery</c>: the
/// DoD requires the dashboard to show `Project.EacVariantDefault` specifically ("EAC/ETC/VAC ใช้
/// Project.EacVariantDefault") - a per-request switcher belongs to the EVM screen's own variant
/// selector, not to a KPI-tile summary.</para>
/// </summary>
public sealed record GetDashboardQuery(Guid ProjectId, DateTimeOffset? DataDate)
    : IRequest<Result<DashboardResponseDto>>;
