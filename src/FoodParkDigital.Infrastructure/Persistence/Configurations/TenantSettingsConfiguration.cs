using FoodParkDigital.Domain.Tenants.Entities;
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
        builder.Property(s => s.SettingsJson).HasColumnType("nvarchar(max)");

        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}
