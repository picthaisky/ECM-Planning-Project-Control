using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Payment.Queries.ListPaymentCertificates;

/// <summary>
/// Never 404s on an unknown/cross-tenant/other-project <see cref="ListPaymentCertificatesQuery.ProjectId"/> -
/// returns an empty list instead, the established precedent for list reads in this codebase
/// (<c>ListEvmSnapshotsQueryHandler</c>, <c>GetJobHistoryAsync</c> - confirmed non-leaking in
/// `docs/security/reviews/sprint-03.md` §5a): the global tenant query filter (ADR-0002) plus this
/// query's own <c>ProjectId</c> scoping already make "wrong tenant", "right tenant/wrong project" and
/// "does not exist" all produce the identical empty result, so there is nothing to leak and no need
/// to pay for a separate project-existence round trip.
///
/// <para>Deliberately does not compute per-row quorum progress
/// (<see cref="PaymentCertificateDto.CurrentStepApprovalsCollected"/> stays <see langword="null"/>
/// for every row here, unlike <c>GetPaymentCertificateQueryHandler</c>): doing it per row would be
/// exactly the N+1 pattern this codebase's own read models forbid (`WbsTreeReader`'s own remarks),
/// and a single batched second query (group <c>ApprovalAction</c> by
/// <c>(DocumentId, RevisionNo, StepNo)</c> across every certificate in the result, the same
/// two-query shape <c>WbsTreeReader.GetNodesWithActivityCountsAsync</c> uses) is a reasonable
/// fast-follow but is more than this "thin wrapper" gap-closure needs today - the prototype's
/// milestone *table* only needs status/amounts per row; the quorum-bearing chain bar
/// (`ApprovalChainBar.tsx`) is a single-certificate *detail* view fed by
/// <c>GET /payment-certificates/{id}</c>, which does supply the real count.</para>
/// </summary>
public sealed class ListPaymentCertificatesQueryHandler(IPaymentCertificateRepository certificates)
    : IRequestHandler<ListPaymentCertificatesQuery, Result<IReadOnlyList<PaymentCertificateDto>>>
{
    public async Task<Result<IReadOnlyList<PaymentCertificateDto>>> Handle(
        ListPaymentCertificatesQuery request, CancellationToken cancellationToken)
    {
        var rows = await certificates.ListByProjectAsync(request.ProjectId, cancellationToken);

        return Result<IReadOnlyList<PaymentCertificateDto>>.Success(
            rows.Select(c => PaymentCertificateDto.From(c)).ToList());
    }
}
