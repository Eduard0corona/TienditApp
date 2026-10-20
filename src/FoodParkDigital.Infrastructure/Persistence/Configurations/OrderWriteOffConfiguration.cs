using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>What the business forgave of an order a customer owed (Fase 26B, ADR 0034): immutable, never deleted alone.</summary>
public class OrderWriteOffConfiguration : IEntityTypeConfiguration<OrderWriteOff>
{
    public void Configure(EntityTypeBuilder<OrderWriteOff> builder)
    {
        builder.ToTable("OrderWriteOffs", table =>
            table.HasCheckConstraint("CK_OrderWriteOffs_Amount", "[Amount] > 0"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Reason).HasConversion<int>().IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(OrderWriteOff.MaxNotesLength);
        builder.Property(x => x.WrittenOffAt).IsRequired();
        builder.Property(x => x.WrittenOffBy).HasMaxLength(256).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.OrderId });
        builder.HasIndex(x => new { x.TenantId, x.WrittenOffAt });
    }
}
