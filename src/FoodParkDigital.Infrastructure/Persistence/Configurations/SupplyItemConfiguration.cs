using FoodParkDigital.Domain.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class SupplyItemConfiguration : IEntityTypeConfiguration<SupplyItem>
{
    public void Configure(EntityTypeBuilder<SupplyItem> builder)
    {
        builder.ToTable("SupplyItems");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(100);
        builder.Property(x => x.Sku).HasMaxLength(100);
        builder.Property(x => x.Barcode).HasMaxLength(50);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Brand).HasMaxLength(120);
        builder.Property(x => x.Model).HasMaxLength(120);
        builder.Property(x => x.CategoryName).HasMaxLength(120);
        builder.Property(x => x.Unit).HasConversion<int>().IsRequired();
        builder.Property(x => x.DefaultCost).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.LastCost).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.AverageCost).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.MinStock).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.MaxStock).HasColumnType("decimal(18,4)");
        builder.Property(x => x.ReorderPoint).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.TaxRate).HasColumnType("decimal(5,2)");
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Name });
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[Code] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique().HasFilter("[Sku] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.Barcode }).HasFilter("[Barcode] IS NOT NULL");
    }
}
