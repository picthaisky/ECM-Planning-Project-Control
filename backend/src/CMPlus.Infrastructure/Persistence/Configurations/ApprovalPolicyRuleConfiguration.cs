using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class ApprovalPolicyRuleConfiguration : IEntityTypeConfiguration<ApprovalPolicyRule>
{
    public void Configure(EntityTypeBuilder<ApprovalPolicyRule> builder)
    {
        builder.ToTable("ApprovalPolicyRules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.MinAmount).HasPrecision(18, 2);
        builder.Property(r => r.MaxAmount).HasPrecision(18, 2);

        builder.HasIndex(r => new { r.TenantId, r.ApprovalPolicyId, r.StepNo });

        builder.ToTable(tb =>
        {
            tb.HasCheckConstraint("CK_ApprovalPolicyRules_StepNo", "[StepNo] >= 1");
            tb.HasCheckConstraint("CK_ApprovalPolicyRules_MinAmount", "[MinAmount] >= 0");
            tb.HasCheckConstraint("CK_ApprovalPolicyRules_MaxAmount", "[MaxAmount] IS NULL OR [MaxAmount] > [MinAmount]");
            tb.HasCheckConstraint("CK_ApprovalPolicyRules_QuorumCount", "[QuorumCount] >= 1");
        });
    }
}
