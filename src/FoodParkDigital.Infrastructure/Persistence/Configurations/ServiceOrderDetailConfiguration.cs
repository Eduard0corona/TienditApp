using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class ServiceOrderDetailConfiguration : IEntityTypeConfiguration<ServiceOrderDetail>
{
    public void Configure(EntityTypeBuilder<ServiceOrderDetail> builder)
    {
        builder.ToTable("ServiceOrderDetails");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.ReceivedAt).IsRequired();
        builder.Property(x => x.Priority).IsRequired();
        builder.Property(x => x.IntakeNotes).HasMaxLength(2000);
        builder.Property(x => x.DeliveryNotes).HasMaxLength(2000);
        builder.Property(x => x.IntakeChecklistJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.BeforePhotosJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.AfterPhotosJson).HasColumnType("nvarchar(max)");

        builder.HasIndex(x => x.OrderId).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.PromisedAt });
    }
}
