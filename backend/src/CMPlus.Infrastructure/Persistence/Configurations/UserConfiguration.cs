using CMPlus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CMPlus.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.TenantId).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(250).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(250).IsRequired();

        // TenantId leading column (ADR-0002); also enforces email uniqueness per tenant.
        builder.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
    }
}
