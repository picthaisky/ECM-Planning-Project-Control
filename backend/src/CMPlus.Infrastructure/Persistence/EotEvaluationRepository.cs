using System.Text.Json;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Services.Eot;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CMPlus.Infrastructure.Persistence;

/// <summary>S11-BE-02: <see cref="IEotEvaluationRepository"/> against <see cref="CmPlusDbContext"/>.
/// <see cref="SaveAsync"/> mirrors <c>CpmScheduleRepository.SaveResultsAsync</c>'s
/// <c>SuppressPerEntityAudit</c> pattern - domain-rules.md weather-eot §2.1's zero-side-effect
/// boundary (fixture W-14) requires exactly one summarizing <see cref="AuditLog"/> row, never one per
/// <see cref="EotEvaluationRun"/>/<see cref="EotEvaluationSource"/>/<see cref="EotEvaluationDriver"/>
/// child (which for a long-running project's weather history could be hundreds of rows).</summary>
public sealed class EotEvaluationRepository(
    CmPlusDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : IEotEvaluationRepository
{
    public async Task<EotProjectContext?> GetProjectContextAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.ContractStart, p.DataDate })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new EotProjectContext(row.ContractStart, row.DataDate);
    }

    public async Task<EotCalendarContext?> GetDefaultCalendarAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var calendar = await dbContext.Calendars
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.IsDefault, cancellationToken);

        if (calendar is null)
        {
            return null;
        }

        var exceptions = await dbContext.CalendarExceptions
            .AsNoTracking()
            .Where(e => e.CalendarId == calendar.Id)
            .ToListAsync(cancellationToken);

        return new EotCalendarContext(calendar.WorkingDays, exceptions);
    }

    public Task<ProjectEotPolicy?> GetPolicyAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        dbContext.ProjectEotPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, EotActivityContext>> GetActivityContextsAsync(
        Guid projectId, IReadOnlyCollection<Guid> activityIds, CancellationToken cancellationToken = default)
    {
        if (activityIds.Count == 0)
        {
            return new Dictionary<Guid, EotActivityContext>();
        }

        var idList = activityIds.ToList();

        var rows = await dbContext.Activities
            .AsNoTracking()
            .Where(a => idList.Contains(a.Id) && dbContext.WBSNodes.Any(n => n.Id == a.WbsNodeId && n.ProjectId == projectId))
            .Select(a => new EotActivityContext(a.Id, a.ActivityCode, a.Name, a.ActualStart, a.PlannedStart, a.ActualFinish))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(a => a.ActivityId);
    }

    public async Task SaveAsync(EotEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        // A single new aggregate (the evaluation plus its owned Runs/Sources/Drivers) - an ordinary
        // tracked Add, exactly like CpmRun's own capture (never the CpmScheduleRepository raw-SQL
        // OPENJSON technique, which has nothing to do with this write at all).
        dbContext.EotEvaluations.Add(evaluation);

        dbContext.SuppressPerEntityAudit = true;
        try
        {
            // Domain-rules.md §2.1/fixture W-14: exactly ONE summarizing AuditLog row for the whole
            // evaluation - not one per Run/Source/Driver child, which for a long-running project's
            // weather history could be hundreds of rows. Anchored on the EotEvaluation itself (unlike
            // CpmScheduleRepository's Project-anchored row): this write never touches any other
            // aggregate at all, so there is no broader "what changed" to summarize.
            dbContext.AuditLogs.Add(new AuditLog(
                tenantProvider.TenantId, nameof(EotEvaluation), evaluation.Id, AuditAction.Created, currentUser.UserId,
                beforeJson: null,
                afterJson: JsonSerializer.Serialize(new
                {
                    evaluation.ProjectId,
                    evaluation.WindowStart,
                    evaluation.WindowEnd,
                    evaluation.CriticalityBasis,
                    evaluation.Confidence,
                    evaluation.EotEligibleDays,
                }),
                clock.UtcNow));

            // Persists both the EotEvaluation graph (Added, tracked above) and the one AuditLog row -
            // SuppressPerEntityAudit means AuditSaveChangesInterceptor does not also add one AuditLog
            // row per child.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.SuppressPerEntityAudit = false;
        }
    }
}
