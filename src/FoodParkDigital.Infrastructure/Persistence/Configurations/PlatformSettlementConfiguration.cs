using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Platforms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class PlatformSettlementConfiguration : IEntityTypeConfiguration<PlatformSettlement>
{
    public void Configure(EntityTypeBuilder<PlatformSettlement> builder)
    {
        // Only what any settlement keeps: the limits of policy (a month of period, ten million deposited) live in
        // PlatformSettlement and can change without a migration.
        builder.ToTable("PlatformSettlements", table =>
        {
            table.HasCheckConstraint("CK_PlatformSettlements_Channel", "[Channel] IN (3, 4, 5)");
            table.HasCheckConstraint("CK_PlatformSettlements_Status", "[Status] IN (0, 1, 99)");
            table.HasCheckConstraint("CK_PlatformSettlements_Dates", "[PeriodTo] >= [PeriodFrom] AND [DepositedOn] >= [PeriodFrom]");
            table.HasCheckConstraint("CK_PlatformSettlements_Closed", "[Status] <> 1 OR [ClosedAt] IS NOT NULL");
            table.HasCheckConstraint("CK_PlatformSettlements_Voided", "[Status] <> 99 OR ([VoidedAt] IS NOT NULL AND [VoidReason] IS NOT NULL)");
            table.HasCheckConstraint("CK_PlatformSettlements_Revision", "[Revision] >= 1");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Channel).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.PeriodFrom).IsRequired();
        builder.Property(x => x.PeriodTo).IsRequired();
        builder.Property(x => x.DepositedOn).IsRequired();
        builder.Property(x => x.DepositedAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(PlatformSettlement.MaxReferenceLength);
        builder.Property(x => x.Notes).HasMaxLength(PlatformSettlement.MaxNotesLength);
        builder.Property(x => x.OrdersTotal).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.AdjustmentsTotal).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.VoidReason).HasMaxLength(PlatformSettlement.MaxVoidReasonLength);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Ignore(x => x.ExpectedDeposit);
        builder.Ignore(x => x.Difference);

        // One open settlement per platform and business, also when two people open one at the same time.
        builder.HasIndex(x => new { x.TenantId, x.Channel })
            .IsUnique()
            .HasFilter("[Status] = 0")
            .HasDatabaseName("IX_PlatformSettlements_TenantId_Channel_Open");
        // The list, the newest period first.
        builder.HasIndex(x => new { x.TenantId, x.PeriodTo });

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

public class PlatformSettlementOrderConfiguration : IEntityTypeConfiguration<PlatformSettlementOrder>
{
    public void Configure(EntityTypeBuilder<PlatformSettlementOrder> builder)
    {
        builder.ToTable("PlatformSettlementOrders");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ExpectedNet).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.IsActive).IsRequired();

        // A settled order is history: it is never deleted from under its settlement.
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // An order is in one settlement in force at most, also when two saves choose it at the same time. A voided
        // settlement keeps its rows with IsActive = 0, outside the index.
        builder.HasIndex(x => x.OrderId)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("IX_PlatformSettlementOrders_OrderId_Active");
        // What a business still has pending reads the orders of its settlements in force.
        builder.HasIndex(x => new { x.TenantId, x.OrderId });
    }
}

public class PlatformSettlementAdjustmentConfiguration : IEntityTypeConfiguration<PlatformSettlementAdjustment>
{
    public void Configure(EntityTypeBuilder<PlatformSettlementAdjustment> builder)
    {
        // A known reason and an amount that says something; its limits (a million either way) live in the domain.
        builder.ToTable("PlatformSettlementAdjustments", table =>
        {
            table.HasCheckConstraint("CK_PlatformSettlementAdjustments_Kind", "[Kind] IN (1, 2, 3, 4, 5, 99)");
            table.HasCheckConstraint("CK_PlatformSettlementAdjustments_Amount", "[Amount] <> 0");
            table.HasCheckConstraint("CK_PlatformSettlementAdjustments_Other", "[Kind] <> 99 OR [Description] IS NOT NULL");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Position).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>().IsRequired();
        builder.Property(x => x.Description).HasMaxLength(PlatformSettlementAdjustment.MaxDescriptionLength);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        // The place of each adjustment, which the domain numbers on every save, without an index: a unique one would make
        // two saves of the same settlement collide on it before the revision tells who saved first.
    }
}
