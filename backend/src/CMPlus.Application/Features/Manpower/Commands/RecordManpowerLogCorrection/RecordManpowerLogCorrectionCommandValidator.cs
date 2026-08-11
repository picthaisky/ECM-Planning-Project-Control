using CMPlus.Domain.Enums;
using FluentValidation;

namespace CMPlus.Application.Features.Manpower.Commands.RecordManpowerLogCorrection;

public sealed class RecordManpowerLogCorrectionCommandValidator : AbstractValidator<RecordManpowerLogCorrectionCommand>
{
    public RecordManpowerLogCorrectionCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.CorrectsLogId).NotEmpty();

        RuleFor(x => x.EntryKind)
            .Must(k => k is ManpowerLogEntryKind.Correction or ManpowerLogEntryKind.Retraction)
            .WithMessage("EntryKind must be Correction or Retraction.");

        // §4.7 rule 5: mandatory reason - no countersignature, but a reason is what keeps the chain
        // self-evidencing (mirrors weather-eot §8.2 rule 5's identical ruling).
        RuleFor(x => x.CorrectionReason).NotEmpty().MaximumLength(500);

        RuleFor(x => x.LogDate).NotEqual(default(DateTimeOffset));
        RuleFor(x => x.Shift).IsInEnum();
        RuleFor(x => x.WorkCategoryId).NotEmpty();
        RuleFor(x => x.LabourType).IsInEnum();
        RuleFor(x => x.SubcontractorRef).MaximumLength(100);
        RuleFor(x => x.WorkDescription).MaximumLength(500);

        RuleFor(x => x.WorkerCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ManHours).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.OvertimeHours).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.EquipmentCount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.EquipmentOperatingHours).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.EquipmentStandbyHours).GreaterThanOrEqualTo(0m);

        RuleFor(x => x)
            .Must(x => x.ManHours <= x.WorkerCount * 24.00m)
            .WithMessage("ManHours cannot exceed WorkerCount * 24.00.");
        RuleFor(x => x)
            .Must(x => x.ManHours == 0m || x.WorkerCount > 0)
            .WithMessage("ManHours > 0 requires WorkerCount > 0.");
        RuleFor(x => x)
            .Must(x => x.OvertimeHours <= x.ManHours)
            .WithMessage("OvertimeHours cannot exceed ManHours.");
        RuleFor(x => x)
            .Must(x => x.EquipmentOperatingHours + x.EquipmentStandbyHours <= x.EquipmentCount * 24.00m)
            .WithMessage("EquipmentOperatingHours + EquipmentStandbyHours cannot exceed EquipmentCount * 24.00.");
    }
}
