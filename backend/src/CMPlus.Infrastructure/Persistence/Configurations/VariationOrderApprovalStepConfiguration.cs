using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="VariationOrderApprovalStep"/> (S10-BE-01). Mirrors
/// <see cref="PaymentCertificateApprovalStepConfiguration"/> with one deliberate difference:
/// domain-rules.md §2.4 explicitly calls out that the IPC's own natural-key index is NOT unique
/// (finding N-04, still open there) and instructs "do not repeat it" on the VO's own snapshot table -
/// so this index IS unique, closing that gap for the VO from day one instead of inheriting it.
/// </summary>
public sealed class VariationOrderApprovalStepConfiguration : IEntityTypeConfiguration<VariationOrderApprovalStep>
{
    public void Configure(EntityTypeBuilder<VariationOrderApprovalStep> builder)
    {
        builder.ToTable("VariationOrderApprovalSteps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.TenantId).IsRequired();

        // domain-rules.md §2.4: "The natural-key index on the VO chain snapshot must be UNIQUE
        // (TenantId, VariationOrderId, RevisionNo, StepNo)" - unlike PaymentCertificateApprovalStep's
        // own (still-open, N-04) non-unique index, this one is unique from the start.
        builder.HasIndex(s => new { s.TenantId, s.VariationOrderId, s.RevisionNo, s.StepNo }).IsUnique();

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_VariationOrderApprovalSteps_StepNo", "[StepNo] >= 1");
            tb.HasCheckConstraint("CK_VariationOrderApprovalSteps_QuorumCount", "[QuorumCount] >= 1");
            tb.HasCheckConstraint("CK_VariationOrderApprovalSteps_RevisionNo", "[RevisionNo] >= 1");
        });
    }
}
