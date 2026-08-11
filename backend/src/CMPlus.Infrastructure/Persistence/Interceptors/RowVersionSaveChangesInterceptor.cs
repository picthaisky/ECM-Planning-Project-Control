using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CMPlus.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Generates a fresh <c>RowVersion</c> value on every Add/Modify of <see cref="PaymentCertificate"/>
/// (S9-BE-01), <see cref="VariationOrder"/> (S10-BE-01) or <see cref="Project"/> (sprint-10 security
/// review H-03 - identical shape, all three aggregates carry the same SQL Server <c>rowversion</c>
/// optimistic-concurrency token, design.md §3's <c>IsRowVersion()</c>) - needed because, unlike SQL
/// Server, the EF Core InMemory provider has no database engine of its own to auto-increment a
/// <c>rowversion</c>/<c>timestamp</c> column, so a bare <c>IsRowVersion()</c> configuration alone
/// leaves the value permanently empty under InMemory and the concurrency check never has anything to
/// disagree about (verified empirically - see the Sprint 9 backend-developer report). Without
/// <see cref="Project"/> stamped here too, H-03's fix would be real on SQL Server but structurally
/// invisible to every InMemory-backed test - including the ones that are the only way this
/// Docker-outage environment can execute mutation evidence for it at all.
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

        // S10-BE-01: same InMemory-provider gap, same fix, for the VariationOrder aggregate.
        foreach (var entry in context.ChangeTracker.Entries<VariationOrder>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(nameof(VariationOrder.RowVersion)).CurrentValue = Guid.NewGuid().ToByteArray();
            }
        }

        // Sprint-10 security review H-03: same InMemory-provider gap, same fix, for Project - the
        // aggregate two concurrent VariationOrder/PaymentCertificate final approvals race on.
        foreach (var entry in context.ChangeTracker.Entries<Project>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(nameof(Project.RowVersion)).CurrentValue = Guid.NewGuid().ToByteArray();
            }
        }

        // S11-BE-03 (domain-rules.md weather-eot §9.2): same InMemory-provider gap, same fix, for
        // IssueLog - two site users tapping "advance" on the same issue race here.
        foreach (var entry in context.ChangeTracker.Entries<IssueLog>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(nameof(IssueLog.RowVersion)).CurrentValue = Guid.NewGuid().ToByteArray();
            }
        }
    }
}
