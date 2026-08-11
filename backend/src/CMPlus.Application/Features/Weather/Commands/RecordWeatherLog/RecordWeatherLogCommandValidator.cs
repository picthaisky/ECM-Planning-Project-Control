using FluentValidation;

namespace CMPlus.Application.Features.Weather.Commands.RecordWeatherLog;

public sealed class RecordWeatherLogCommandValidator : AbstractValidator<RecordWeatherLogCommand>
{
    public RecordWeatherLogCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.LogDate).NotEqual(default(DateTimeOffset));
        RuleFor(x => x.Condition).IsInEnum();
        RuleFor(x => x.ConditionNote).MaximumLength(200);
        RuleFor(x => x.Impact).IsInEnum();
        RuleFor(x => x.ImpactNote).MaximumLength(500);

        RuleFor(x => x.RainfallMm).GreaterThanOrEqualTo(0m).When(x => x.RainfallMm.HasValue)
            .WithMessage("RainfallMm cannot be negative.");
        RuleFor(x => x.RainfallMm)
            .Must(v => v!.Value == Math.Round(v.Value, 2, MidpointRounding.AwayFromZero))
            .When(x => x.RainfallMm.HasValue)
            .WithMessage("RainfallMm cannot have more than 2 decimal places.");

        RuleFor(x => x.HoursLost).InclusiveBetween(0m, 24m).When(x => x.HoursLost.HasValue)
            .WithMessage("HoursLost must be between 0 and 24.");

        // domain-rules.md §3.4: "HoursLost is required by FluentValidation when Impact <> NoImpact".
        RuleFor(x => x.HoursLost)
            .NotNull()
            .When(x => x.Impact != Domain.Enums.WeatherImpact.NoImpact)
            .WithMessage("HoursLost is required when Impact is not NoImpact.");

        RuleForEach(x => x.AffectedActivityIds).NotEmpty();
        RuleFor(x => x.AffectedActivityIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("AffectedActivityIds cannot contain duplicates.");
    }
}
