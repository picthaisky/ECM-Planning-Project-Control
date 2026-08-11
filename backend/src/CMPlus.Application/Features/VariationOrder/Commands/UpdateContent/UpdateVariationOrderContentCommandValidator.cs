using FluentValidation;

namespace CMPlus.Application.Features.VariationOrder.Commands.UpdateContent;

public sealed class UpdateVariationOrderContentCommandValidator : AbstractValidator<UpdateVariationOrderContentCommand>
{
    public UpdateVariationOrderContentCommandValidator()
    {
        RuleFor(x => x.VariationOrderId).NotEmpty();
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Justification).MaximumLength(4000);
        RuleFor(x => x.Amount).Must(a => a == Math.Round(a, 2)).WithMessage("Amount cannot have more than 2 decimal places.");

        RuleForEach(x => x.ScopeItems).ChildRules(item =>
        {
            item.RuleFor(i => i.ActivityId).NotEmpty();
            item.RuleFor(i => i.BudgetCostDelta).NotEqual(0m);
            item.RuleFor(i => i.BudgetCostDelta).Must(a => a == Math.Round(a, 2))
                .WithMessage("BudgetCostDelta cannot have more than 2 decimal places.");
        });
    }
}
