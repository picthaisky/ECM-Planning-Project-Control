using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.CashFlow.Queries.GetCashFlow;

/// <summary>
/// S8-BE-01 (US-8.2): `GET /api/v1/projects/{projectId}/cash-flow?dataDate=&amp;from=`.
///
/// <para><paramref name="DataDate"/> is the same "as of" concept <c>GetEvmQuery</c> already
/// establishes - optional, defaults to `Project.DataDate` - and is <b>also</b> the upper bound of
/// the period-bars window (there is no separate `to`; see the handler's remarks for why collapsing
/// these into one date is a deliberate simplification, not an oversight).</para>
///
/// <para><paramref name="From"/> optionally bounds the lower edge of the period-bars window (e.g.
/// "last 6 months" on the Cash Flow screen); omitted means "since project inception" - the same
/// nullable-means-unbounded convention <c>ListEvmSnapshotsQuery</c> already uses. It never affects
/// the cumulative headline figures, which always reflect the full project history up to the
/// effective data date.</para>
/// </summary>
public sealed record GetCashFlowQuery(Guid ProjectId, DateTimeOffset? DataDate, DateTimeOffset? From)
    : IRequest<Result<CashFlowResponseDto>>;
