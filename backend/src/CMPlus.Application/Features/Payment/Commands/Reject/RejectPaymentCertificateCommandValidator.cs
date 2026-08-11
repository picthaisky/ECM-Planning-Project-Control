using FluentValidation;

namespace CMPlus.Application.Features.Payment.Commands.Reject;

public sealed class RejectPaymentCertificateCommandValidator : AbstractValidator<RejectPaymentCertificateCommand>
{
    public RejectPaymentCertificateCommandValidator()
    {
        RuleFor(x => x.PaymentCertificateId).NotEmpty();

        // approval-workflow.md §4 rule 2: comment is mandatory on Reject.
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(2000);
    }
}
