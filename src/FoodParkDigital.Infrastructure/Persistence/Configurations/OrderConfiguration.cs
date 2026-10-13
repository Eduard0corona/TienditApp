using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Dining.Entities;
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
        builder.Property(o => o.Channel).HasConversion<int>().IsRequired();
        builder.Property(o => o.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(o => o.CustomerPhone).HasMaxLength(20);
        builder.Property(o => o.CustomerNotes).HasMaxLength(500);
        builder.Property(o => o.TableNumber).HasMaxLength(50);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.CustomerUserId).IsRequired(false);
        builder.Property(o => o.TemporaryCustomerId).IsRequired(false);
        builder.Property(o => o.CreatedByBusiness).IsRequired().HasDefaultValue(true);
        builder.Property(o => o.SubTotal).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Tax).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Total).HasColumnType("decimal(18,2)");
        builder.Property(o => o.PaidAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Property(o => o.RefundedAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m);
        builder.Ignore(o => o.NetPaidAmount);
        builder.Ignore(o => o.TipTotal);
        builder.Ignore(o => o.IsOpenTab);
        builder.Ignore(o => o.CurrentRound);
        builder.Ignore(o => o.IsDeliveryPlatformOrder);
        builder.Property(o => o.IsTab).HasDefaultValue(false).ValueGeneratedNever();
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

        // The folio, terms and amounts of an order of a delivery platform. Not auto-included: the queries that show
        // or return the order include it, as they include ServiceDetail.
        builder.HasOne(o => o.PlatformDetail)
            .WithOne()
            .HasForeignKey<PlatformOrderDetail>(d => d.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Where the order goes and what its delivery charges. Always loaded with the order (unlike PlatformDetail): its
        // fee is part of the total, so an order changed without it would recalculate a total without the delivery.
        builder.HasOne(o => o.Delivery)
            .WithOne()
            .HasForeignKey<OrderDelivery>(d => d.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Delivery).AutoInclude();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(o => o.CustomerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TemporaryCustomer>()
            .WithMany()
            .HasForeignKey(o => o.TemporaryCustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FoodParkDigital.Domain.Branches.Entities.Branch>()
            .WithMany()
            .HasForeignKey(o => o.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // Open tabs: the table it was served at, and OpenTableId while it is open (one open tab per table).
        builder.HasOne<DiningTable>()
            .WithMany()
            .HasForeignKey(o => o.DiningTableId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DiningTable>()
            .WithMany()
            .HasForeignKey(o => o.OpenTableId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(o => o.KitchenTickets)
            .WithOne()
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.KitchenTickets).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(o => o.ItemCancellations)
            .WithOne()
            .HasForeignKey(c => c.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.ItemCancellations).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Backing fields for encapsulated collections
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(o => o.Refunds)
            .WithOne()
            .HasForeignKey(r => r.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(o => o.ServicePieces).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Indexes
        builder.HasIndex(o => new { o.TenantId, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.OrderType, o.Status });
        builder.HasIndex(o => new { o.TenantId, o.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(o => new { o.TenantId, o.BranchId, o.CreatedAt });
        builder.HasIndex(o => new { o.TenantId, o.OrderNumber }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.CustomerUserId });
        builder.HasIndex(o => new { o.TenantId, o.TemporaryCustomerId });
        // A device's key identifies one sale per business: retries return it instead of creating another.
        builder.HasIndex(o => new { o.TenantId, o.ClientRequestId })
            .IsUnique()
            .HasFilter("[ClientRequestId] IS NOT NULL")
            .HasDatabaseName("IX_Orders_TenantId_ClientRequestId");
        builder.HasIndex(o => new { o.TenantId, o.OpenTableId })
            .IsUnique()
            .HasFilter("[OpenTableId] IS NOT NULL")
            .HasDatabaseName("IX_Orders_TenantId_OpenTableId_Open");
    }
}
