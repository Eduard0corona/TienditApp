using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The photos of what happens a customer sends with a visit requested online (Fase 29H, ADR 0062), one row each. The base
/// keeps each place of a visit once and none past the last, so photos sent at the same time never make more than
/// <see cref="HomeVisitRules.MaxProblemPhotos"/>, even if the counts read before saving agreed.
/// </summary>
public class AppointmentProblemPhotoConfiguration : IEntityTypeConfiguration<AppointmentProblemPhoto>
{
    public void Configure(EntityTypeBuilder<AppointmentProblemPhoto> builder)
    {
        builder.ToTable("AppointmentProblemPhotos", table =>
            table.HasCheckConstraint(
                "CK_AppointmentProblemPhotos_Position", $"[Position] >= 0 AND [Position] < {HomeVisitRules.MaxProblemPhotos}"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AppointmentId).IsRequired();
        builder.Property(x => x.Position).IsRequired();
        builder.Property(x => x.FileKey).HasMaxLength(AppointmentProblemPhoto.MaxFileKeyLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.AddedAt).IsRequired();

        // Its appointment, which goes with its photos; a photo never changes it (no navigation, no version).
        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Each place once per visit, in the order they were sent.
        builder.HasIndex(x => new { x.AppointmentId, x.Position }).IsUnique();
        // A private file opens only when a photo of the business keeps it.
        builder.HasIndex(x => new { x.TenantId, x.FileKey }).IsUnique();
    }
}
