using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using MediatR;

namespace CMPlus.Application.Features.Manpower.Commands.RecordManpowerLogCorrection;

/// <summary>
/// S12-BE-02: posts a <see cref="ManpowerLogEntryKind.Correction"/>/<see cref="ManpowerLogEntryKind.Retraction"/>
/// row. Enforces domain-rules.md (manpower-equipment) §4.7's chain-integrity rules 1/2/4 (5 is
/// FluentValidation + the Domain constructor's own belt-and-braces check) - every check happens
/// before anything is constructed, all-or-nothing, mirroring <c>RecordWeatherLogCorrectionCommandHandler</c>.
/// </summary>
public sealed class RecordManpowerLogCorrectionCommandHandler(
    IManpowerEquipmentLogRepository repository, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IRequestHandler<RecordManpowerLogCorrectionCommand, Result<ManpowerLogDto>>
{
    public async Task<Result<ManpowerLogDto>> Handle(RecordManpowerLogCorrectionCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.ProjectNotFound);
        }

        if (currentUser.UserId is not { } recordedByUserId)
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.ActorRequired);
        }

        // Rule 1: the target must resolve within the same tenant and project.
        var target = await repository.GetByIdAsync(request.ProjectId, request.CorrectsLogId, cancellationToken);
        if (target is null)
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.CorrectionTargetNotFound);
        }

        // Rule 2 (the load-bearing one): at most one entry may point at any given entry - a
        // correction must target the current chain tail, never an already-superseded entry.
        if (await repository.HasAnyCorrectionTargetingAsync(request.ProjectId, request.CorrectsLogId, cancellationToken))
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.AlreadySuperseded);
        }

        var recordedAt = clock.UtcNow;

        // Rule 4: the target must already exist and be strictly older.
        if (target.RecordedAt >= recordedAt)
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.CorrectionOrdering);
        }

        var existingCategoryIds = await repository.FindExistingWorkCategoryIdsAsync(
            request.ProjectId, [request.WorkCategoryId], cancellationToken);
        if (existingCategoryIds.Count == 0)
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.WorkCategoryNotInProject);
        }

        if (request.WbsNodeId is { } wbsNodeId)
        {
            var idsInProject = await repository.FindExistingWbsNodeIdsAsync(request.ProjectId, [wbsNodeId], cancellationToken);
            if (!idsInProject.Contains(wbsNodeId))
            {
                var idsInTenant = await repository.FindWbsNodeIdsInTenantAsync([wbsNodeId], cancellationToken);
                return Result<ManpowerLogDto>.Failure(
                    idsInTenant.Contains(wbsNodeId) ? ManpowerLogErrorCodes.WbsNodeNotInProject : ManpowerLogErrorCodes.WbsNodeNotFound);
            }
        }

        Guid? activityOwnWbsNodeId = null;
        if (request.ActivityId is { } activityId)
        {
            var activitiesWithWbsNode = await repository.FindExistingActivitiesWithWbsNodeAsync(
                request.ProjectId, [activityId], cancellationToken);

            if (!activitiesWithWbsNode.TryGetValue(activityId, out var resolvedWbsNodeId))
            {
                var inTenant = await repository.FindActivityIdsInTenantAsync([activityId], cancellationToken);
                return Result<ManpowerLogDto>.Failure(
                    inTenant.Count > 0 ? ManpowerLogErrorCodes.ActivityNotInProject : ManpowerLogErrorCodes.ActivityNotFound);
            }

            activityOwnWbsNodeId = resolvedWbsNodeId;
        }

        if (request.ActivityId is not null && request.WbsNodeId is not null && activityOwnWbsNodeId != request.WbsNodeId)
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.ActivityWbsNodeMismatch);
        }

        var entry = request.EntryKind == ManpowerLogEntryKind.Retraction
            ? ManpowerEquipmentLog.CreateRetraction(
                tenantProvider.TenantId, request.ProjectId, request.CorrectsLogId, request.CorrectionReason,
                request.LogDate, request.Shift, request.WorkCategoryId, request.WbsNodeId, request.ActivityId,
                request.LabourType, request.SubcontractorRef, request.WorkerCount, request.ManHours,
                request.OvertimeHours, request.ManHoursDerived, request.EquipmentCount,
                request.EquipmentOperatingHours, request.EquipmentStandbyHours, request.WorkDescription,
                request.RelatedWeatherLogId, recordedByUserId, recordedAt)
            : ManpowerEquipmentLog.CreateCorrection(
                tenantProvider.TenantId, request.ProjectId, request.CorrectsLogId, request.CorrectionReason,
                request.LogDate, request.Shift, request.WorkCategoryId, request.WbsNodeId, request.ActivityId,
                request.LabourType, request.SubcontractorRef, request.WorkerCount, request.ManHours,
                request.OvertimeHours, request.ManHoursDerived, request.EquipmentCount,
                request.EquipmentOperatingHours, request.EquipmentStandbyHours, request.WorkDescription,
                request.RelatedWeatherLogId, recordedByUserId, recordedAt);

        await repository.AddAsync(entry, cancellationToken);

        return Result<ManpowerLogDto>.Success(ManpowerLogDto.From(entry));
    }
}
