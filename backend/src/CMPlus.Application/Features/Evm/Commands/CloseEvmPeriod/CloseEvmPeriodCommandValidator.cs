using FluentValidation;

namespace CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;

public sealed class CloseEvmPeriodCommandValidator : AbstractValidator<CloseEvmPeriodCommand>
{
    public CloseEvmPeriodCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.DataDate).NotEqual(default(DateTimeOffset));
    }
}
