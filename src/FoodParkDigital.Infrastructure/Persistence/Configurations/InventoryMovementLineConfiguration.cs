using FoodParkDigital.Domain.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class InventoryMovementLineConfiguration : IEntityTypeConfiguration<InventoryMovementLine>
{
    public void Configure(EntityTypeBuilder<InventoryMovementLine> builder)
    {
        builder.ToTable("InventoryMovementLines");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.MovementId).IsRequired();
        builder.Property(x => x.SupplyItemId).IsRequired();
        builder.Property(x => x.InventoryLocationId).IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.UnitCost).HasColumnType("decimal(18,4)");
        builder.Property(x => x.TotalCost).HasColumnType("decimal(18,4)");
        builder.Property(x => x.QuantityBefore).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.QuantityAfter).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.LotNumber).HasMaxLength(80);
        builder.Property(x => x.ExpirationDate);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasIndex(x => new { x.TenantId, x.MovementId });
        builder.HasIndex(x => new { x.TenantId, x.SupplyItemId, x.InventoryLocationId });
        builder.HasIndex(x => new { x.TenantId, x.ExpirationDate });

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
