using CMPlus.Domain.Enums;
using FluentValidation;

namespace CMPlus.Application.Features.Weather.Commands.RecordWeatherLogCorrection;

public sealed class RecordWeatherLogCorrectionCommandValidator : AbstractValidator<RecordWeatherLogCorrectionCommand>
{
    public RecordWeatherLogCorrectionCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.CorrectsWeatherLogId).NotEmpty();

        RuleFor(x => x.EntryKind)
            .Must(k => k is WeatherLogEntryKind.Correction or WeatherLogEntryKind.Retraction)
            .WithMessage("EntryKind must be Correction or Retraction.");

        // domain-rules.md §8.2 rule 5: mandatory reason. Q8's ruling: no countersignature, a
        // mandatory reason is what keeps the chain self-evidencing.
        RuleFor(x => x.CorrectionReason).NotEmpty().MaximumLength(500);

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

        RuleFor(x => x.HoursLost)
            .NotNull()
            .When(x => x.Impact != WeatherImpact.NoImpact)
            .WithMessage("HoursLost is required when Impact is not NoImpact.");

        RuleForEach(x => x.AffectedActivityIds).NotEmpty();
        RuleFor(x => x.AffectedActivityIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("AffectedActivityIds cannot contain duplicates.");
    }
}
