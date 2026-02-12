using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistory");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.PreviousStatus).HasConversion<int>().IsRequired();
        builder.Property(h => h.NewStatus).HasConversion<int>().IsRequired();
        builder.Property(h => h.ChangedBy).HasMaxLength(256);
        builder.Property(h => h.Notes).HasMaxLength(500);

        builder.HasIndex(h => new { h.OrderId, h.ChangedAt });
    }
}
