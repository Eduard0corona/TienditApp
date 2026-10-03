using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Purchasing.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchaseOrderLines");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.PurchaseOrderId).IsRequired();
        builder.Property(x => x.SupplyItemId).IsRequired();
        builder.Property(x => x.InventoryLocationId).IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,4)").IsRequired();
        // Concurrency token: two simultaneous receptions of the same line cannot both pass the pending check.
        builder.Property(x => x.ReceivedQuantity).HasColumnType("decimal(18,4)").IsRequired().IsConcurrencyToken();
        builder.Property(x => x.UnitCost).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.TaxPercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.SubTotal).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.TaxAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.Total).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasIndex(x => new { x.TenantId, x.PurchaseOrderId });
        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId });
        builder.HasIndex(x => new { x.TenantId, x.InventoryLocationId });

        builder.HasOne<SupplyItem>()
            .WithMany()
            .HasForeignKey(x => x.SupplyItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryLocation>()
            .WithMany()
            .HasForeignKey(x => x.InventoryLocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
