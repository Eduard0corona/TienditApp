using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Platforms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class PlatformOrderDetailConfiguration : IEntityTypeConfiguration<PlatformOrderDetail>
{
    public void Configure(EntityTypeBuilder<PlatformOrderDetail> builder)
    {
        // Mirror what the domain writes, with comparisons against 0 only: an arithmetic identity (the expected
        // settlement equal to the total minus the rest) would compare decimals as REAL in the SQLite of the relational
        // tests. The domain and the SQL Server tests keep that identity. When the business charged, nothing is
        // withheld; the folio is never empty (LEN, not <> '', as in SpecialDays).
        builder.ToTable("PlatformOrderDetails", table =>
        {
            table.HasCheckConstraint("CK_PlatformOrderDetails_Channel", "[Channel] IN (3, 4, 5)");
            table.HasCheckConstraint("CK_PlatformOrderDetails_CollectedBy", "[CollectedBy] IN (0, 1)");
            table.HasCheckConstraint("CK_PlatformOrderDetails_CommissionBase", "[CommissionBase] IN (0, 1)");
            table.HasCheckConstraint("CK_PlatformOrderDetails_Number", "LEN([ExternalOrderNumber]) > 0");
            table.HasCheckConstraint("CK_PlatformOrderDetails_ExternalOrderId", "[ExternalOrderId] IS NULL OR LEN([ExternalOrderId]) >= 6");
            table.HasCheckConstraint(
                "CK_PlatformOrderDetails_Percents",
                "[CommissionPercent] BETWEEN 0 AND 100 AND [CommissionVatPercent] BETWEEN 0 AND 100 AND [IsrWithholdingPercent] BETWEEN 0 AND 100 AND [VatWithholdingPercent] BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "CK_PlatformOrderDetails_Amounts",
                "[OrderTotal] > 0 AND [Commission] >= 0 AND [CommissionTax] >= 0 AND [IsrWithheld] >= 0 AND [VatWithheld] >= 0");
            table.HasCheckConstraint(
                "CK_PlatformOrderDetails_BusinessCollected",
                "[CollectedBy] = 0 OR ([IsrWithheld] = 0 AND [VatWithheld] = 0)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.Channel).HasConversion<int>().IsRequired();
        builder.Property(x => x.ExternalOrderNumber).HasMaxLength(PlatformOrderNumber.MaxLength).IsRequired();
        builder.Property(x => x.ExternalOrderId).HasMaxLength(PlatformOrderId.MaxLength);
        builder.Property(x => x.CollectedBy).HasConversion<int>().IsRequired();
        builder.Property(x => x.SettingsRevision).IsRequired();
        builder.Property(x => x.CommissionPercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.CommissionBase).HasConversion<int>().IsRequired();
        builder.Property(x => x.CommissionVatPercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.IsrWithholdingPercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.VatWithholdingPercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.OrderTotal).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Commission).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CommissionTax).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.IsrWithheld).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.VatWithheld).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.ExpectedSettlement).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.RegisteredAt).IsRequired();

        // One detail per order (the relationship lives in OrderConfiguration). Not unique by folio: Uber Eats' 5
        // characters and DiDi's pickup number repeat, so a repeated folio is a warning when the order is placed,
        // found through this index, as the search by folio is.
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Channel, x.ExternalOrderNumber, x.RegisteredAt });
        // The platform's long id (Fase 22I): a statement finds the order by it, and a repeated one is a warning too, not a
        // unique index (an order captured twice is corrected by cancelling one, as with the folio).
        builder.HasIndex(x => new { x.TenantId, x.Channel, x.ExternalOrderId })
            .HasFilter("[ExternalOrderId] IS NOT NULL");
        // The orders of a platform in a period, to compare with its weekly statement.
        builder.HasIndex(x => new { x.TenantId, x.RegisteredAt });
    }
}
