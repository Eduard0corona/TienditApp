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
        // Fase 31J (ADR 0090): the postal code a customer account gives in Explorar, five digits.
        builder.Property(u => u.PostalCode).HasMaxLength(5).IsUnicode(false).IsRequired(false);

        builder.HasIndex(u => u.DefaultTenantId);
    }
}
