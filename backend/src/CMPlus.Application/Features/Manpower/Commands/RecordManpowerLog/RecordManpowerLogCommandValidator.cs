using FluentValidation;

namespace CMPlus.Application.Features.Manpower.Commands.RecordManpowerLog;

/// <summary>Field-level validation ahead of the Domain constructor's own belt-and-braces checks
/// (§4.1's validation table) - gives a structured 400 validation-error response instead of a bare
/// 422 <see cref="CMPlus.Domain.Common.DomainException"/> message for the common typo cases.</summary>
public sealed class RecordManpowerLogCommandValidator : AbstractValidator<RecordManpowerLogCommand>
{
    public RecordManpowerLogCommandValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty();
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

        // §4.1 validation table, restated here for an early, structured 400.
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

        // §4.1's last rule ("an ActivityId's own WbsNodeId must equal the row's WbsNodeId when that
        // is also set") needs a DB lookup to know the activity's node - enforced in the handler
        // (ManpowerLogErrorCodes.ActivityWbsNodeMismatch), not here.
    }
}
