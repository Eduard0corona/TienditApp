using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Role).HasConversion(UserRoleStorage.Converter).IsRequired();
        builder.Property(u => u.AvatarUrl).HasMaxLength(500).IsRequired(false);
        builder.Property(u => u.DefaultTenantId).IsRequired(false);

        builder.HasIndex(u => u.DefaultTenantId);
    }
}
