using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using MediatR;

namespace CMPlus.Application.Features.Manpower.Commands.RecordManpowerLog;

/// <summary>
/// S12-BE-02: posts one immutable <see cref="ManpowerEquipmentLog"/> Original row. Every existence
/// check happens before anything is constructed (mirrors <c>RecordWeatherLogCommandHandler</c>'s
/// "check everything, then mutate" ordering) so a bad reference never reaches the append-only log at
/// all.
/// </summary>
public sealed class RecordManpowerLogCommandHandler(
    IManpowerEquipmentLogRepository repository, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IRequestHandler<RecordManpowerLogCommand, Result<ManpowerLogDto>>
{
    public async Task<Result<ManpowerLogDto>> Handle(RecordManpowerLogCommand request, CancellationToken cancellationToken)
    {
        if (!await repository.ProjectExistsAsync(request.ProjectId, cancellationToken))
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.ProjectNotFound);
        }

        // L-01 fix pattern (this task's brief): fail closed on a null actor id rather than
        // fabricating Guid.Empty - structurally unreachable behind [Authorize] but never trusted
        // here either.
        if (currentUser.UserId is not { } recordedByUserId)
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.ActorRequired);
        }

        var existingCategoryIds = await repository.FindExistingWorkCategoryIdsAsync(
            request.ProjectId, [request.WorkCategoryId], cancellationToken);
        if (existingCategoryIds.Count == 0)
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.WorkCategoryNotInProject);
        }

        if (request.WbsNodeId is { } wbsNodeId)
        {
            var scopeResult = await ResolveScopeAsync(
                wbsNodeId,
                idsInProject: await repository.FindExistingWbsNodeIdsAsync(request.ProjectId, [wbsNodeId], cancellationToken),
                idsInTenant: () => repository.FindWbsNodeIdsInTenantAsync([wbsNodeId], cancellationToken),
                ManpowerLogErrorCodes.WbsNodeNotInProject,
                ManpowerLogErrorCodes.WbsNodeNotFound);
            if (scopeResult is not null)
            {
                return Result<ManpowerLogDto>.Failure(scopeResult);
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

        // §4.1's last validation rule: one row, one attribution.
        if (request.ActivityId is not null && request.WbsNodeId is not null && activityOwnWbsNodeId != request.WbsNodeId)
        {
            return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.ActivityWbsNodeMismatch);
        }

        // §4.4/Q8: warn-and-confirm, not a hard block.
        if (!request.AllowDuplicate)
        {
            var duplicateExists = await repository.HasInForceOriginalForNaturalKeyAsync(
                request.ProjectId, request.LogDate, request.Shift, request.WorkCategoryId, request.WbsNodeId,
                request.LabourType, request.SubcontractorRef, cancellationToken);
            if (duplicateExists)
            {
                return Result<ManpowerLogDto>.Failure(ManpowerLogErrorCodes.AlreadyExists);
            }
        }

        var log = ManpowerEquipmentLog.CreateOriginal(
            tenantProvider.TenantId,
            request.ProjectId,
            request.LogDate,
            request.Shift,
            request.WorkCategoryId,
            request.WbsNodeId,
            request.ActivityId,
            request.LabourType,
            request.SubcontractorRef,
            request.WorkerCount,
            request.ManHours,
            request.OvertimeHours,
            request.ManHoursDerived,
            request.EquipmentCount,
            request.EquipmentOperatingHours,
            request.EquipmentStandbyHours,
            request.WorkDescription,
            request.RelatedWeatherLogId,
            recordedByUserId,
            clock.UtcNow,
            request.AllowDuplicate);

        await repository.AddAsync(log, cancellationToken);

        return Result<ManpowerLogDto>.Success(ManpowerLogDto.From(log));
    }

    /// <summary>Shared cross-tenant/wrong-project distinction (ADR-0002, fixture M-14a/b) - a
    /// same-tenant id belonging to a different project is a 422; an id absent from this tenant
    /// entirely (cross-tenant or genuinely unknown) is a 404, indistinguishable from "does not
    /// exist".</summary>
    private static async Task<string?> ResolveScopeAsync(
        Guid id,
        IReadOnlyList<Guid> idsInProject,
        Func<Task<IReadOnlyList<Guid>>> idsInTenant,
        string notInProjectErrorCode,
        string notFoundErrorCode)
    {
        if (idsInProject.Contains(id))
        {
            return null;
        }

        var inTenant = await idsInTenant();
        return inTenant.Contains(id) ? notInProjectErrorCode : notFoundErrorCode;
    }
}
