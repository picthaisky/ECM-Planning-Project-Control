using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class ApprovalActionConfiguration : IEntityTypeConfiguration<ApprovalAction>
{
    public void Configure(EntityTypeBuilder<ApprovalAction> builder)
    {
        builder.ToTable("ApprovalActions");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.Comment).HasMaxLength(2000);

        // design.md §3 mandatory index - the chain-history read for one document.
        builder.HasIndex(a => new { a.TenantId, a.DocumentType, a.DocumentId, a.RevisionNo, a.StepNo });

        // Append-only (design.md §3): no update/delete path exists on the entity (no mutator
        // methods/setters - verified structurally by CMPlus.Domain.Tests) or here in configuration.
    }
}
