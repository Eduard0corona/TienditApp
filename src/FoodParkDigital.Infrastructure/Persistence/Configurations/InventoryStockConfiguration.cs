using FoodParkDigital.Domain.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class InventoryStockConfiguration : IEntityTypeConfiguration<InventoryStock>
{
    public void Configure(EntityTypeBuilder<InventoryStock> builder)
    {
        builder.ToTable("InventoryStocks");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.SupplyItemId).IsRequired();
        builder.Property(x => x.InventoryLocationId).IsRequired();
        // Concurrency token: two requests moving the same stock cannot overwrite each other.
        builder.Property(x => x.OnHandQuantity).HasColumnType("decimal(18,4)").IsRequired().IsConcurrencyToken();
        builder.Property(x => x.ReservedQuantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.LastCost).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.LastMovementAt);

        builder.Ignore(x => x.AvailableQuantity);

        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId, x.InventoryLocationId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.InventoryLocationId });
        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId });

        builder.HasOne(x => x.SupplyItem)
            .WithMany()
            .HasForeignKey(x => x.SupplyItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InventoryLocation)
            .WithMany()
            .HasForeignKey(x => x.InventoryLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
