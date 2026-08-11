using FluentValidation;

namespace CMPlus.Application.Features.VariationOrder.Commands.Submit;

public sealed class SubmitVariationOrderCommandValidator : AbstractValidator<SubmitVariationOrderCommand>
{
    public SubmitVariationOrderCommandValidator()
    {
        RuleFor(x => x.VariationOrderId).NotEmpty();
    }
}
