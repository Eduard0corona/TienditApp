using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class AppointmentVisitConfiguration : IEntityTypeConfiguration<AppointmentVisit>
{
    public void Configure(EntityTypeBuilder<AppointmentVisit> builder)
    {
        // What the domain writes, also for a row written by hand: a postal code of five digits, an address and a charge a
        // zone can make.
        builder.ToTable("AppointmentVisits", table =>
        {
            table.HasCheckConstraint(
                "CK_AppointmentVisits_PostalCode", "LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'");
            table.HasCheckConstraint("CK_AppointmentVisits_AddressLine", "LEN([AddressLine]) > 0");
            table.HasCheckConstraint("CK_AppointmentVisits_Fee", "[Fee] >= 0 AND [Fee] <= 10000");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AppointmentId).IsRequired();
        builder.Property(x => x.PostalCode).HasMaxLength(DeliveryZonePostalCode.Length).IsUnicode(false).IsRequired();
        builder.Property(x => x.AddressLine).HasMaxLength(DeliveryAddress.MaxAddressLineLength).IsRequired();
        builder.Property(x => x.References).HasMaxLength(DeliveryAddress.MaxReferencesLength);
        builder.Property(x => x.ZoneName).HasMaxLength(DeliveryZone.MaxNameLength).IsRequired();
        builder.Property(x => x.Fee).HasColumnType("decimal(18,2)").IsRequired();

        // One visit per appointment (the relationship lives in AppointmentConfiguration).
        builder.HasIndex(x => x.AppointmentId).IsUnique();
    }
}
