using FoodParkDigital.Domain.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.ToTable("CashSessions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OpenedAt).IsRequired();
        builder.Property(x => x.OpenedBy).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OpeningAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.ClosedAt);
        builder.Property(x => x.ClosedBy).HasMaxLength(200);
        builder.Property(x => x.ClosingAmount).HasColumnType("decimal(18,4)");
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.ExpectedClosingAmount).HasColumnType("decimal(18,4)");
        builder.Property(x => x.ClosingDifference).HasColumnType("decimal(18,4)");
        builder.Property(x => x.ClosingNotes).HasMaxLength(2000);
        builder.Property(x => x.CashCountJson).HasMaxLength(4000);
        builder.Property(x => x.Revision).IsConcurrencyToken();

        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.OpenedAt });
        builder.HasIndex(x => new { x.TenantId, x.InventoryLocationId, x.OpenedAt });
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.OpenedAt });

        // Business rule: each cashier has at most one open drawer, enforced by the database. Drawers opened
        // before per-cashier sessions have no owner and are not covered (there was at most one per tenant).
        builder.HasIndex(x => new { x.TenantId, x.OpenedByUserId })
            .IsUnique()
            .HasFilter("[Status] = 0 AND [OpenedByUserId] IS NOT NULL")
            .HasDatabaseName("IX_CashSessions_TenantId_OpenedByUserId_Open");

        builder.HasOne<FoodParkDigital.Domain.Branches.Entities.Branch>()
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FoodParkDigital.Domain.Inventory.Entities.InventoryLocation>()
            .WithMany()
            .HasForeignKey(x => x.InventoryLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Transactions)
            .WithOne(x => x.CashSession)
            .HasForeignKey(x => x.CashSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Transactions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
