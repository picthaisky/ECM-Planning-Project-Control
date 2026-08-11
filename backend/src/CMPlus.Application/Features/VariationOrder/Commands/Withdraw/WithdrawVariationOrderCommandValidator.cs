using FluentValidation;

namespace CMPlus.Application.Features.VariationOrder.Commands.Withdraw;

public sealed class WithdrawVariationOrderCommandValidator : AbstractValidator<WithdrawVariationOrderCommand>
{
    public WithdrawVariationOrderCommandValidator()
    {
        RuleFor(x => x.VariationOrderId).NotEmpty();
    }
}
