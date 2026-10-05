using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Fiscal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);

        // Core info
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.Sku).HasMaxLength(100);
        builder.Property(p => p.Barcode).HasMaxLength(50);
        builder.Property(p => p.Tags).HasMaxLength(500);
        builder.Property(p => p.Unit)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Pricing
        builder.Property(p => p.PriceAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(p => p.PriceCurrency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.CompareAtPrice).HasColumnType("decimal(18,2)");
        builder.Property(p => p.TaxRate).HasColumnType("decimal(5,2)");

        // Invoicing (CFDI 4.0); null codes mean the generic product code and the unit's code.
        builder.Property(p => p.SatProductCode).HasMaxLength(8);
        builder.Property(p => p.SatUnitCode).HasMaxLength(3);
        builder.Property(p => p.TaxObject)
            .HasConversion<int>()
            .HasDefaultValue(CfdiTaxObject.Subject)
            .ValueGeneratedNever()
            .IsRequired();

        // Media
        builder.Property(p => p.ImageUrl).HasMaxLength(500);

        // Food-specific
        builder.Property(p => p.Allergens).HasMaxLength(500);
        builder.Property(p => p.AvailableFromTime).HasColumnType("time");
        builder.Property(p => p.AvailableToTime).HasColumnType("time");
        builder.Property(p => p.IsTrackable).HasDefaultValue(false);
        builder.Property(p => p.NotifyCustomer).HasDefaultValue(false);

        // Tenant
        builder.Property(p => p.TenantId).IsRequired();

        // Variants and extras, owned by the product.
        builder.HasMany(p => p.OptionGroups)
            .WithOne()
            .HasForeignKey(g => g.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.OptionGroups).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Computed properties ignored by EF
        builder.Ignore(p => p.HasDiscount);

        // Indexes
        builder.HasIndex(p => new { p.TenantId, p.CategoryId });
        builder.HasIndex(p => new { p.TenantId, p.IsAvailable });
        builder.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique().HasFilter("[Sku] IS NOT NULL");
        builder.HasIndex(p => new { p.TenantId, p.Barcode }).HasFilter("[Barcode] IS NOT NULL");
        builder.HasIndex(p => new { p.TenantId, p.IsFeatured });
        builder.HasIndex(p => new { p.TenantId, p.SortOrder });
    }
}
