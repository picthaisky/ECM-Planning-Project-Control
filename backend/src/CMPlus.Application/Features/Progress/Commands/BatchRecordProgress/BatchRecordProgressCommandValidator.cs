using FluentValidation;

namespace CMPlus.Application.Features.Progress.Commands.BatchRecordProgress;

public sealed class BatchRecordProgressCommandValidator : AbstractValidator<BatchRecordProgressCommand>
{
    public BatchRecordProgressCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Entries).NotEmpty();

        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.ActivityId).NotEmpty();
            entry.RuleFor(e => e.ProgressPercentage).InclusiveBetween(0m, 100m);
            entry.RuleFor(e => e.ActualQuantity).GreaterThanOrEqualTo(0m).When(e => e.ActualQuantity.HasValue);
        });
    }
}
