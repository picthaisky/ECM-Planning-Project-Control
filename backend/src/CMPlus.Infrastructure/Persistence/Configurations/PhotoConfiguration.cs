using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="Photo"/> (S12-BE-01, S12-DB-shaped baseline).
/// "ProjectId/ActivityId are not EF-level FKs" mirrors this codebase's established convention (see
/// <see cref="DailyWeatherLogConfiguration"/>'s identical remark).</summary>
public sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("Photos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.Caption).HasMaxLength(500);
        builder.Property(p => p.StorageKey).IsRequired().HasMaxLength(400);

        // db-conventions.md §2 rule 2: TenantId leads every composite index. Anticipates the
        // not-yet-built photo gallery read (newest first, per project; optionally filtered to one
        // activity/zone) - the same "index ahead of the read endpoint" precedent IssueLogConfiguration
        // already established for its own CreatedAt/Status indexes.
        builder.HasIndex(p => new { p.TenantId, p.ProjectId, p.UploadedAt });
        builder.HasIndex(p => new { p.TenantId, p.ProjectId, p.ActivityId });

        builder.ToTable(tb => tb.HasCheckConstraint("CK_Photos_FileSizeBytes_Positive", "[FileSizeBytes] > 0"));
    }
}
