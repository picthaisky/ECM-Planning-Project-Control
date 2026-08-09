using CMPlus.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CMPlus.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Structural enforcement of "append-only" (security review sprint-09.md M-01): throws when
/// <c>SavingChanges(Async)</c> detects any <see cref="IAppendOnly"/> entity in the
/// <see cref="EntityState.Modified"/> or <see cref="EntityState.Deleted"/> state, so the guarantee
/// does not rest on developer discipline alone. Execution-verified reachable through an ordinary
/// <see cref="CmPlusDbContext"/> before this interceptor existed (review probe 7: an
/// <c>ApprovalAction</c> row was rewritten and deleted with no error at all).
///
/// <para>Applies uniformly via the <see cref="IAppendOnly"/> marker rather than a per-entity-type
/// list, so a future append-only entity is covered by implementing the marker, not by remembering to
/// add a new special case here. <see cref="EntityState.Added"/> is deliberately unaffected -
/// appending is the one operation this guarantee exists to still allow.</para>
/// </summary>
public sealed class AppendOnlyGuardInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AssertNoAppendOnlyMutation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AssertNoAppendOnlyMutation(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void AssertNoAppendOnlyMutation(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAppendOnly && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"{entry.Entity.GetType().Name} ('{entry.Property(nameof(CMPlus.Domain.Common.Entity.Id)).CurrentValue}') " +
                    $"is append-only and cannot be {entry.State}. Corrections must be a new compensating row, " +
                    "never an edit or delete (conventions.md).");
            }
        }
    }
}
