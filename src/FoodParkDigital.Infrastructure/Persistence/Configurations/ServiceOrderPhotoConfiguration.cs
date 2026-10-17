using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>The photos added to a service order after it was received (Fase 25, ADR 0024), one row each.</summary>
public class ServiceOrderPhotoConfiguration : IEntityTypeConfiguration<ServiceOrderPhoto>
{
    public void Configure(EntityTypeBuilder<ServiceOrderPhoto> builder)
    {
        builder.ToTable("ServiceOrderPhotos", table =>
            table.HasCheckConstraint("CK_ServiceOrderPhotos_Stage", "[Stage] IN (0, 1)"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.Stage).IsRequired();
        builder.Property(x => x.FileUrl).IsRequired().HasMaxLength(ServiceOrderPhoto.MaxFileUrlLength);
        builder.Property(x => x.FileName).HasMaxLength(ServiceOrderPhoto.MaxFileNameLength);
        builder.Property(x => x.Note).HasMaxLength(ServiceOrderPhoto.MaxNoteLength);
        builder.Property(x => x.TakenAt).IsRequired();
        builder.Property(x => x.TakenBy).HasMaxLength(ServiceOrderPhoto.MaxTakenByLength);

        // The photos of an order, in the order they were taken.
        builder.HasIndex(x => new { x.TenantId, x.OrderId, x.TakenAt });
    }
}
