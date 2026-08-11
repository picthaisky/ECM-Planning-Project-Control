using FluentValidation;

namespace CMPlus.Application.Features.Gantt.Queries.GetGantt;

public sealed class GetGanttQueryValidator : AbstractValidator<GetGanttQuery>
{
    public GetGanttQueryValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();

        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From.Value <= x.To.Value)
            .WithMessage("From must be less than or equal to To.")
            .WithName("DateRange");
    }
}
