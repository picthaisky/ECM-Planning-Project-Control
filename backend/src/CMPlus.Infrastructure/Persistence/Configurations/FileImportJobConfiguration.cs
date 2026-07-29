using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class FileImportJobConfiguration : IEntityTypeConfiguration<FileImportJob>
{
    public void Configure(EntityTypeBuilder<FileImportJob> builder)
    {
        builder.ToTable("FileImportJobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();

        builder.Property(j => j.TenantId).IsRequired();
        builder.Property(j => j.ProjectId).IsRequired();
        builder.Property(j => j.FileName).HasMaxLength(250).IsRequired();
        builder.Property(j => j.ErrorJson).HasMaxLength(4000);

        // History/status queries are always "jobs for project X, newest first" (S3-BE-04 DoD);
        // TenantId leads per docs/db-conventions.md §2 rule 2.
        builder.HasIndex(j => new { j.TenantId, j.ProjectId, j.StartedAt });

        builder.Property(j => j.RowsImported).HasDefaultValue(0);

        builder.ToTable(tb => tb.HasCheckConstraint("CK_FileImportJobs_RowsImported", "[RowsImported] >= 0"));
    }
}
