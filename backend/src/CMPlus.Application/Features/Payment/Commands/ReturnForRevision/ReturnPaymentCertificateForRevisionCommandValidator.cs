using FluentValidation;

namespace CMPlus.Application.Features.Payment.Commands.ReturnForRevision;

public sealed class ReturnPaymentCertificateForRevisionCommandValidator : AbstractValidator<ReturnPaymentCertificateForRevisionCommand>
{
    public ReturnPaymentCertificateForRevisionCommandValidator()
    {
        RuleFor(x => x.PaymentCertificateId).NotEmpty();

        // approval-workflow.md §4 rule 2: comment is mandatory on ReturnForRevision.
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(2000);
    }
}
