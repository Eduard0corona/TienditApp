using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class CourierSettlementConfiguration : IEntityTypeConfiguration<CourierSettlement>
{
    public void Configure(EntityTypeBuilder<CourierSettlement> builder)
    {
        // Only what any settlement keeps (Fase 22D): a known courier, cash or a transfer, and what was handed over adding
        // up to its orders and adjustments. The limits of policy (200 orders, a million) live in CourierSettlement.
        builder.ToTable("CourierSettlements", table =>
        {
            table.HasCheckConstraint("CK_CourierSettlements_Courier", "[Courier] IN (1)");
            table.HasCheckConstraint("CK_CourierSettlements_Method", "[Method] IN (0, 2)");
            table.HasCheckConstraint("CK_CourierSettlements_HandedAmount", "[HandedAmount] >= 0");
            table.HasCheckConstraint("CK_CourierSettlements_Balances", "[HandedAmount] = [OrdersTotal] + [AdjustmentsTotal]");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Courier).HasConversion<int>().IsRequired();
        builder.Property(x => x.CashSessionId).IsRequired();
        builder.Property(x => x.Method).HasConversion<int>().IsRequired();
        builder.Property(x => x.HandedAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.OrdersTotal).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.AdjustmentsTotal).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(CourierSettlement.MaxReferenceLength);
        builder.Property(x => x.Notes).HasMaxLength(CourierSettlement.MaxNotesLength);
        builder.Property(x => x.SettledAt).IsRequired();
        builder.Property(x => x.SettledBy).HasMaxLength(256);

        // The drawer the money entered is history: it is never deleted from under the settlement.
        builder.HasOne<CashSession>()
            .WithMany()
            .HasForeignKey(x => x.CashSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        // The list, the newest first.
        builder.HasIndex(x => new { x.TenantId, x.SettledAt });

        builder.HasMany(x => x.Orders)
            .WithOne()
            .HasForeignKey(x => x.SettlementId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Orders).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Adjustments)
            .WithOne()
            .HasForeignKey(x => x.SettlementId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Adjustments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class CourierSettlementOrderConfiguration : IEntityTypeConfiguration<CourierSettlementOrder>
{
    public void Configure(EntityTypeBuilder<CourierSettlementOrder> builder)
    {
        builder.ToTable("CourierSettlementOrders", table =>
            table.HasCheckConstraint("CK_CourierSettlementOrders_Amount", "[Amount] > 0"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();

        // A settled order is history: it is never deleted from under its settlement.
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // The courier hands an order over once, also when two settlements choose it at the same time.
        builder.HasIndex(x => x.OrderId).IsUnique();
        // What a courier still owes reads the orders already settled.
        builder.HasIndex(x => new { x.TenantId, x.OrderId });
    }
}

public class CourierSettlementAdjustmentConfiguration : IEntityTypeConfiguration<CourierSettlementAdjustment>
{
    public void Configure(EntityTypeBuilder<CourierSettlementAdjustment> builder)
    {
        // A known reason and an amount that says something; a trip, a fee or a shortage is deducted (negative).
        builder.ToTable("CourierSettlementAdjustments", table =>
        {
            table.HasCheckConstraint("CK_CourierSettlementAdjustments_Kind", "[Kind] IN (1, 2, 3, 99)");
            table.HasCheckConstraint("CK_CourierSettlementAdjustments_Amount", "[Amount] <> 0 AND ([Kind] = 99 OR [Amount] < 0)");
            table.HasCheckConstraint("CK_CourierSettlementAdjustments_Other", "[Kind] <> 99 OR [Description] IS NOT NULL");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Position).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>().IsRequired();
        builder.Property(x => x.Description).HasMaxLength(CourierSettlementAdjustment.MaxDescriptionLength);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
    }
}
