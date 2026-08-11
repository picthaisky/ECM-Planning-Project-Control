using FluentValidation;

namespace CMPlus.Application.Features.Payment.Commands.RecordPayment;

public sealed class RecordPaymentForPaymentCertificateCommandValidator : AbstractValidator<RecordPaymentForPaymentCertificateCommand>
{
    public RecordPaymentForPaymentCertificateCommandValidator()
    {
        RuleFor(x => x.PaymentCertificateId).NotEmpty();

        // PaymentCertificateConfiguration.PaymentReference is HasMaxLength(64).
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(64);

        RuleFor(x => x.PaidAt).NotEqual(default(DateTimeOffset));
    }
}
