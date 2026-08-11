using FluentValidation;

namespace CMPlus.Application.Features.VariationOrder.Commands.Reject;

public sealed class RejectVariationOrderCommandValidator : AbstractValidator<RejectVariationOrderCommand>
{
    public RejectVariationOrderCommandValidator()
    {
        RuleFor(x => x.VariationOrderId).NotEmpty();
        RuleFor(x => x.Comment).NotEmpty();
    }
}
