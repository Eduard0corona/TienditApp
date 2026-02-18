using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class ServiceOrderPieceConfiguration : IEntityTypeConfiguration<ServiceOrderPiece>
{
    public void Configure(EntityTypeBuilder<ServiceOrderPiece> builder)
    {
        builder.ToTable("ServiceOrderPieces");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.ItemName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Brand).HasMaxLength(120);
        builder.Property(x => x.Model).HasMaxLength(120);
        builder.Property(x => x.Color).HasMaxLength(50);
        builder.Property(x => x.Size).HasMaxLength(30);
        builder.Property(x => x.Material).HasMaxLength(80);
        builder.Property(x => x.ConditionNotes).HasMaxLength(1000);
        builder.Property(x => x.RequestedService).HasMaxLength(300);
        builder.Property(x => x.InternalNotes).HasMaxLength(1000);
        builder.Property(x => x.ServiceProductId);
        builder.Property(x => x.IsTrackable).HasDefaultValue(true);
        builder.Property(x => x.NotifyCustomer).HasDefaultValue(false);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.OrderId });
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.IsTrackable });
    }
}
