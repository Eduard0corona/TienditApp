using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class ProductSupplyRequirementConfiguration : IEntityTypeConfiguration<ProductSupplyRequirement>
{
    public void Configure(EntityTypeBuilder<ProductSupplyRequirement> builder)
    {
        builder.ToTable("ProductSupplyRequirements");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.SupplyItemId).IsRequired();
        builder.Property(x => x.InventoryLocationId);
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.WastePercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ProductId });
        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId });
        builder.HasIndex(x => new { x.TenantId, x.InventoryLocationId });
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.SupplyItemId, x.InventoryLocationId })
            .IsUnique()
            .HasFilter("[InventoryLocationId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.ProductId, x.SupplyItemId, x.IsActive })
            .HasFilter("[InventoryLocationId] IS NULL");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.SupplyItem)
            .WithMany()
            .HasForeignKey(x => x.SupplyItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryLocation>()
            .WithMany()
            .HasForeignKey(x => x.InventoryLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
