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

        // The chain-lookup read for one revision's current/final step (Approve/Reject/
        // ReturnForRevision), mirroring ApprovalActionConfiguration's own history index shape.
        builder.HasIndex(s => new { s.TenantId, s.PaymentCertificateId, s.RevisionNo, s.StepNo });

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_PaymentCertificateApprovalSteps_StepNo", "[StepNo] >= 1");
            tb.HasCheckConstraint("CK_PaymentCertificateApprovalSteps_QuorumCount", "[QuorumCount] >= 1");
            tb.HasCheckConstraint("CK_PaymentCertificateApprovalSteps_RevisionNo", "[RevisionNo] >= 1");
        });
    }
}
