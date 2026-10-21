using FoodParkDigital.Domain.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// Transfers between locations (Fase 27B, ADR 0038). Each one owns its two movements: one transfer per movement, and
/// one per key of the app, so a retry never moves the items twice.
/// </summary>
public class InventoryTransferConfiguration : IEntityTypeConfiguration<InventoryTransfer>
{
    public void Configure(EntityTypeBuilder<InventoryTransfer> builder)
    {
        builder.ToTable("InventoryTransfers", table =>
        {
            table.HasCheckConstraint("CK_InventoryTransfers_Locations", "[FromLocationId] <> [ToLocationId]");
            table.HasCheckConstraint("CK_InventoryTransfers_Movements", "[OutMovementId] <> [InMovementId]");
            table.HasCheckConstraint("CK_InventoryTransfers_Lines", $"[LineCount] BETWEEN 1 AND {InventoryTransfer.MaxLines}");
            table.HasCheckConstraint("CK_InventoryTransfers_TotalCost", "[TotalCost] >= 0");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TransferNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.IdempotencyKey).IsRequired();
        builder.Property(x => x.TransferredAt).IsRequired();
        builder.Property(x => x.TransferredBy).HasMaxLength(InventoryTransfer.MaxUserNameLength).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(InventoryTransfer.MaxNotesLength);
        builder.Property(x => x.TotalCost).HasColumnType("decimal(18,4)").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TransferNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => x.OutMovementId).IsUnique();
        builder.HasIndex(x => x.InMovementId).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.TransferredAt });

        builder.HasOne<InventoryLocation>()
            .WithMany()
            .HasForeignKey(x => x.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryLocation>()
            .WithMany()
            .HasForeignKey(x => x.ToLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryMovement>()
            .WithMany()
            .HasForeignKey(x => x.OutMovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryMovement>()
            .WithMany()
            .HasForeignKey(x => x.InMovementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
