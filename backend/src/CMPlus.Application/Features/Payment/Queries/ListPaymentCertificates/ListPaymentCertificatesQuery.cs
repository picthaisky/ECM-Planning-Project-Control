using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Payment.Queries.ListPaymentCertificates;

/// <summary>
/// `GET /api/v1/projects/{projectId}/payment-certificates` (docs/9 §6; design.md §2.3's "identical
/// shape" document-workflow family; S9 read-side gap closure, finding L-04) - the milestone/
/// certificate table the Payment Certificate screen needs
/// (`web/src/features/payment/api.ts#listPaymentCertificates`, already built front-end-side and
/// waiting on this endpoint). Newest-first (see <see cref="Abstractions.IPaymentCertificateRepository.ListByProjectAsync"/>).
///
/// <para>Project-scoped by <see cref="ProjectId"/> in addition to the ambient tenant filter
/// (ADR-0002) - security review sprint-09.md M-02 noted this aggregate's transition routes are flat
/// (`/payment-certificates/{id}/...`, no project to cross-check); this list route carries a real
/// `projectId` and scopes the query by it, which is strictly better than the flat-route situation
/// the review criticised, though it does not itself close M-02 (same-tenant-wrong-project
/// authorization on the id-scoped transition actions remains a tracked, open product decision).</para>
/// </summary>
public sealed record ListPaymentCertificatesQuery(Guid ProjectId) : IRequest<Result<IReadOnlyList<PaymentCertificateDto>>>;
