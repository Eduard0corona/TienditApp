using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CustomerPhone).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ServiceName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StaffName).HasMaxLength(120);
        builder.Property(x => x.StartAt).IsRequired();
        builder.Property(x => x.EndAt).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Version).IsRowVersion();
        builder.Ignore(x => x.IsHomeVisit);

        // Where it happens when it is at the customer's address (Fase 22E). Always loaded with the appointment: the agenda,
        // the travel time between appointments and its sale need it.
        builder.HasOne(x => x.Visit)
            .WithOne()
            .HasForeignKey<AppointmentVisit>(v => v.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Visit).AutoInclude();

        // The sale it was charged with; the order stays while an appointment points to it.
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.SaleOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.StartAt });
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.CustomerPhone });
        // Overlaps are checked per team member.
        builder.HasIndex(x => new { x.TenantId, x.StaffUserId, x.StartAt });
        builder.HasIndex(x => x.SaleOrderId);
    }
}
