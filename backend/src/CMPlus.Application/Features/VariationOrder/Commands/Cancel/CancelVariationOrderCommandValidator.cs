using FluentValidation;

namespace CMPlus.Application.Features.VariationOrder.Commands.Cancel;

public sealed class CancelVariationOrderCommandValidator : AbstractValidator<CancelVariationOrderCommand>
{
    public CancelVariationOrderCommandValidator()
    {
        RuleFor(x => x.VariationOrderId).NotEmpty();
        RuleFor(x => x.Comment).NotEmpty();
    }
}
