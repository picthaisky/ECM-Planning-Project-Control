using FluentValidation;

namespace CMPlus.Application.Features.VariationOrder.Commands.Create;

public sealed class CreateVariationOrderCommandValidator : AbstractValidator<CreateVariationOrderCommand>
{
    public CreateVariationOrderCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.VoNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Justification).MaximumLength(4000);

        // 400 AmountPrecision (domain-rules.md §7.3) - a request-shape concern, checked here rather
        // than only as a DomainException inside the aggregate, so it comes back as a field-level
        // validation error rather than a bare 400/500.
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
