using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OrderVisitChargeConfiguration : IEntityTypeConfiguration<OrderVisitCharge>
{
    public void Configure(EntityTypeBuilder<OrderVisitCharge> builder)
    {
        // What the domain writes, also for a row written by hand: a charge above zero that a zone can make, and a rate.
        builder.ToTable("OrderVisitCharges", table =>
        {
            table.HasCheckConstraint("CK_OrderVisitCharges_Fee", "[Fee] > 0 AND [Fee] <= 10000");
            table.HasCheckConstraint("CK_OrderVisitCharges_FeeTaxRate", "[FeeTaxRate] >= 0 AND [FeeTaxRate] <= 100");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.AppointmentId).IsRequired();
        builder.Property(x => x.ZoneName).HasMaxLength(DeliveryZone.MaxNameLength).IsRequired();
        builder.Property(x => x.Fee).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.FeeTaxRate).HasColumnType("decimal(5,2)").IsRequired();
        builder.Ignore(x => x.FeeVatRate);

        // One visit charge per order (the relationship with the order lives in OrderConfiguration); the appointment stays
        // while a sale charges it.
        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.AppointmentId);
    }
}
