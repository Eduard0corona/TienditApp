using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(100).IsRequired();
        builder.Property(t => t.BusinessType).HasConversion<int>().IsRequired();
        builder.Property(t => t.Status).HasConversion<int>().IsRequired();
        builder.Property(t => t.ContactEmail).HasMaxLength(256).IsRequired();
        builder.Property(t => t.ContactPhone).HasMaxLength(20);
        builder.Property(t => t.LogoUrl).HasMaxLength(500);

        // MX Business fields
        builder.Property(t => t.Rfc).HasMaxLength(13);
        builder.Property(t => t.LegalName).HasMaxLength(300);
        builder.Property(t => t.Industry).HasMaxLength(200);
        builder.Property(t => t.TaxRegime).HasMaxLength(200);

        builder.OwnsOne(t => t.Address, a =>
        {
            a.Property(p => p.Street).HasMaxLength(300).HasColumnName("Address_Street");
            a.Property(p => p.ExteriorNumber).HasMaxLength(20).HasColumnName("Address_ExteriorNumber");
            a.Property(p => p.InteriorNumber).HasMaxLength(20).HasColumnName("Address_InteriorNumber");
            a.Property(p => p.Neighborhood).HasMaxLength(200).HasColumnName("Address_Neighborhood");
            a.Property(p => p.ZipCode).HasMaxLength(10).HasColumnName("Address_ZipCode");
            a.Property(p => p.City).HasMaxLength(200).HasColumnName("Address_City");
            a.Property(p => p.State).HasMaxLength(100).HasColumnName("Address_State");
            a.Property(p => p.Country).HasMaxLength(100).HasColumnName("Address_Country");
        });

        builder.HasIndex(t => t.Slug).IsUnique();

        builder.HasOne(t => t.Settings)
            .WithOne(s => s.Tenant)
            .HasForeignKey<TenantSettings>(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
