using FluentValidation;

namespace CMPlus.Application.Features.Payment.Commands.Approve;

public sealed class ApprovePaymentCertificateCommandValidator : AbstractValidator<ApprovePaymentCertificateCommand>
{
    public ApprovePaymentCertificateCommandValidator()
    {
        RuleFor(x => x.PaymentCertificateId).NotEmpty();

        // ApprovalActionConfiguration.Comment is HasMaxLength(2000).
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}
