using FluentValidation;

namespace CMPlus.Application.Features.Payment.Commands.Submit;

public sealed class SubmitPaymentCertificateCommandValidator : AbstractValidator<SubmitPaymentCertificateCommand>
{
    public SubmitPaymentCertificateCommandValidator()
    {
        RuleFor(x => x.PaymentCertificateId).NotEmpty();
    }
}
