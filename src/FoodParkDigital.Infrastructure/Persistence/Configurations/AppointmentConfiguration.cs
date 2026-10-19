using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments", table =>
        {
            // Fase 24D (ADR 0025): who booked it, and the link of one booked online with the length the domain gives it.
            table.HasCheckConstraint("CK_Appointments_Channel", "[Channel] IN (0, 1)");
            table.HasCheckConstraint(
                "CK_Appointments_TrackingToken",
                $"[TrackingToken] IS NULL OR LEN([TrackingToken]) = {Order.TrackingTokenLength}");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CustomerPhone).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ServiceName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StaffName).HasMaxLength(Appointment.MaxStaffNameLength);
        builder.Property(x => x.StartAt).IsRequired();
        builder.Property(x => x.EndAt).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Version).IsRowVersion();
        builder.Ignore(x => x.IsHomeVisit);
        builder.Ignore(x => x.IsRequest);
        // Fase 24D: the database default fills the rows that exist and those the previous version saves while this one is
        // published (all of them the team's); EF always writes the value.
        builder.Property(x => x.Channel)
            .HasConversion<int>()
            .HasDefaultValue(Domain.Appointments.Enums.AppointmentChannel.Business)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(x => x.TrackingToken).HasMaxLength(Order.TrackingTokenLength).IsUnicode(false);

        // The customer account that booked it online; the appointment stays while it points to the account.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.CustomerUserId)
            .OnDelete(DeleteBehavior.Restrict);

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
        // The account's appointments in every business, by time (Fase 24D).
        builder.HasIndex(x => new { x.CustomerUserId, x.StartAt })
            .HasFilter("[CustomerUserId] IS NOT NULL");
        // The same request sent twice (a retry after a lost answer) is one appointment: the second insert collides.
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId })
            .IsUnique()
            .HasFilter("[ClientRequestId] IS NOT NULL")
            .HasDatabaseName("IX_Appointments_TenantId_ClientRequestId");
        builder.HasIndex(x => x.TrackingToken)
            .IsUnique()
            .HasFilter("[TrackingToken] IS NOT NULL")
            .HasDatabaseName("IX_Appointments_TrackingToken");
    }
}
