using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("TenantSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();
        builder.Property(s => s.TimeZone).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Capabilities).HasConversion<int>().IsRequired();
        builder.Property(s => s.SettingsJson).HasColumnType("nvarchar(max)");
        builder.Property(s => s.DefaultTaxRate).HasColumnType("decimal(5,2)");
        builder.Property(s => s.OnlineMenuMode).HasConversion<int>().IsRequired();
        builder.Property(s => s.OrderNotificationPolicy).HasConversion<int>().IsRequired();
        builder.Property(s => s.InsufficientStockPolicy).HasConversion<int>().IsRequired();
        // The database default only fills the rows the previous version saves while this one is published; EF always
        // writes the value.
        builder.Property(s => s.Theme)
            .HasConversion<int>()
            .HasDefaultValue(BusinessTheme.Nopal)
            .ValueGeneratedNever()
            .IsRequired();

        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}
