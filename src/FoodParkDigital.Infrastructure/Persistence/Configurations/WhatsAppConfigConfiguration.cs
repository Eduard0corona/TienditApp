using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class WhatsAppConfigConfiguration : IEntityTypeConfiguration<WhatsAppConfig>
{
    public void Configure(EntityTypeBuilder<WhatsAppConfig> builder)
    {
        builder.ToTable("WhatsAppConfigs");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.TenantId).IsRequired();
        builder.Property(w => w.IsEnabled).IsRequired().HasDefaultValue(false);
        builder.Property(w => w.AccessToken).HasMaxLength(500);
        builder.Property(w => w.PhoneNumberId).HasMaxLength(50).IsUnicode(false);
        builder.Property(w => w.WabaId).HasMaxLength(50).IsUnicode(false);
        builder.Property(w => w.DisplayPhoneNumber).HasMaxLength(20);
        builder.Property(w => w.VerifyToken).HasMaxLength(200);

        builder.HasIndex(w => w.TenantId).IsUnique();

        builder.HasOne(w => w.Tenant)
            .WithOne()
            .HasForeignKey<WhatsAppConfig>(w => w.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
