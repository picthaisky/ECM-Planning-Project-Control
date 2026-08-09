using CMPlus.Domain.Common;
using FluentValidation;

namespace CMPlus.Application.Features.ActualCosts.Commands.RecordActualCost;

public sealed class RecordActualCostCommandValidator : AbstractValidator<RecordActualCostCommand>
{
    public RecordActualCostCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.CostCategory).IsInEnum();
        RuleFor(x => x.EntryType).IsInEnum();
        RuleFor(x => x.IncurredDate).NotEqual(default(DateTimeOffset));

        // actual-cost.md §7.6: a zero cost entry, including a zero reversal, is noise - rejected
        // outright rather than silently accepted.
        RuleFor(x => x.Amount).NotEqual(0m).WithMessage("Amount cannot be zero.");

        // actual-cost.md §7.6: manual entry rejects more-than-2dp input outright; only the
        // (not-yet-built) import path rounds it and logs the adjustment instead.
        RuleFor(x => x.Amount)
            .Must(a => a == RoundingRules.ToMoney(a))
            .WithMessage("Amount cannot have more than 2 decimal places.");

        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(0m)
            .When(x => x.Quantity.HasValue)
            .WithMessage("Quantity cannot be negative.");

        RuleFor(x => x.DocumentReference).MaximumLength(64);
        RuleFor(x => x.CostCode).MaximumLength(32);
        RuleFor(x => x.VendorName).MaximumLength(200);
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.UnitOfMeasure).MaximumLength(16);

        // Whether a Note is *mandatory* (posting into an already-closed EVM period,
        // actual-cost.md §6.6) needs a DB read the validator cannot see - enforced in
        // RecordActualCostCommandHandler via ActualCostErrorCodes.NoteRequiredForClosedPeriod
        // instead of here.
    }
}
