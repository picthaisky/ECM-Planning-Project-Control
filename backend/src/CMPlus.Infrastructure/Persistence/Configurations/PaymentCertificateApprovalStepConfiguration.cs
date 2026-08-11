using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="PaymentCertificateApprovalStep"/> (security review sprint-09.md
/// H-01 fix). Owned-collection navigation backed by <see cref="PaymentCertificate"/>'s private
/// backing field, the same pattern <see cref="ApprovalPolicyConfiguration"/> already established for
/// <c>ApprovalPolicy.Rules</c>.
/// </summary>
public sealed class PaymentCertificateApprovalStepConfiguration : IEntityTypeConfiguration<PaymentCertificateApprovalStep>
{
    public void Configure(EntityTypeBuilder<PaymentCertificateApprovalStep> builder)
    {
        builder.ToTable("PaymentCertificateApprovalSteps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.TenantId).IsRequired();

        // S10-DB-01 (database-engineer): N-04 fix (sprint-09.md §9.5, carried forward as an open
        // item in domain-rules.md §9's "not open, recorded as accepted limitations" list until now).
        // This natural-key index was non-unique, so the schema permitted two rungs claiming the
        // same (RevisionNo, StepNo) - both Approve and Reject resolve the current/final step with
        // FirstOrDefault, so a duplicate would silently pick whichever row happened to sort first
        // rather than fail loudly. VariationOrderApprovalStepConfiguration made its equivalent index
        // UNIQUE from day one specifically so this gap would not be repeated (see its own remarks);
        // this closes the same gap here instead of leaving the IPC as the one document type still
        // carrying it. No corrective data migration needed: chain rows are written once per revision
        // by chain resolution at Submit (approval-workflow.md §5.3), which assigns each StepNo
        // exactly once per RevisionNo by construction - a genuine duplicate has never been anything
        // other than a latent possibility the schema failed to close, not a state any passing test
        // or seed path actually produces.
        builder.HasIndex(s => new { s.TenantId, s.PaymentCertificateId, s.RevisionNo, s.StepNo }).IsUnique();

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_PaymentCertificateApprovalSteps_StepNo", "[StepNo] >= 1");
            tb.HasCheckConstraint("CK_PaymentCertificateApprovalSteps_QuorumCount", "[QuorumCount] >= 1");
            tb.HasCheckConstraint("CK_PaymentCertificateApprovalSteps_RevisionNo", "[RevisionNo] >= 1");
        });
    }
}
