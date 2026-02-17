using FoodParkDigital.Domain.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("InventoryMovements");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.MovementNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.MovementType).HasConversion<int>().IsRequired();
        builder.Property(x => x.MovementReason).HasConversion<int>().IsRequired();
        builder.Property(x => x.MovementDate).IsRequired();
        builder.Property(x => x.ReferenceNumber).HasMaxLength(100);
        builder.Property(x => x.ExternalReference).HasMaxLength(100);
        builder.Property(x => x.CounterpartyName).HasMaxLength(200);
        builder.Property(x => x.DocumentNumber).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.TotalAmount).HasColumnType("decimal(18,4)").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.MovementNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ExternalReference })
            .IsUnique()
            .HasFilter("[ExternalReference] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.MovementDate });
        builder.HasIndex(x => new { x.TenantId, x.MovementType });

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.Movement)
            .HasForeignKey(x => x.MovementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
