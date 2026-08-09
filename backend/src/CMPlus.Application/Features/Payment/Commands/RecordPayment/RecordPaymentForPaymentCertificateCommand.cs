using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.RecordPayment;

/// <summary>
/// <c>[Certified] --RecordPayment--&gt; [Paid]</c> (approval-workflow.md §4): records the actual
/// disbursement. Not chain-gated (there is no pending step once <c>Certified</c>) - authorized by
/// the static role gate on <c>PaymentCertificatesController</c>, the same "certificate CRUD
/// surface" list as <c>Submit</c>.
/// </summary>
public sealed record RecordPaymentForPaymentCertificateCommand(Guid PaymentCertificateId, string Reference, DateTimeOffset PaidAt)
    : IRequest<Result<PaymentCertificateDto>>;
