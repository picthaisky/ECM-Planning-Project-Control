using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(200).IsRequired();
        // Unbounded snapshot JSON - a wide entity's property bag has no fixed practical limit.
        builder.Property(a => a.BeforeJson).HasColumnType("nvarchar(max)");
        builder.Property(a => a.AfterJson).HasColumnType("nvarchar(max)");

        builder.HasIndex(a => new { a.TenantId, a.EntityName, a.EntityId });
        builder.HasIndex(a => new { a.TenantId, a.Timestamp });

        // Append-only (S2-BE-02): no update/delete path exists on the entity or here.
    }
}
