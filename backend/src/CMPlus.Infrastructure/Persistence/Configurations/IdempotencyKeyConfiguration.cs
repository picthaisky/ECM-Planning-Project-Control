using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="IdempotencyKey"/> (S13-DB-01).</summary>
public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("IdempotencyKeys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();

        builder.Property(k => k.TenantId).IsRequired();
        builder.Property(k => k.Key).IsRequired().HasMaxLength(IdempotencyKey.MaxKeyLength);
        builder.Property(k => k.RequestMethod).IsRequired().HasMaxLength(10);
        builder.Property(k => k.RequestPath).IsRequired().HasMaxLength(500);
        builder.Property(k => k.RequestHash).IsRequired().HasMaxLength(64); // hex-encoded SHA-256.
        builder.Property(k => k.RequestedByUserId).IsRequired();
        builder.Property(k => k.ReservedAt).IsRequired();
        builder.Property(k => k.ResponseContentType).HasMaxLength(200);

        // Unbounded column type (no HasMaxLength -> nvarchar(max)) because the payload is a JSON DTO
        // of whatever shape the wrapped endpoint returns - the real bound is the application-level
        // MaxReplayableResponseBodyBytes cap (IdempotencyOptions), enforced by
        // IdempotencyMiddleware/EfIdempotencyStore before a row is ever completed; the CHECK
        // constraint below is defense-in-depth mirroring that same cap at the DB layer
        // (docs/db-conventions.md §3.1's pattern, applied to a text-length invariant rather than a
        // money/percent one).
        builder.Property(k => k.ResponseBody);

        // S13-DB-01 DoD: unique (TenantId, Key) - the natural serialisation point two concurrent
        // same-key requests race on (see EfIdempotencyStore's remarks), and what makes cross-tenant
        // key reuse structurally impossible to collide (docs/db-conventions.md §2 rule 2: TenantId
        // leads).
        builder.HasIndex(k => new { k.TenantId, k.Key }).IsUnique();

        // IdempotencyKeyCleanupService's retention sweep predicates (IdempotencyOptions) - both
        // TenantId-leading per docs/db-conventions.md §2 rule 2 ("no exceptions"), even though the
        // sweep itself deliberately reads across every tenant (IgnoreQueryFilters()): an index scan
        // across tenant partitions is an acceptable cost for an hourly background job with no <100ms
        // budget, and keeping the rule exception-free means a future per-tenant/admin-triggered purge
        // still seeks efficiently on the identical index.
        builder.HasIndex(k => new { k.TenantId, k.Status, k.CompletedAt });
        builder.HasIndex(k => new { k.TenantId, k.Status, k.ReservedAt });

        builder.ToTable(tb => tb.HasCheckConstraint(
            "CK_IdempotencyKeys_ResponseBody_Length", "[ResponseBody] IS NULL OR LEN([ResponseBody]) <= 65536"));
    }
}
