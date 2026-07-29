using System.Text.Json;
using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CMPlus.Infrastructure.Persistence.Interceptors;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> writing one <see cref="AuditLog"/> row per
/// successful Create/Update/Delete detected on the change tracker (S2-BE-02). Runs from within
/// <c>SaveChanges(Async)</c> only, so a command that fails validation (FluentValidation, a thrown
/// <c>DomainException</c>, etc.) before ever calling <c>SaveChanges</c> writes nothing - there is
/// no interception point to reach. New <see cref="AuditLog"/> rows are added directly onto the
/// same <see cref="DbContext"/>'s change tracker during <c>SavingChanges(Async)</c>, which EF Core
/// picks up and persists in the same batch/transaction as the changes being audited - a
/// documented, supported interceptor pattern.
/// </summary>
public sealed class AuditSaveChangesInterceptor(
    ITenantProvider tenantProvider, ICurrentUserContext currentUser, IDateTimeProvider clock)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditLogs(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AddAuditLogs(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditLogs(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // S3-BE-04 bulk-import escape hatch (see CmPlusDbContext.SuppressPerEntityAudit) - the
        // caller is responsible for adding its own single summarizing AuditLog row in this case.
        if (context is CmPlusDbContext { SuppressPerEntityAudit: true })
        {
            return;
        }

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Deleted => AuditAction.Deleted,
                _ => AuditAction.Updated,
            };

            var beforeJson = action == AuditAction.Created ? null : Serialize(entry, useOriginalValues: true);
            var afterJson = action == AuditAction.Deleted ? null : Serialize(entry, useOriginalValues: false);

            var log = new AuditLog(
                tenantProvider.TenantId,
                entry.Entity.GetType().Name,
                TryGetEntityId(entry),
                action,
                currentUser.UserId,
                beforeJson,
                afterJson,
                clock.UtcNow);

            context.Add(log);
        }
    }

    private static Guid TryGetEntityId(EntityEntry entry)
    {
        var idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == nameof(Entity.Id));
        return idProperty?.CurrentValue is Guid id ? id : Guid.Empty;
    }

    /// <summary>Property names never written to <see cref="AuditLog"/> in cleartext (S2-SEC-01
    /// finding M-01): a leaked/queried audit trail must not become a second place credential
    /// material can be recovered from, even hashed - <c>AuditLog.BeforeJson</c>/<c>AfterJson</c>
    /// has none of the access restrictions or rotation discipline that `User.PasswordHash`'s own
    /// column has. Matched by property name only (not per-entity-type), so a future sensitive
    /// field (e.g. a refresh token) is covered by adding its name here once, not per call site.</summary>
    private static readonly HashSet<string> RedactedPropertyNames = new(StringComparer.Ordinal)
    {
        nameof(User.PasswordHash),
    };

    private const string RedactedPlaceholder = "***REDACTED***";

    /// <summary>Serializes only scalar/primitive properties (EF Core's <c>EntityEntry.Properties</c>
    /// never includes navigations), so this can never produce a cyclic reference graph.</summary>
    private static string Serialize(EntityEntry entry, bool useOriginalValues)
    {
        var snapshot = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            snapshot[property.Metadata.Name] = RedactedPropertyNames.Contains(property.Metadata.Name)
                ? RedactedPlaceholder
                : useOriginalValues ? property.OriginalValue : property.CurrentValue;
        }

        return JsonSerializer.Serialize(snapshot);
    }
}
