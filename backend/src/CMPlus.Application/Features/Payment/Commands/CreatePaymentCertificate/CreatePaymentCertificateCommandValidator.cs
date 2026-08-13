using FluentValidation;

namespace CMPlus.Application.Features.Payment.Commands.CreatePaymentCertificate;

/// <summary>
/// Shape-only validation (400 before the handler runs). The money-rule guards that a 400 cannot
/// express - retention/advance rate configured, monotonic cumulative vs. the auto-derived previous -
/// live in <see cref="CMPlus.Application.Services.Payment.CertificateCalculator"/> and surface as 422
/// from the handler, never duplicated here (they depend on state this validator cannot see).
/// </summary>
public sealed class CreatePaymentCertificateCommandValidator : AbstractValidator<CreatePaymentCertificateCommand>
{
    public CreatePaymentCertificateCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();

        RuleFor(x => x.MilestoneNo).GreaterThan(0);

        RuleFor(x => x.MilestoneValue).GreaterThanOrEqualTo(0m);

        RuleFor(x => x.ThisCumulativeApprovePct).InclusiveBetween(0m, 100m);

        RuleFor(x => x.ClaimPct).InclusiveBetween(0m, 100m).When(x => x.ClaimPct is not null);

        RuleFor(x => x.ActualProgressPct).InclusiveBetween(0m, 100m).When(x => x.ActualProgressPct is not null);

        RuleFor(x => x.ManualAdvanceRecoveryAmount)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.ManualAdvanceRecoveryAmount is not null);
    }
}
