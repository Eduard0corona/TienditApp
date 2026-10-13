using FoodParkDigital.Domain.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class DeliveryZoneConfiguration : IEntityTypeConfiguration<DeliveryZone>
{
    public void Configure(EntityTypeBuilder<DeliveryZone> builder)
    {
        // The limits the domain keeps, also for a row written by hand: amounts that can be charged and a free delivery
        // only where the delivery costs something.
        builder.ToTable("DeliveryZones", table =>
        {
            table.HasCheckConstraint("CK_DeliveryZones_Fee", "[Fee] >= 0 AND [Fee] <= 10000");
            table.HasCheckConstraint("CK_DeliveryZones_MinimumOrder", "[MinimumOrder] >= 0 AND [MinimumOrder] <= 100000");
            table.HasCheckConstraint(
                "CK_DeliveryZones_FreeDeliveryFrom",
                "[FreeDeliveryFrom] IS NULL OR ([FreeDeliveryFrom] > 0 AND [FreeDeliveryFrom] <= 100000 AND [Fee] > 0)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(DeliveryZone.MaxNameLength).IsRequired();
        builder.Property(x => x.Fee).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.MinimumOrder).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.FreeDeliveryFrom).HasColumnType("decimal(18,2)");
        builder.Property(x => x.IsActive).IsRequired();

        // The list, by name.
        builder.HasIndex(x => new { x.TenantId, x.Name });

        builder.HasMany(x => x.PostalCodes)
            .WithOne()
            .HasForeignKey(x => x.DeliveryZoneId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.PostalCodes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class DeliveryZonePostalCodeConfiguration : IEntityTypeConfiguration<DeliveryZonePostalCode>
{
    public void Configure(EntityTypeBuilder<DeliveryZonePostalCode> builder)
    {
        builder.ToTable("DeliveryZonePostalCodes", table =>
            table.HasCheckConstraint(
                "CK_DeliveryZonePostalCodes_PostalCode",
                "LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.PostalCode).HasMaxLength(DeliveryZonePostalCode.Length).IsUnicode(false).IsRequired();

        // A postal code is in one zone of the business at most, also when two saves add it at the same time; finding the
        // zone of a postal code reads this index.
        builder.HasIndex(x => new { x.TenantId, x.PostalCode }).IsUnique();
    }
}
