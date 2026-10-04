using FoodParkDigital.Domain.Dining.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class DiningTableConfiguration : IEntityTypeConfiguration<DiningTable>
{
    public void Configure(EntityTypeBuilder<DiningTable> builder)
    {
        builder.ToTable("DiningTables");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(DiningTable.MaxNameLength).IsRequired();
        builder.Property(x => x.Area).HasMaxLength(DiningTable.MaxAreaLength);

        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IsActive, x.SortOrder });
    }
}

public class KitchenTicketConfiguration : IEntityTypeConfiguration<KitchenTicket>
{
    public void Configure(EntityTypeBuilder<KitchenTicket> builder)
    {
        builder.ToTable("KitchenTickets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.SentBy).HasMaxLength(256);

        builder.HasIndex(x => new { x.OrderId, x.Round }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}

public class OrderItemOptionConfiguration : IEntityTypeConfiguration<OrderItemOption>
{
    public void Configure(EntityTypeBuilder<OrderItemOption> builder)
    {
        builder.ToTable("OrderItemOptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.GroupName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.OptionName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PriceDelta).HasColumnType("decimal(18,2)").IsRequired();

        builder.HasIndex(x => x.OrderItemId);
    }
}
