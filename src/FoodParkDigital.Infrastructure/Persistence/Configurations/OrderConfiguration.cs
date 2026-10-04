using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        // Mirrors the Order constructor: never both customer links, and only counter sales created by the
        // business (walk-in, without phone) may have neither.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Orders_CustomerLink",
            "([CustomerUserId] IS NULL OR [TemporaryCustomerId] IS NULL) AND ([CreatedByBusiness] = 1 OR [CustomerUserId] IS NOT NULL OR [TemporaryCustomerId] IS NOT NULL)"));
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).HasMaxLength(20).IsRequired();
        builder.Property(o => o.BusinessType).HasConversion<int>().IsRequired();
        builder.Property(o => o.OrderType).HasConversion<int>().IsRequired();
        builder.Property(o => o.Status).HasConversion<int>().IsRequired();
        builder.Property(o => o.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(o => o.CustomerPhone).HasMaxLength(20);
        builder.Property(o => o.CustomerNotes).HasMaxLength(500);
        builder.Property(o => o.TableNumber).HasMaxLength(10);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.CustomerUserId).IsRequired(false);
        builder.Property(o => o.TemporaryCustomerId).IsRequired(false);
        builder.Property(o => o.CreatedByBusiness).IsRequired().HasDefaultValue(true);
        builder.Property(o => o.SubTotal).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Tax).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)");
        builder.Property(o => o.PaidAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
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

        builder.HasMany(o => o.Payments)
            .WithOne(p => p.Order)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.ServicePieces)
            .WithOne(p => p.Order)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.ServiceDetail)
            .WithOne(d => d.Order)
            .HasForeignKey<ServiceOrderDetail>(d => d.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(o => o.CustomerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TemporaryCustomer>()
            .WithMany()
            .HasForeignKey(o => o.TemporaryCustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Backing fields for encapsulated collections
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.ServicePieces).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Indexes
        builder.HasIndex(o => new { o.TenantId, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.OrderType, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(o => new { o.TenantId, o.OrderNumber }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.CustomerUserId });
        builder.HasIndex(o => new { o.TenantId, o.TemporaryCustomerId });
    }
}
