using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.ToTable("OrderPayments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Method).HasConversion<int>().IsRequired();
        builder.Property(x => x.PaidAt).IsRequired();
        builder.Property(x => x.InventoryLocationId).IsRequired(false);
        builder.Property(x => x.EvidenceUrl).HasMaxLength(1000);
        builder.Property(x => x.Reference).HasMaxLength(120);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasOne<InventoryLocation>()
            .WithMany()
            .HasForeignKey(x => x.InventoryLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.OrderId });
        builder.HasIndex(x => new { x.TenantId, x.PaidAt });
        builder.HasIndex(x => new { x.TenantId, x.InventoryLocationId, x.PaidAt });
    }
}
