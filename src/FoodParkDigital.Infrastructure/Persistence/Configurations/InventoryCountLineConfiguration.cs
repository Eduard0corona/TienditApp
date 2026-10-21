using FoodParkDigital.Domain.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>The items of a physical count (Fase 27A): each item once, counted with what the system had of it.</summary>
public class InventoryCountLineConfiguration : IEntityTypeConfiguration<InventoryCountLine>
{
    public void Configure(EntityTypeBuilder<InventoryCountLine> builder)
    {
        builder.ToTable("InventoryCountLines", table =>
        {
            // Counted, with what the system had, when and by whom; or none of them.
            table.HasCheckConstraint("CK_InventoryCountLines_Counted",
                "([CountedQuantity] IS NULL AND [SystemQuantity] IS NULL AND [CountedAt] IS NULL AND [CountedBy] IS NULL)"
                + " OR ([CountedQuantity] >= 0 AND [SystemQuantity] IS NOT NULL AND [CountedAt] IS NOT NULL AND [CountedBy] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CountId).IsRequired();
        builder.Property(x => x.SupplyItemId).IsRequired();
        builder.Property(x => x.CountedQuantity).HasColumnType("decimal(18,4)");
        builder.Property(x => x.SystemQuantity).HasColumnType("decimal(18,4)");
        builder.Property(x => x.CountedBy).HasMaxLength(InventoryCount.MaxUserNameLength);
        builder.Property(x => x.Notes).HasMaxLength(InventoryCountLine.MaxNotesLength);

        builder.Ignore(x => x.IsCounted);
        builder.Ignore(x => x.Difference);

        builder.HasIndex(x => new { x.CountId, x.SupplyItemId }).IsUnique();

        builder.HasOne<InventoryCount>()
            .WithMany()
            .HasForeignKey(x => x.CountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SupplyItem>()
            .WithMany()
            .HasForeignKey(x => x.SupplyItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
