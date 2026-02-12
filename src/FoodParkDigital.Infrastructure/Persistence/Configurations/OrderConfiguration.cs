using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(o => o.BusinessType).HasConversion<int>().IsRequired();
        builder.Property(o => o.Status).HasConversion<int>().IsRequired();
        builder.Property(o => o.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(o => o.CustomerPhone).HasMaxLength(20);
        builder.Property(o => o.CustomerNotes).HasMaxLength(500);
        builder.Property(o => o.TableNumber).HasMaxLength(10);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.SubTotal).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Tax).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)");
        builder.Property(o => o.TenantId).IsRequired();

        // Concurrency token
        builder.Property(o => o.Version).IsRowVersion();

        // Relationships
        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.StatusHistory)
            .WithOne()
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Backing fields for encapsulated collections
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Indexes
        builder.HasIndex(o => new { o.TenantId, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(o => new { o.TenantId, o.OrderNumber }).IsUnique();
    }
}
