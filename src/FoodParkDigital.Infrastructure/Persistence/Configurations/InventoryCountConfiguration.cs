using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// Physical counts (Fase 27A, ADR 0037). The base keeps who closed each one and when, according to its state, and a
/// location with one open count at a time: two open counts would adjust the same stock twice.
/// </summary>
public class InventoryCountConfiguration : IEntityTypeConfiguration<InventoryCount>
{
    public void Configure(EntityTypeBuilder<InventoryCount> builder)
    {
        builder.ToTable("InventoryCounts", table =>
        {
            // Open (1) has neither; posted (2) has when and who posted it; cancelled (3), when and who cancelled it.
            table.HasCheckConstraint("CK_InventoryCounts_Status",
                "([Status] = 1 AND [PostedAt] IS NULL AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 2 AND [PostedAt] IS NOT NULL AND [PostedBy] IS NOT NULL AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 3 AND [CancelledAt] IS NOT NULL AND [CancelledBy] IS NOT NULL AND [PostedAt] IS NULL)");
            table.HasCheckConstraint("CK_InventoryCounts_Lines",
                "[LineCount] >= 0 AND [CountedLineCount] >= 0 AND [CountedLineCount] <= [LineCount]");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CountNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.CategoryName).HasMaxLength(InventoryCount.MaxCategoryNameLength);
        builder.Property(x => x.Notes).HasMaxLength(InventoryCount.MaxNotesLength);
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.StartedBy).HasMaxLength(InventoryCount.MaxUserNameLength).IsRequired();
        builder.Property(x => x.PostedBy).HasMaxLength(InventoryCount.MaxUserNameLength);
        builder.Property(x => x.CancelledBy).HasMaxLength(InventoryCount.MaxUserNameLength);
        builder.Property(x => x.CancelReason).HasMaxLength(InventoryCount.MaxCancelReasonLength);
        // Concurrency token besides the location's lock: a capture never slips into a count being posted.
        builder.Property(x => x.Revision).IsRequired().IsConcurrencyToken();

        builder.Ignore(x => x.IsOpen);

        builder.HasIndex(x => new { x.TenantId, x.CountNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.InventoryLocationId })
            .IsUnique()
            .HasFilter($"[Status] = {(int)InventoryCountStatus.InProgress}")
            .HasDatabaseName("IX_InventoryCounts_TenantId_InventoryLocationId_Open");
        builder.HasIndex(x => new { x.TenantId, x.StartedAt });

        builder.HasOne<InventoryLocation>()
            .WithMany()
            .HasForeignKey(x => x.InventoryLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryMovement>()
            .WithMany()
            .HasForeignKey(x => x.IncreaseMovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryMovement>()
            .WithMany()
            .HasForeignKey(x => x.DecreaseMovementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
