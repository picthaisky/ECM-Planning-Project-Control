using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for <see cref="VariationOrderScopeItem"/> (S10-BE-01) - the
/// <c>{ActivityId, BudgetCostDelta}</c> delta-list scope payload, domain-rules.md §5.2.
/// </summary>
public sealed class VariationOrderScopeItemConfiguration : IEntityTypeConfiguration<VariationOrderScopeItem>
{
    public void Configure(EntityTypeBuilder<VariationOrderScopeItem> builder)
    {
        builder.ToTable("VariationOrderScopeItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.TenantId).IsRequired();
        builder.Property(i => i.Note).HasMaxLength(500);
        builder.Property(i => i.BudgetCostDelta).HasPrecision(18, 2);

        // Lookup shape: "every scope line for this VO" (SetVariationContent's Clear+re-add) and
        // "every scope line touching this Activity" (an omission-of-executed-scope audit query).
        builder.HasIndex(i => new { i.TenantId, i.VariationOrderId });
        builder.HasIndex(i => new { i.TenantId, i.ActivityId });

        builder.ToTable(tb =>
            tb.HasCheckConstraint("CK_VariationOrderScopeItems_BudgetCostDelta_NotZero", "[BudgetCostDelta] <> 0"));
    }
}
