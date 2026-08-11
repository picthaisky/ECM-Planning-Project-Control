using FluentValidation;

namespace CMPlus.Application.Features.VariationOrder.Commands.ReturnForRevision;

public sealed class ReturnVariationOrderForRevisionCommandValidator : AbstractValidator<ReturnVariationOrderForRevisionCommand>
{
    public ReturnVariationOrderForRevisionCommandValidator()
    {
        RuleFor(x => x.VariationOrderId).NotEmpty();
        RuleFor(x => x.Comment).NotEmpty();
    }
}
