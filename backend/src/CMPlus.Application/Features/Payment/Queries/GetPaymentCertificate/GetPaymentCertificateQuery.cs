using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Payment.Queries.GetPaymentCertificate;

/// <summary>
/// `GET /api/v1/payment-certificates/{id}` (S9 read-side gap closure, finding L-04) - single-
/// certificate reload, the natural read-only complement to the five S9-BE-05 transition commands
/// that already return this exact <see cref="PaymentCertificateDto"/> shape as their own response
/// (`web/src/features/payment/api.ts#getPaymentCertificate`, already built front-end-side, used
/// after a `409 concurrent-transition` conflict or any manual "โหลดใหม่"). Tenant-scoped by the
/// global EF query filter (ADR-0002) - a cross-tenant id resolves to <see langword="null"/>, mapped
/// to the same <see cref="PaymentApprovalErrorCodes.NotFound"/> -&gt; 404 the five mutating commands
/// already use (S9-SEC-01 confirmed this mapping makes "wrong tenant" and "does not exist"
/// indistinguishable), never a differently-shaped error.
/// </summary>
public sealed record GetPaymentCertificateQuery(Guid PaymentCertificateId) : IRequest<Result<PaymentCertificateDto>>;
