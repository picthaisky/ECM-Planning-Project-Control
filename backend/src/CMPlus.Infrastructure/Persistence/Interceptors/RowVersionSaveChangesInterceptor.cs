using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CMPlus.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Generates a fresh <see cref="PaymentCertificate.RowVersion"/> value on every Add/Modify (S9-BE-01,
/// design.md §3's <c>IsRowVersion()</c> optimistic-concurrency token) - needed because, unlike SQL
/// Server, the EF Core InMemory provider has no database engine of its own to auto-increment a
/// <c>rowversion</c>/<c>timestamp</c> column, so a bare <c>IsRowVersion()</c> configuration alone
/// leaves the value permanently empty under InMemory and the concurrency check never has anything to
/// disagree about (verified empirically - see the Sprint 9 backend-developer report).
///
/// <para><b>Harmless against real SQL Server:</b> a server-generated <c>rowversion</c> column
/// silently discards whatever value a client sends on INSERT/UPDATE - the database engine always
/// computes its own value, and EF reads that back afterwards because the property is
/// <c>ValueGeneratedOnAddOrUpdate</c>. This interceptor's client-side assignment is therefore always
/// superseded on SQL Server, never a source of divergence between the two providers.</para>
/// </summary>
public sealed class RowVersionSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<PaymentCertificate>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(nameof(PaymentCertificate.RowVersion)).CurrentValue = Guid.NewGuid().ToByteArray();
            }
        }
    }
}
