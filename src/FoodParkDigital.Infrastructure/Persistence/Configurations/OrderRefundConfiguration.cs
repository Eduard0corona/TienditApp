using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.ToTable("OrderRefunds");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Method).HasConversion<int>().IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(OrderRefund.MaxReasonLength).IsRequired();
        builder.Property(x => x.RefundedAt).IsRequired();
        builder.Property(x => x.RefundedBy).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(120);

        builder.HasOne<InventoryLocation>()
            .WithMany()
            .HasForeignKey(x => x.InventoryLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.RefundId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.OrderId });
        builder.HasIndex(x => new { x.TenantId, x.RefundedAt });
    }
}

public class OrderRefundLineConfiguration : IEntityTypeConfiguration<OrderRefundLine>
{
    public void Configure(EntityTypeBuilder<OrderRefundLine> builder)
    {
        builder.ToTable("OrderRefundLines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();

        // The returned line must exist; deleting it is not possible while a return points to it.
        builder.HasOne<OrderItem>()
            .WithMany()
            .HasForeignKey(x => x.OrderItemId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.TenantId, x.RefundId });
    }
}
