using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="VariationOrder"/> (S10-BE-01/S10-DB-01). Mirrors
/// <see cref="PaymentCertificateConfiguration"/>'s shape closely - same owned-collection-via-backing-field
/// pattern (here for two collections, <c>ApprovalSteps</c> and <c>ScopeItems</c>, not one),
/// same <c>RowVersion</c> optimistic-concurrency token, same "ProjectId is not an EF-level FK"
/// convention this codebase uses everywhere.
/// </summary>
public sealed class VariationOrderConfiguration : IEntityTypeConfiguration<VariationOrder>
{
    public void Configure(EntityTypeBuilder<VariationOrder> builder)
    {
        builder.ToTable("VariationOrders");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.TenantId).IsRequired();
        builder.Property(v => v.VoNumber).IsRequired().HasMaxLength(50);
        builder.Property(v => v.Description).HasMaxLength(2000);
        builder.Property(v => v.Justification).HasMaxLength(4000);

        builder.Property(v => v.Amount).HasPrecision(18, 2);
        builder.Property(v => v.BacBefore).HasPrecision(18, 2);
        builder.Property(v => v.BacAfter).HasPrecision(18, 2);
        builder.Property(v => v.ContractValueBefore).HasPrecision(18, 2);
        builder.Property(v => v.ContractValueAfter).HasPrecision(18, 2);
        builder.Property(v => v.EscalationBasisContractValue).HasPrecision(18, 2);

        // decimal(9,6) - RoundingRules.RatioDecimals, not the decimal(5,2) percent convention: this
        // column is Phi itself (domain-rules.md §4.7), stored for audit/reconstruction of a governance
        // decision. V-5b exists specifically because a value that displays as "10.00%" can genuinely be
        // 10.004% underneath - collapsing this column to 2dp would destroy exactly the fidelity an
        // auditor re-checking "was this VO really over threshold" needs, the live equivalent of
        // domain-rules.md §4.6's "compare at full decimal precision" rule.
        builder.Property(v => v.CumulativeVoPctAtApproval).HasPrecision(9, 6);

        // design.md/S10-BE-01: two simultaneous approvers race here, the second gets 409 (domain-rules.md
        // §2.3's "takes the RowVersion optimistic-concurrency token").
        builder.Property(v => v.RowVersion).IsRowVersion();

        // Owned-collection navigations backed by the private `_approvalSteps`/`_scopeItems` fields -
        // neither child type has a public constructor/adder, so EF must populate them via the backing
        // field (PaymentCertificateConfiguration's established pattern). Cascade delete: neither
        // collection has a reason to outlive the VariationOrder itself.
        builder.HasMany(v => v.ApprovalSteps)
            .WithOne()
            .HasForeignKey(s => s.VariationOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(VariationOrder.ApprovalSteps))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(v => v.ScopeItems)
            .WithOne()
            .HasForeignKey(i => i.VariationOrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(VariationOrder.ScopeItems))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // domain-rules.md §7.1: "VoNumber is unique per (TenantId, ProjectId) ... Enforce with a
        // unique index." - the authoritative, race-safe backstop behind
        // IVariationOrderRepository.ExistsWithVoNumberAsync's pre-check.
        builder.HasIndex(v => new { v.TenantId, v.ProjectId, v.VoNumber }).IsUnique();

        // Baseline list/status index, same shape as PaymentCertificateConfiguration's own
        // (TenantId, ProjectId, Status) index - correctness-level baseline every other
        // document-type repository in this codebase already has. Serves ListByProjectAsync (all
        // statuses; TenantId+ProjectId equality prefix) and is defence-in-depth for
        // GetApprovedNetSignedTotalAsync if that query is ever refactored to take Status as a
        // parameter (see the dedicated index below for why that distinction matters).
        builder.HasIndex(v => new { v.TenantId, v.ProjectId, v.Status });

        // S10-DB-01 (database-engineer): the query GetApprovedNetSignedTotalAsync actually runs is
        //   .Where(v => v.ProjectId == projectId && v.Status == VariationOrderStatus.Approved).SumAsync(v => v.Amount)
        // - the Sigma^VO escalation aggregate (domain-rules.md §4), evaluated on every VO Submit
        // and re-checked on every final-step Approve (§4.7's bypass guard), so this is a genuinely
        // hot, correctness-adjacent read. Two deliberate refinements over the baseline index above:
        //
        // 1. INCLUDE (Amount) makes it fully covering for SumAsync(v => v.Amount) - the query never
        //    visits the clustered index, so there is no per-row Key Lookup regardless of how many
        //    approved VOs a project accumulates over its life (a seek-and-aggregate against the
        //    nonclustered leaf alone).
        //
        // 2. WHERE Status = 3 (Approved) is a filtered index, not a blanket one. This is safe here
        //    specifically because the EF Core 10-generated SQL for this exact call site embeds
        //    "[v].[Status] = 3" as a literal (confirmed via ToQueryString() against the real model -
        //    VariationOrderStatus.Approved is referenced directly, not through a captured variable,
        //    so EF's parameter-extraction leaves it as a constant), which is exactly the shape SQL
        //    Server's optimizer requires to match a filtered index (Microsoft's own filtered-index
        //    guidance: the predicate must be a literal/constant, not a parameter or local variable).
        //    Approved is also terminal in VariationOrderStatus's state machine - no transition ever
        //    leaves it - so a filtered-index entry is written once, on the PendingApproval->Approved
        //    transition, and never relocated again; every other status transition a VO goes through
        //    (Draft->PendingApproval, PendingApproval->Draft via ReturnForRevision, etc.) never
        //    touches this index at all, unlike the unfiltered baseline above which relocates its
        //    entry on every Status change. The baseline (TenantId, ProjectId, Status) index is kept
        //    alongside this one, not replaced by it: it is what ListByProjectAsync (which needs
        //    every status) actually uses, and it is the fallback if this query is ever generalised
        //    to take Status as a parameter (at which point the literal-match precondition above no
        //    longer holds and this filtered index would silently stop being selected - the baseline
        //    still guarantees a seek, just with a Key Lookup for Amount, never a scan).
        builder.HasIndex(v => new { v.TenantId, v.ProjectId })
            .HasDatabaseName("IX_VariationOrders_TenantId_ProjectId_Approved")
            .IncludeProperties(v => v.Amount)
            .HasFilter("[Status] = 3");

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_VariationOrders_RevisionNo", "[RevisionNo] >= 1");
            tb.HasCheckConstraint("CK_VariationOrders_CurrentStepNo", "[CurrentStepNo] >= 0");
            tb.HasCheckConstraint("CK_VariationOrders_TotalSteps", "[TotalSteps] >= 0");
        });
    }
}
