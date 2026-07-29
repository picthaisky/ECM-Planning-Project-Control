using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class CalendarExceptionConfiguration : IEntityTypeConfiguration<CalendarException>
{
    public void Configure(EntityTypeBuilder<CalendarException> builder)
    {
        builder.ToTable("CalendarExceptions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(250);

        builder.HasOne<Calendar>()
            .WithMany()
            .HasForeignKey(e => e.CalendarId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantId, e.CalendarId });
    }
}
