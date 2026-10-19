using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Reporting.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches", table =>
        {
            table.HasCheckConstraint("CK_Branches_ServiceMode", "[ServiceMode] IN (0, 1)");
            // A point on the map has both halves, in range, and is never 0, 0 (GeoPoint): never one without the other.
            table.HasCheckConstraint(
                "CK_Branches_Location",
                "([Latitude] IS NULL AND [Longitude] IS NULL) OR ([Latitude] IS NOT NULL AND [Longitude] IS NOT NULL"
                + " AND [Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180"
                + " AND NOT ([Latitude] = 0 AND [Longitude] = 0))");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(Branch.MaxNameLength).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(Branch.MaxCodeLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.AddressLine1).HasMaxLength(250);
        builder.Property(x => x.AddressLine2).HasMaxLength(250);
        builder.Property(x => x.City).HasMaxLength(120);
        builder.Property(x => x.State).HasMaxLength(120);
        builder.Property(x => x.Country).HasMaxLength(120);
        builder.Property(x => x.PostalCode).HasMaxLength(20);
        builder.Property(x => x.ContactName).HasMaxLength(120);
        builder.Property(x => x.ContactPhone).HasMaxLength(30);
        builder.Property(x => x.IsDefault).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        // EF always writes it; the default fills the branches the previous version adds while this one is published
        // (it knows only branches with premises).
        builder.Property(x => x.ServiceMode)
            .HasConversion<int>()
            .HasDefaultValue(BranchServiceMode.Premises)
            .ValueGeneratedNever()
            .IsRequired();

        // Six decimals, about 11 cm (GeoPoint.Decimals).
        builder.Property(x => x.Latitude).HasPrecision(9, GeoPoint.Decimals);
        builder.Property(x => x.Longitude).HasPrecision(9, GeoPoint.Decimals);
        builder.Ignore(x => x.Location);
        builder.Ignore(x => x.PublicLocation);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IsActive });

        // Business rule: one default branch per business, enforced by the database.
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasFilter("[IsDefault] = 1")
            .HasDatabaseName("IX_Branches_TenantId_Default");
    }
}

public class DailySalesSnapshotConfiguration : IEntityTypeConfiguration<DailySalesSnapshot>
{
    public void Configure(EntityTypeBuilder<DailySalesSnapshot> builder)
    {
        builder.ToTable("DailySalesSnapshots");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.BusinessDate).IsRequired();
        builder.Property(x => x.OrderType).HasConversion<int>().IsRequired();
        builder.Property(x => x.TimeZoneId).HasMaxLength(DailySalesSnapshot.MaxTimeZoneIdLength).IsRequired();
        builder.Property(x => x.ComputedAt).IsRequired();
        builder.Property(x => x.PaymentsAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CashAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CardAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TransferAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.WalletAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.OtherAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RefundsAmount).HasColumnType("decimal(18,2)");
        // EF always writes it; the default fills the rows the previous version stores while this one is published
        // (it does not know the «Plataforma» method, so 0 is what those days had).
        builder.Property(x => x.PlatformAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m).ValueGeneratedNever();
        // The same for what customers paid online (Fase 24I): the previous version does not know the «En línea» method.
        builder.Property(x => x.OnlineAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0m).ValueGeneratedNever();
        builder.Ignore(x => x.Figures);

        // One row per day, branch and kind of order. A null branch is one more bucket: without the filter SQL Server
        // treats nulls as equal in a unique index, so concurrent passes cannot store a day twice.
        builder.HasIndex(x => new { x.TenantId, x.BusinessDate, x.BranchId, x.OrderType })
            .IsUnique()
            .HasFilter(null);
    }
}
