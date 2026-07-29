namespace CMPlus.Domain.Enums;

/// <summary>
/// The kind of mutation an <c>AuditLog</c> row records (S2-BE-02). Backs
/// <c>AuditLog.Action</c> - one row per successful Create/Update/Delete detected by
/// <c>AuditSaveChangesInterceptor</c>. Never written for a command that fails validation before
/// <c>SaveChanges</c> is reached, since the interceptor only runs from within SaveChanges itself.
/// </summary>
public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3,
}
