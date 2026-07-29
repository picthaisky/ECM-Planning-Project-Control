using CMPlus.Domain.Common;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Entities;

/// <summary>
/// Append-only audit trail row (S2-BE-02), tenant-owned per ADR-0002. Written by
/// <c>AuditSaveChangesInterceptor</c> for every successful Create/Update/Delete detected by EF
/// Core's change tracker - never for a command that never reaches <c>SaveChanges</c> (e.g. one
/// FluentValidation rejects first). Deliberately has no mutator methods: a correction is a new
/// row, never an edit of an existing one (same append-only pattern as
/// <see cref="ActivityProgressLog"/>).
/// </summary>
public sealed class AuditLog : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }

    public string EntityName { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }

    public AuditAction Action { get; private set; }

    /// <summary>Null for a system/seed-time mutation with no authenticated actor.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Serialized property snapshot before the change; null for a <see cref="AuditAction.Created"/> row.</summary>
    public string? BeforeJson { get; private set; }

    /// <summary>Serialized property snapshot after the change; null for a <see cref="AuditAction.Deleted"/> row.</summary>
    public string? AfterJson { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    // EF Core materialization fallback - see Project.cs's remark on why every entity keeps one.
    private AuditLog()
    {
    }

    public AuditLog(
        Guid tenantId,
        string entityName,
        Guid entityId,
        AuditAction action,
        Guid? userId,
        string? beforeJson,
        string? afterJson,
        DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new DomainException("AuditLog.EntityName is required.");
        }

        TenantId = tenantId;
        EntityName = entityName;
        EntityId = entityId;
        Action = action;
        UserId = userId;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        Timestamp = timestamp;
    }
}
