using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.Platforms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OrderDeliveryConfiguration : IEntityTypeConfiguration<OrderDelivery>
{
    public void Configure(EntityTypeBuilder<OrderDelivery> builder)
    {
        // What the domain writes, also for a row written by hand: a postal code of five digits, an address, a fee a zone
        // can charge and a rate, and the courier's trip whole or not at all, with a secure link.
        builder.ToTable("OrderDeliveries", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderDeliveries_PostalCode", "LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'");
            table.HasCheckConstraint("CK_OrderDeliveries_AddressLine", "LEN([AddressLine]) > 0");
            table.HasCheckConstraint("CK_OrderDeliveries_Fee", "[Fee] >= 0 AND [Fee] <= 10000");
            table.HasCheckConstraint("CK_OrderDeliveries_FeeTaxRate", "[FeeTaxRate] >= 0 AND [FeeTaxRate] <= 100");
            // IS NOT NULL on each column of the second branch: a CHECK lets UNKNOWN through, and [Courier] = 1 is UNKNOWN
            // for a null courier, so without it a link with no courier, or a courier with no folio, would pass.
            table.HasCheckConstraint(
                "CK_OrderDeliveries_Shipment",
                "([Courier] IS NULL AND [CourierFolio] IS NULL AND [TrackingUrl] IS NULL AND [ShipmentRegisteredAt] IS NULL) "
                + "OR ([Courier] IS NOT NULL AND [Courier] = 1 AND [CourierFolio] IS NOT NULL AND LEN([CourierFolio]) > 0 "
                + "AND [ShipmentRegisteredAt] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_OrderDeliveries_TrackingUrl", "[TrackingUrl] IS NULL OR [TrackingUrl] LIKE 'https://_%'");
            // Fase 22D: a bill above zero; an order goes out with its trip, and the courier collects only what went out.
            table.HasCheckConstraint("CK_OrderDeliveries_PaysWith", "[PaysWith] IS NULL OR ([PaysWith] > 0 AND [PaysWith] <= 100000)");
            table.HasCheckConstraint("CK_OrderDeliveries_Dispatch", "[DispatchedAt] IS NULL OR [Courier] IS NOT NULL");
            table.HasCheckConstraint(
                "CK_OrderDeliveries_CashToCollect", "[CashToCollect] IS NULL OR ([CashToCollect] > 0 AND [DispatchedAt] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.PostalCode).HasMaxLength(DeliveryZonePostalCode.Length).IsUnicode(false).IsRequired();
        builder.Property(x => x.AddressLine).HasMaxLength(DeliveryAddress.MaxAddressLineLength).IsRequired();
        builder.Property(x => x.References).HasMaxLength(DeliveryAddress.MaxReferencesLength);
        builder.Property(x => x.ZoneName).HasMaxLength(DeliveryZone.MaxNameLength).IsRequired();
        builder.Property(x => x.Fee).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.FeeTaxRate).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.Courier).HasConversion<int?>();
        builder.Property(x => x.CourierFolio).HasMaxLength(PlatformOrderNumber.MaxLength).IsUnicode(false);
        builder.Property(x => x.TrackingUrl).HasMaxLength(CourierShipment.MaxTrackingUrlLength);
        builder.Property(x => x.ShipmentRegisteredBy).HasMaxLength(256);
        builder.Property(x => x.PaysWith).HasColumnType("decimal(18,2)");
        builder.Property(x => x.DispatchedBy).HasMaxLength(256);
        builder.Property(x => x.CashToCollect).HasColumnType("decimal(18,2)");
        builder.Ignore(x => x.FeeVatRate);
        builder.Ignore(x => x.IsDispatched);

        // One delivery per order (the relationship lives in OrderConfiguration).
        builder.HasIndex(x => x.OrderId).IsUnique();
    }
}
