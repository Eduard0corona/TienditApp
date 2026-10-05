using FoodParkDigital.Domain.Purchasing.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.SupplierId).IsRequired();
        builder.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.OrderedAt).IsRequired();
        builder.Property(x => x.ExpectedDeliveryAt);
        builder.Property(x => x.ReceivedAt);
        builder.Property(x => x.ReferenceNumber).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.SubTotal).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.Tax).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.Total).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.ClosedAt);
        builder.Property(x => x.CloseReason).HasMaxLength(PurchaseOrder.MaxCloseReasonLength);

        builder.HasIndex(x => new { x.TenantId, x.OrderNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.SupplierId });

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.PurchaseOrder)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
