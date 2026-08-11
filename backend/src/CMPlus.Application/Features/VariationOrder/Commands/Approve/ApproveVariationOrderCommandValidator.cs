using FluentValidation;

namespace CMPlus.Application.Features.VariationOrder.Commands.Approve;

public sealed class ApproveVariationOrderCommandValidator : AbstractValidator<ApproveVariationOrderCommand>
{
    public ApproveVariationOrderCommandValidator()
    {
        RuleFor(x => x.VariationOrderId).NotEmpty();
    }
}
